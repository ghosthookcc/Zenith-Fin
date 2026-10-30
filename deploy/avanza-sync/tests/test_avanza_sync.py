import io
import json
import logging
import unittest
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from unittest import mock

import requests
from nacl.public import PrivateKey, SealedBox

import avanza_sync
from avanza_sync import AuthFailed, Credential, SyncStatus

NOW = datetime(2026, 11, 2, 6, 30, tzinfo=timezone.utc)
SECRETS = {"username": "fejkanvandare-77", "password": "hemligt-pw-123!", "totpSecret": "JBSWY3DPEHPK3PXP"}

OVERVIEW = {
    "categories": [{"id": "c1", "name": "Sparande", "totalValue": {"value": 1000.0, "unit": "SEK", "unitType": "MONETARY", "decimalPrecision": 2}}],
    "accounts": [
        {
            "id": "1234567",
            "categoryId": "c1",
            "type": "INVESTERINGSSPARKONTO",
            "name": {"defaultName": "ISK", "userDefinedName": "Långsiktigt"},
            "balance": {"value": 12.5, "unit": "SEK", "unitType": "MONETARY", "decimalPrecision": 2},
            "totalValue": {"value": 123456.785, "unit": "SEK", "unitType": "MONETARY", "decimalPrecision": 2},
            "errorStatus": None,
            "somethingNew": {"nested": True},
        },
        {
            "id": "7654321",
            "categoryId": "c1",
            "type": "KAPITALFORSAKRING",
            "name": {"defaultName": "Kapitalförsäkring", "userDefinedName": None},
            "totalValue": {"value": 0, "unit": "SEK", "unitType": "MONETARY", "decimalPrecision": 2},
        },
    ],
    "loans": [],
}


class FakeStore:
    def __init__(self, credentials, statuses=None):
        self._credentials = credentials
        self.statuses = dict(statuses or {})
        self.attempts = []
        self.failures = []
        self.saved = []

    def credentials(self):
        return self._credentials

    def status(self, user_id):
        return self.statuses.get(user_id, SyncStatus(None, None))

    def mark_attempt(self, user_id, now):
        self.attempts.append((user_id, now))
        current = self.status(user_id)
        self.statuses[user_id] = SyncStatus(current.status, now)

    def mark_failure(self, user_id, status, error_class):
        self.failures.append((user_id, status, error_class))
        current = self.status(user_id)
        self.statuses[user_id] = SyncStatus(status, current.last_attempt_at)

    def save(self, user_id, snapshots, now):
        self.saved.append((user_id, snapshots, now))
        self.statuses[user_id] = SyncStatus("OK", self.status(user_id).last_attempt_at)


class FakeClient:
    def __init__(self, overview=None, error=None):
        self.overview = overview if overview is not None else OVERVIEW
        self.error = error
        self.calls = 0

    def get_overview(self):
        self.calls += 1

        if self.error:
            raise self.error

        return self.overview


class Logins:
    def __init__(self, client=None, error=None):
        self.client = client or FakeClient()
        self.error = error
        self.calls = []

    def __call__(self, secrets):
        self.calls.append(dict(secrets))

        if self.error:
            raise self.error

        return self.client


class Setup:
    def __init__(self):
        self.key = PrivateKey.generate()
        self.fingerprint = avanza_sync.fingerprint(bytes(self.key.public_key))

    def credential(self, user_id=1, updated_at=None, secrets=None, fingerprint=None):
        ciphertext = SealedBox(self.key.public_key).encrypt(json.dumps(secrets or SECRETS).encode())
        return Credential(user_id, ciphertext, fingerprint or self.fingerprint, updated_at or NOW - timedelta(days=10))


