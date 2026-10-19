#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backend="$repo/core/zenith"
frontend="$repo/www"

data="${XDG_DATA_HOME:-$HOME/.local/share}/zenith-fin"
config="${XDG_CONFIG_HOME:-$HOME/.config}/zenith-fin"
units="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"

web_port="${ZENITH_WEB_PORT:-8443}"
api_http_port="${ZENITH_API_HTTP_PORT:-4455}"
api_https_port="${ZENITH_API_HTTPS_PORT:-4456}"
host="${ZENITH_HOST:-$(hostname | tr '[:upper:]' '[:lower:]').local}"
address="${ZENITH_ADDRESS:-$(ip -4 route get 1.1.1.1 | awk '{ for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1) }')}"

node="$(command -v node)"
dotnet="$(command -v dotnet)"
mkcert="$(command -v mkcert)"

cert="$config/tls/cert.pem"
key="$config/tls/key.pem"

mkdir -p "$data" "$config/tls" "$units"

echo "Building the API"
rm -rf "$data/api.next"
"$dotnet" publish "$backend/ZenithFin.csproj" -c Release -o "$data/api.next" --nologo -v quiet
rm -f "$data/api.next/appsettings.Development.json"
install -m 600 "$backend/appsettings.Development.json" "$data/api.next/appsettings.Production.json"

echo "Building the website"
if ! (cd "$frontend" && ./node_modules/.bin/astro build) > "$data/build.log" 2>&1; then
    cat "$data/build.log"
    exit 1
fi
rm -rf "$data/web.next"
mkdir -p "$data/web.next"
cp -r "$frontend/dist" "$data/web.next/dist"
ln -s "$frontend/node_modules" "$data/web.next/node_modules"

if [ ! -f "$cert" ] || ! openssl x509 -in "$cert" -noout -ext subjectAltName 2>/dev/null | grep -q "IP Address:$address\b" ||
   ! openssl x509 -in "$cert" -noout -ext subjectAltName 2>/dev/null | grep -q "DNS:$host\b"; then
    echo "Creating a certificate for $host and $address"
    "$mkcert" -cert-file "$cert" -key-file "$key" "$host" "$address" localhost 127.0.0.1 2>/dev/null
    chmod 600 "$key"
fi

cat > "$units/zenith-api.service" <<EOF
[Unit]
Description=Zenith-Fin API

[Service]
WorkingDirectory=$backend
ExecStart=$dotnet $data/api/ZenithFin.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_CONTENTROOT=$data/api
Environment=Kestrel__Endpoints__Http__Url=http://localhost:$api_http_port
Environment=Kestrel__Endpoints__Https__Url=https://localhost:$api_https_port
Environment=Kestrel__Endpoints__Https__Certificate__Path=$cert
Environment=Kestrel__Endpoints__Https__Certificate__KeyPath=$key
Environment=EnableBanking__RedirectUrl=https://$host:$web_port/api/callback/auth/aspsp
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
EOF

cat > "$units/zenith-web.service" <<EOF
[Unit]
Description=Zenith-Fin website
Wants=zenith-api.service
After=zenith-api.service

[Service]
WorkingDirectory=$data/web
ExecStart=$node $data/web/dist/server/entry.mjs
Environment=NODE_ENV=production
Environment=HOST=0.0.0.0
Environment=PORT=$web_port
Environment=SERVER_CERT_PATH=$cert
Environment=SERVER_KEY_PATH=$key
Environment=ZENITH_API_URL=https://localhost:$api_https_port/api/v1
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
EOF

echo "Restarting the services"
systemctl --user stop zenith-web.service zenith-api.service 2>/dev/null || true
rm -rf "$data/api.previous" "$data/web.previous"
if [ -d "$data/api" ]; then mv "$data/api" "$data/api.previous"; fi
if [ -d "$data/web" ]; then mv "$data/web" "$data/web.previous"; fi
mv "$data/api.next" "$data/api"
mv "$data/web.next" "$data/web"

systemctl --user daemon-reload
systemctl --user enable zenith-api.service zenith-web.service >/dev/null 2>&1
systemctl --user restart zenith-api.service zenith-web.service

if [ "$(loginctl show-user "$USER" -p Linger --value 2>/dev/null)" != "yes" ]; then
    loginctl enable-linger "$USER" 2>/dev/null || echo "To start Zenith at boot without logging in, run: sudo loginctl enable-linger $USER"
fi

echo
echo "Zenith runs at https://$host:$web_port and https://$address:$web_port"
echo "Enable Banking redirect URL: https://$host:$web_port/api/callback/auth/aspsp"
echo "Certificate authority for your other devices: $("$mkcert" -CAROOT)/rootCA.pem"
if grep -qs "^ENABLED=yes" /etc/ufw/ufw.conf; then
    echo "If other devices can't connect, open the port: sudo ufw allow from ${address%.*}.0/24 to any port $web_port proto tcp"
fi
