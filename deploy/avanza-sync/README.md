# Avanza values in Zenith

Zenith shows the total value of each of your Avanza accounts and how the values change over time. Avanza has no official API, so a small fetcher uses the unofficial [`avanza-api`](https://github.com/Qluxzz/avanza) library. That breaks Avanza's terms of use, so the fetcher is built to leave as small a footprint as possible: it only reads the account overview, logs in at most twice a week and never retries a refused login.

## How it fits together

```
Browser ── username, password, TOTP secret ──▶ API (.NET)
                                                 │ encrypts with the PUBLIC key (libsodium sealed box)
                                                 ▼
                                PostgreSQL: avanza_credentials (ciphertext + key fingerprint)
                                                 ▲                         │
                     reads the ciphertext,       │                         │ avanza_balance_snapshots
                     writes values and status    │                         ▼ avanza_sync_status
                                       avanza-sync.service ◀── avanza-sync.timer (Mon + Thu 07:00 ± 90 min)
                                       user avanza-sync, the only part that holds the PRIVATE key
```

- The API only has the public key (`/etc/avanza-sync/public.key`, or `Avanza:PublicKey` in its configuration). It cannot decrypt anything, and no API response contains the ciphertext or your details.
- The private key is created once by `setup.sh` and only passes through a pipe into `systemd-creds encrypt`. It is stored encrypted in `/etc/credstore.encrypted/avanza-sync-key` (bound to TPM2 when the machine has one, otherwise to the host key) and handed to the service with `LoadCredentialEncrypted=`. It never touches the disk in clear text, the repository or an environment variable.
- The fetcher runs as its own system user `avanza-sync` from `/opt/avanza-sync` (owned by root, read only for the service) and reaches PostgreSQL over the unix socket with peer authentication, so it needs no database password. Its database role can only read the credentials table, write snapshots and read and write its status.
- Every run: check the key fingerprint, decrypt, log in once (`retry_with_next_otp=False`), call `get_overview()`, store one snapshot per account, update the status, exit. It never imports order functions.
- The log only names the step and the error class. It never contains your details or anything Avanza returns.

## Install

1. Deploy Zenith as usual so the API and the database tables exist (`./deploy/deploy.sh`).
2. Run the setup script as root:

   ```bash
   sudo ./deploy/avanza-sync/setup.sh
   ```

   It checks that the clock is synchronised (TOTP codes depend on it), creates the `avanza-sync` user, installs the fetcher and its hash-locked dependencies (`pip install --require-hashes`), creates the key pair, creates the database role with its grants, installs the service and timer, and prints the `systemd-analyze security` score.
3. Get your TOTP secret from Avanza. When you add an authenticator app for two-factor login, Avanza shows a QR code and the same key as text. The text key (letters A–Z and digits 2–7) is the TOTP secret. Add it to your authenticator app as well and confirm with a code, as Avanza asks.
4. In Zenith, open **Avanza** and enter your username, password and TOTP secret. The fields are emptied after saving and can never be shown again; you can only replace or remove them.
5. Optionally fetch once right away and look at the log:

   ```bash
   sudo systemctl start avanza-sync.service
   journalctl -u avanza-sync.service -n 20 --no-pager
   ```

## Schedule and safety limits

- The timer runs on Monday and Thursday at 07:00 with up to 90 minutes of random delay. Runs missed while the computer was off are made up for once at the next start.
- The fetcher refuses to log in if the previous login attempt was less than 60 hours ago, however it was started.
- If Avanza refuses the login, the status becomes `AUTH_FAILED` and the fetcher does not try again until you save new details in Zenith. This keeps Avanza from locking the login.
- Statuses: `OK`, `AUTH_FAILED`, `ERROR` (with the error class, for example `ConnectionError`) and `KEY_MISMATCH` (the details were encrypted with another key).

## Rotate the key

```bash
sudo ./deploy/avanza-sync/setup.sh --rotate-key
```

A new key pair replaces the old one. Details saved with the old key can no longer be read (`KEY_MISMATCH`), so enter them again in Zenith afterwards.

## Turn on trade notifications in Avanza

Turn on notifications for executed orders (avslut) in the Avanza app. The fetcher never places orders, but if your details ever leak you will see an unexpected trade at once.

## Tests

```bash
cd deploy/avanza-sync
/opt/avanza-sync/venv/bin/python -I -m unittest discover -s tests -t .
```

`tests/test_interop.py` decrypts a vector that the .NET code encrypted, so both sides are proven to agree. `tests/test_avanza_sync.py` runs the fetcher against a fake Avanza and a fake database: one login per run, no retry after a refused login, the 60 hour limit, key mismatch and no secrets in the log.

## Remove it

```bash
sudo systemctl disable --now avanza-sync.timer
sudo rm -f /etc/systemd/system/avanza-sync.service /etc/systemd/system/avanza-sync.timer /etc/credstore.encrypted/avanza-sync-key
sudo rm -rf /opt/avanza-sync /etc/avanza-sync
sudo -u postgres psql -d zenith -c 'DROP OWNED BY "avanza-sync"; DROP ROLE "avanza-sync";'
sudo userdel avanza-sync
```

Remove your details in Zenith first. The stored values stay in Zenith until the tables are emptied.