class RunTest(unittest.TestCase):
    def setUp(self):
        self.setup = Setup()
        self.stream = io.StringIO()
        self.handler = logging.StreamHandler(self.stream)
        avanza_sync.log.addHandler(self.handler)
        avanza_sync.log.setLevel(logging.INFO)

    def tearDown(self):
        avanza_sync.log.removeHandler(self.handler)

    def run_sync(self, store, logins):
        return avanza_sync.run(store, self.setup.key, self.setup.fingerprint, logins, lambda: NOW)

    def assert_no_secrets_logged(self):
        output = self.stream.getvalue()

        for secret in SECRETS.values():
            self.assertNotIn(secret, output)

    def test_saves_one_snapshot_per_account_with_one_login(self):
        store = FakeStore([self.setup.credential()])
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 0)
        self.assertEqual(len(logins.calls), 1)
        self.assertEqual(logins.calls[0], SECRETS)
        self.assertEqual(logins.client.calls, 1)
        self.assertEqual(store.attempts, [(1, NOW)])
        user, snapshots, fetched_at = store.saved[0]
        self.assertEqual((user, fetched_at), (1, NOW))
        self.assertEqual([(item.account_id, item.account_name, item.account_type, item.total_value, item.currency) for item in snapshots], [
            ("1234567", "Långsiktigt", "INVESTERINGSSPARKONTO", Decimal("123456.79"), "SEK"),
            ("7654321", "Kapitalförsäkring", "KAPITALFORSAKRING", Decimal("0.00"), "SEK"),
        ])
        self.assertEqual(store.statuses[1].status, "OK")
        self.assert_no_secrets_logged()

    def test_a_refused_login_is_not_retried(self):
        store = FakeStore([self.setup.credential()])
        logins = Logins(error=AuthFailed())

        self.assertEqual(self.run_sync(store, logins), 1)
        self.assertEqual(len(logins.calls), 1)
        self.assertEqual(store.failures, [(1, "AUTH_FAILED", "AuthFailed")])
        self.assertEqual(store.saved, [])
        self.assert_no_secrets_logged()

    def test_after_auth_failure_waits_for_new_credentials(self):
        failed_at = NOW - timedelta(days=4)
        store = FakeStore([self.setup.credential(updated_at=NOW - timedelta(days=10))], {1: SyncStatus("AUTH_FAILED", failed_at)})
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 0)
        self.assertEqual(logins.calls, [])

        store = FakeStore([self.setup.credential(updated_at=failed_at + timedelta(hours=1))], {1: SyncStatus("AUTH_FAILED", failed_at)})
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 0)
        self.assertEqual(len(logins.calls), 1)

    def test_logs_in_at_most_every_60_hours(self):
        store = FakeStore([self.setup.credential()], {1: SyncStatus("OK", NOW - timedelta(hours=59))})
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 0)
        self.assertEqual(logins.calls, [])

        store = FakeStore([self.setup.credential()], {1: SyncStatus("OK", NOW - timedelta(hours=61))})
        logins = Logins()

        self.run_sync(store, logins)
        self.assertEqual(len(logins.calls), 1)

    def test_key_mismatch_never_logs_in(self):
        store = FakeStore([self.setup.credential(fingerprint="0" * 64)])
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 1)
        self.assertEqual(logins.calls, [])
        self.assertEqual(store.attempts, [])
        self.assertEqual(store.failures, [(1, "KEY_MISMATCH", None)])

    def test_unreadable_ciphertext_never_logs_in(self):
        for ciphertext, error_class in ((b"too short", "TypeError"), (bytes(range(100)), "CryptoError")):
            store = FakeStore([Credential(1, ciphertext, self.setup.fingerprint, NOW - timedelta(days=10))])
            logins = Logins()

            self.assertEqual(self.run_sync(store, logins), 1)
            self.assertEqual(logins.calls, [])
            self.assertEqual(store.attempts, [])
            self.assertEqual(store.failures, [(1, "ERROR", error_class)])

    def test_a_failed_fetch_is_recorded_without_a_second_login(self):
        store = FakeStore([self.setup.credential()])
        logins = Logins(client=FakeClient(error=requests.exceptions.ConnectionError("network down")))

        self.assertEqual(self.run_sync(store, logins), 1)
        self.assertEqual(len(logins.calls), 1)
        self.assertEqual(logins.client.calls, 1)
        self.assertEqual(store.failures, [(1, "ERROR", "ConnectionError")])
        self.assertEqual(store.attempts, [(1, NOW)])
        self.assertNotIn("network down", self.stream.getvalue())

    def test_an_unexpected_overview_is_an_error(self):
        store = FakeStore([self.setup.credential()])
        logins = Logins(client=FakeClient(overview={"accounts": [{"id": "1"}]}))

        self.assertEqual(self.run_sync(store, logins), 1)
        self.assertEqual(store.failures, [(1, "ERROR", "ValidationError")])

    def test_each_user_logs_in_once(self):
        store = FakeStore([self.setup.credential(user_id=1), self.setup.credential(user_id=2)])
        logins = Logins()

        self.assertEqual(self.run_sync(store, logins), 0)
        self.assertEqual(len(logins.calls), 2)
        self.assertEqual([user for user, _, _ in store.saved], [1, 2])


class LoginTest(unittest.TestCase):
    def test_disables_the_libraries_second_login_and_maps_401(self):
        response = requests.Response()
        response.status_code = 401
        created = []

        def refuse(credentials, **options):
            created.append(options)
            raise requests.exceptions.HTTPError(response=response)

        with mock.patch("avanza.Avanza", side_effect=refuse):
            with self.assertRaises(AuthFailed):
                avanza_sync.avanza_login(SECRETS)

        self.assertEqual(created, [{"retry_with_next_otp": False, "quiet": True}])

    def test_server_errors_are_not_auth_failures(self):
        response = requests.Response()
        response.status_code = 503

        with mock.patch("avanza.Avanza", side_effect=requests.exceptions.HTTPError(response=response)):
            with self.assertRaises(requests.exceptions.HTTPError):
                avanza_sync.avanza_login(SECRETS)

    def test_never_imports_order_functions(self):
        source = open(avanza_sync.__file__, encoding="utf-8").read()

        for name in ("place_order", "edit_order", "delete_order", "stop_loss", "OrderType"):
            self.assertNotIn(name, source)


class LoadKeyTest(unittest.TestCase):
    def test_reads_the_key_from_the_systemd_credentials_directory(self):
        import base64
        import tempfile
        from pathlib import Path

        key = PrivateKey.generate()

        with tempfile.TemporaryDirectory() as directory:
            Path(directory, avanza_sync.KEY_NAME).write_text(base64.b64encode(bytes(key)).decode() + "\n")

            with mock.patch.dict("os.environ", {"CREDENTIALS_DIRECTORY": directory}):
                self.assertEqual(bytes(avanza_sync.load_private_key()), bytes(key))

    def test_fails_without_a_credentials_directory(self):
        with mock.patch.dict("os.environ", {}, clear=True):
            with self.assertRaises(RuntimeError):
                avanza_sync.load_private_key()


if __name__ == "__main__":
    unittest.main()
