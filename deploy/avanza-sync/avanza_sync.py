import base64
import hashlib
import json
import logging
import os
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path
from typing import Callable, Optional

from pydantic import BaseModel

MIN_INTERVAL = timedelta(hours=60)
KEY_NAME = "avanza-sync-key"
DATABASE = "dbname=zenith host=/var/run/postgresql"
CURRENCY = re.compile(r"[A-Z]{3}")
AUTH_STATUS_CODES = {400, 401, 403}

log = logging.getLogger("avanza-sync")


class AuthFailed(Exception):
    pass


class AccountName(BaseModel):
    defaultName: str
    userDefinedName: Optional[str] = None


class Value(BaseModel):
    value: float
    unit: str


class OverviewAccount(BaseModel):
    id: str
    type: Optional[str] = None
    name: AccountName
    totalValue: Value


class OverviewAccounts(BaseModel):
    accounts: list[OverviewAccount]


@dataclass(frozen=True)
class Credential:
    user_id: int
    ciphertext: bytes
    key_fingerprint: str
    updated_at: datetime


@dataclass(frozen=True)
class SyncStatus:
    status: Optional[str]
    last_attempt_at: Optional[datetime]


@dataclass(frozen=True)
class Snapshot:
    account_id: str
    account_name: str
    account_type: Optional[str]
    total_value: Decimal
    currency: str


def fingerprint(public_key: bytes) -> str:
    return hashlib.sha256(public_key).hexdigest()


def load_private_key():
    from nacl.public import PrivateKey

    directory = os.environ.get("CREDENTIALS_DIRECTORY")

    if not directory:
        raise RuntimeError("missing credentials directory")

    return PrivateKey(base64.b64decode(Path(directory, KEY_NAME).read_bytes().strip()))


def decrypt(private_key, ciphertext: bytes) -> dict:
    from nacl.public import SealedBox

    secrets = json.loads(SealedBox(private_key).decrypt(bytes(ciphertext)))

    if not all(isinstance(secrets.get(field), str) and secrets[field] for field in ("username", "password", "totpSecret")):
        raise ValueError("incomplete credentials")

    return secrets


def skip_reason(credential: Credential, status: SyncStatus, now: datetime) -> Optional[str]:
    if status.last_attempt_at is not None and now - status.last_attempt_at < MIN_INTERVAL:
        return "the last login was less than 60 hours ago"

    if status.status == "AUTH_FAILED" and status.last_attempt_at is not None and credential.updated_at <= status.last_attempt_at:
        return "the login failed, waiting for new credentials"

    return None


def snapshots_from(overview: dict) -> list[Snapshot]:
    accounts = OverviewAccounts.model_validate(overview).accounts

    return [
        Snapshot(
            account_id=account.id[:64],
            account_name=(account.name.userDefinedName or account.name.defaultName)[:200],
            account_type=account.type[:64] if account.type else None,
            total_value=Decimal(str(account.totalValue.value)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP),
            currency=account.totalValue.unit if CURRENCY.fullmatch(account.totalValue.unit) else "SEK",
        )
        for account in accounts
    ]


def avanza_login(secrets: dict):
    import requests
    from avanza import Avanza

    try:
        return Avanza(
            {
                "username": secrets["username"],
                "password": secrets["password"],
                "totpSecret": secrets["totpSecret"],
            },
            retry_with_next_otp=False,
            quiet=True,
        )
    except requests.exceptions.HTTPError as error:
        if error.response is not None and error.response.status_code in AUTH_STATUS_CODES:
            raise AuthFailed() from None

        raise


