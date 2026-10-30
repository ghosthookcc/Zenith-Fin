#!/usr/bin/env bash
set -euo pipefail
umask 077

if [[ $EUID -ne 0 ]]; then
    echo "Run this with sudo: sudo $0 [--rotate-key]" >&2
    exit 1
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
service_user=avanza-sync
install_dir=/opt/avanza-sync
config_dir=/etc/avanza-sync
credstore=/etc/credstore.encrypted
key_name=avanza-sync-key
database=zenith
rotate=false

if [[ "${1:-}" == "--rotate-key" ]]; then
    rotate=true
fi

step() {
    printf '\n==> %s\n' "$1"
}

step "Checking the clock (TOTP codes need the right time)"
if [[ "$(timedatectl show -p NTPSynchronized --value)" != "yes" ]]; then
    echo "The clock is not synchronised. Enable chronyd or systemd-timesyncd before continuing." >&2
    exit 1
fi
for unit in chronyd.service chrony.service systemd-timesyncd.service; do
    if systemctl is-active --quiet "$unit"; then
        echo "Time sync is active ($unit)."
    fi
done

step "Creating the system user $service_user"
if ! id -u "$service_user" >/dev/null 2>&1; then
    useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin "$service_user"
fi
id "$service_user"

step "Installing the fetcher in $install_dir (owned by root, read only for the service)"
install -d -o root -g root -m 0755 "$install_dir"
install -o root -g root -m 0644 "$here/avanza_sync.py" "$here/requirements.txt" "$here/README.md" "$install_dir/"
if [[ ! -x "$install_dir/venv/bin/python" ]]; then
    python3 -m venv "$install_dir/venv"
fi
"$install_dir/venv/bin/pip" install --disable-pip-version-check --quiet --require-hashes --only-binary=:all: --no-deps -r "$install_dir/requirements.txt"
"$install_dir/venv/bin/pip" check --disable-pip-version-check
chown -R root:root "$install_dir"
chmod -R u+rwX,go+rX,go-w "$install_dir"

step "Preparing the key pair"
install -d -o root -g root -m 0755 "$config_dir"
install -d -o root -g root -m 0700 "$credstore"
if [[ ! -f "$credstore/$key_name" || "$rotate" == true ]]; then
    "$install_dir/venv/bin/python" -I -c '
import base64, sys
from nacl.public import PrivateKey
key = PrivateKey.generate()
with open(sys.argv[1], "w") as public:
    public.write(base64.b64encode(bytes(key.public_key)).decode() + "\n")
sys.stdout.write(base64.b64encode(bytes(key)).decode())
' "$config_dir/public.key.new" | systemd-creds encrypt --name="$key_name" --with-key=auto - "$credstore/$key_name.new"
    chmod 0644 "$config_dir/public.key.new"
    mv -f "$credstore/$key_name.new" "$credstore/$key_name"
    mv -f "$config_dir/public.key.new" "$config_dir/public.key"
    echo "A new key pair was created. Credentials saved with an older key must be entered again in Zenith."
else
    echo "Keeping the existing key pair (pass --rotate-key to replace it)."
fi
chmod 0644 "$config_dir/public.key"
echo "Public key fingerprint: $(base64 -d "$config_dir/public.key" | sha256sum | cut -d' ' -f1)"

step "Creating the database role $service_user with the least privileges"
sudo -u postgres psql -X -q -v ON_ERROR_STOP=1 -d "$database" <<'SQL'
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'avanza-sync') THEN
        CREATE ROLE "avanza-sync" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
END
$$;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM "avanza-sync";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM "avanza-sync";
GRANT CONNECT ON DATABASE zenith TO "avanza-sync";
GRANT USAGE ON SCHEMA public TO "avanza-sync";
GRANT SELECT (user_id, ciphertext, key_fingerprint, updated_at) ON avanza_credentials TO "avanza-sync";
GRANT SELECT, INSERT, UPDATE ON avanza_sync_status TO "avanza-sync";
GRANT INSERT ON avanza_balance_snapshots TO "avanza-sync";
GRANT USAGE ON SEQUENCE avanza_balance_snapshots_id_seq TO "avanza-sync";
SQL
if sudo -u "$service_user" psql -X -q -h /var/run/postgresql -d "$database" -c "SELECT count(*) FROM avanza_credentials" >/dev/null 2>&1; then
    echo "The service user can reach the database over the unix socket with peer authentication."
else
    echo "The service user could not connect. Add this line above the other 'local' lines in pg_hba.conf and reload PostgreSQL:" >&2
    echo "    local   $database   $service_user   peer" >&2
    exit 1
fi

step "Installing the systemd service and timer"
install -o root -g root -m 0644 "$here/avanza-sync.service" "$here/avanza-sync.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now avanza-sync.timer
systemctl list-timers avanza-sync.timer --no-pager

step "Sandbox exposure (lower is better)"
systemd-analyze security avanza-sync.service --no-pager | tail -n 1

step "Done"
echo "Open Zenith, go to Avanza and enter your username, password and TOTP secret."
echo "Values are fetched on Monday and Thursday mornings. To fetch once now: sudo systemctl start avanza-sync.service"
echo "Follow a run with: journalctl -u avanza-sync.service -n 20 --no-pager"