class Store:
    def __init__(self, connection):
        self.connection = connection

    def credentials(self) -> list[Credential]:
        with self.connection.cursor() as cursor:
            cursor.execute("SELECT user_id, ciphertext, key_fingerprint, updated_at FROM avanza_credentials ORDER BY user_id")
            rows = cursor.fetchall()

        self.connection.commit()

        return [Credential(int(row[0]), bytes(row[1]), row[2], row[3]) for row in rows]

    def status(self, user_id: int) -> SyncStatus:
        with self.connection.cursor() as cursor:
            cursor.execute("SELECT status::text, last_attempt_at FROM avanza_sync_status WHERE user_id = %s", (user_id,))
            row = cursor.fetchone()

        self.connection.commit()

        return SyncStatus(row[0], row[1]) if row else SyncStatus(None, None)

    def mark_attempt(self, user_id: int, now: datetime) -> None:
        with self.connection.cursor() as cursor:
            cursor.execute(
                "INSERT INTO avanza_sync_status (user_id, last_attempt_at) VALUES (%s, %s) "
                "ON CONFLICT (user_id) DO UPDATE SET last_attempt_at = EXCLUDED.last_attempt_at",
                (user_id, now),
            )

        self.connection.commit()

    def mark_failure(self, user_id: int, status: str, error_class: Optional[str]) -> None:
        with self.connection.cursor() as cursor:
            cursor.execute(
                'INSERT INTO avanza_sync_status (user_id, status, error_class) VALUES (%s, %s::"AvanzaSyncState", %s) '
                "ON CONFLICT (user_id) DO UPDATE SET status = EXCLUDED.status, error_class = EXCLUDED.error_class",
                (user_id, status, error_class),
            )

        self.connection.commit()

    def save(self, user_id: int, snapshots: list[Snapshot], now: datetime) -> None:
        with self.connection.cursor() as cursor:
            cursor.executemany(
                "INSERT INTO avanza_balance_snapshots (user_id, account_id, account_name, account_type, total_value, currency, fetched_at) "
                "VALUES (%s, %s, %s, %s, %s, %s, %s)",
                [(user_id, item.account_id, item.account_name, item.account_type, item.total_value, item.currency, now) for item in snapshots],
            )
            cursor.execute(
                'UPDATE avanza_sync_status SET status = %s::"AvanzaSyncState", error_class = NULL, last_success_at = %s WHERE user_id = %s',
                ("OK", now, user_id),
            )

        self.connection.commit()


def run(store, private_key, key_fingerprint: str, login: Callable[[dict], object], clock: Callable[[], datetime]) -> int:
    failed = False

    for credential in store.credentials():
        user = credential.user_id
        now = clock()
        reason = skip_reason(credential, store.status(user), now)

        if reason:
            log.info("user %s: skipped, %s", user, reason)
            continue

        if credential.key_fingerprint != key_fingerprint:
            store.mark_failure(user, "KEY_MISMATCH", None)
            log.warning("user %s: stored credentials were encrypted with another key", user)
            failed = True
            continue

        try:
            secrets = decrypt(private_key, credential.ciphertext)
        except Exception as error:
            store.mark_failure(user, "ERROR", type(error).__name__)
            log.error("user %s: could not read the credentials (%s)", user, type(error).__name__)
            failed = True
            continue

        store.mark_attempt(user, now)
        log.info("user %s: logging in", user)

        try:
            client = login(secrets)
            secrets = None
            log.info("user %s: fetching the overview", user)
            snapshots = snapshots_from(client.get_overview())
        except AuthFailed:
            store.mark_failure(user, "AUTH_FAILED", "AuthFailed")
            log.error("user %s: the login was refused, no new attempt until the credentials are replaced", user)
            failed = True
            continue
        except Exception as error:
            store.mark_failure(user, "ERROR", type(error).__name__)
            log.error("user %s: the fetch failed (%s)", user, type(error).__name__)
            failed = True
            continue
        finally:
            secrets = None

        store.save(user, snapshots, now)
        log.info("user %s: saved %d accounts", user, len(snapshots))

    return 1 if failed else 0


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(message)s", stream=sys.stderr)
    logging.getLogger("urllib3").setLevel(logging.WARNING)
    log.info("start")

    try:
        private_key = load_private_key()
    except Exception as error:
        log.error("could not load the private key (%s)", type(error).__name__)
        return 1

    import psycopg

    try:
        with psycopg.connect(DATABASE) as connection:
            return run(
                Store(connection),
                private_key,
                fingerprint(bytes(private_key.public_key)),
                avanza_login,
                lambda: datetime.now(timezone.utc),
            )
    except Exception as error:
        log.error("the run stopped (%s)", type(error).__name__)
        return 1
    finally:
        log.info("done")


if __name__ == "__main__":
    sys.exit(main())
