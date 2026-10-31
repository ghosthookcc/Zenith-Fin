# Zenith-Fin

Zenith is a personal finance dashboard for Swedish bank accounts. It connects to your banks through [Enable Banking](https://enablebanking.com/docs/api/reference/), keeps every account and transaction in your own PostgreSQL database, sorts transactions into categories (rules, your own corrections and an optional AI), and shows balances, credit accounts, virtual accounts, a monthly calendar, recurring payments with a monthly plan, and the value of your Avanza accounts. Accounts and transactions can be exported to CSV.

It runs entirely on your own computer and is never published on the internet. Devices on your home network use it at `https://zenith.fin.se` through nginx, and your own devices away from home reach the same address through Tailscale.

## Contents

1. [How it fits together](#how-it-fits-together)
2. [Prerequisites](#1-prerequisites)
3. [Database](#2-database)
4. [Enable Banking](#3-enable-banking)
5. [Backend configuration](#4-backend-configuration)
6. [Run it for development](#5-run-it-for-development)
7. [Always-on deployment](#6-always-on-deployment)
8. [HTTPS on your network with nginx](#7-https-on-your-network-with-nginx)
9. [Away from home with Tailscale](#8-away-from-home-with-tailscale)
10. [Avanza values (optional)](#9-avanza-values-optional)
11. [systemd services](#10-systemd-services)
12. [Everyday tasks](#11-everyday-tasks)
13. [Troubleshooting](#12-troubleshooting)

## How it fits together

```
Phone / laptop at home ──────┐
Your devices, via Tailscale ─┴─https─▶ nginx :443 on the LAN address (zenith.fin.se)
                                         │
                                         ▼
                                 Website :4480 (Astro + Qwik, localhost only)
                                         │
                                         ▼
                                 API :4456 (.NET 8, localhost only) ──▶ PostgreSQL
                                         │
                                         ├──▶ Enable Banking API (accounts, balances, transactions)
                                         └──▶ AI provider (optional, OpenAI-compatible)

avanza-sync.timer (Mon + Thu) ──▶ avanza-sync.service ──▶ Avanza (optional, read only)
                                    │
                                    └──▶ PostgreSQL (encrypted login in, account values out)
```

| Folder | What it is |
| --- | --- |
| `core/zenith` | The API (C# / .NET 8, Dapper, Npgsql) |
| `www` | The website (Astro 5 + Qwik + Tailwind) |
| `tools` | Prisma schema and SQL migrations for the database |
| `deploy` | `deploy.sh`, which builds and runs Zenith as background services |
| `deploy/avanza-sync` | The optional Avanza fetcher: script, systemd service and timer, setup script and tests |

| Port | Used by |
| --- | --- |
| 4444 | Website in development (`npm run dev`) |
| 4445 / 4446 | API in development (Rider or `dotnet run`) |
| 4455 / 4456 | API of the always-on deployment |
| 4480 | Website of the always-on deployment (only reachable through nginx) |
| 80 / 443 | nginx (`zenith.fin.se`), on the LAN address only |
| 53 | AdGuard Home, if it answers DNS for `zenith.fin.se` |

Development and the always-on deployment use different ports, so both can run at the same time. They share the same database.

Only nginx listens on the network, and only on this computer's LAN address. The website and the API listen on localhost, and nothing is forwarded from your router. Away from home, Zenith can only be reached through Tailscale.

## 1. Prerequisites

Tested on Linux Mint 22 (Ubuntu 24.04 base).

```bash
sudo apt install postgresql nginx mkcert libnss3-tools dotnet-sdk-8.0
```

Node.js 24 through [nvm](https://github.com/nvm-sh/nvm):

```bash
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh | bash
nvm install 24
```

You also need:

- an **Enable Banking** application (see [step 3](#3-enable-banking))
- optionally an **AI provider** with an OpenAI-compatible API (for example Groq or OpenAI) for sorting the transactions the rules leave in "Other"
- optionally **AdGuard Home** (or another local DNS server) so devices on your network can find `zenith.fin.se`
- optionally **Tailscale** to use Zenith away from home ([step 8](#8-away-from-home-with-tailscale))
- for the optional **Avanza** values: `sudo apt install python3-venv`, systemd 250 or newer (for `systemd-creds`) and a synchronised clock ([step 9](#9-avanza-values-optional))

Create the local certificate authority once. It is used for every HTTPS certificate below:

```bash
mkcert -install
```

## 2. Database

Create a database user and a database:

```bash
sudo -u postgres createuser --pwprompt zenith
sudo -u postgres createdb --owner zenith zenith
```

Tell Prisma where it is in `tools/.env` (not committed):

```env
DATABASE_URL="postgresql://zenith:<password>@localhost:5432/zenith"
```

Install the tools and create all tables:

```bash
cd tools
pnpm install            # or: npm install
npx prisma migrate deploy
```

Every change to the schema in `tools/prisma/schema/models` comes with a migration in `tools/prisma/migrations`. Run `npx prisma migrate deploy` again whenever new migrations arrive. Use `migrate deploy`, not `migrate dev`: `migrate dev` can offer to wipe the database and also runs the ERD generator (puppeteer).

The Avanza fetcher gets its own database role, `avanza-sync`, which logs in over the unix socket without a password. Its setup script creates it ([step 9](#9-avanza-values-optional)).

## 3. Enable Banking

1. In the Enable Banking control panel, create an application and download its private key (`.pem`).
2. Save the key as `core/zenith/EnableBanking/Keys/<application-id>.pem`. Key files are ignored by git.
3. Add these **redirect URLs** to the application:
   - `https://localhost:4444/api/callback/auth/aspsp` (development)
   - `https://zenith.fin.se/api/callback/auth/aspsp` (your network, [step 7](#7-https-on-your-network-with-nginx))
4. Create `core/zenith/EnableBanking/workspace.json` (ignored by git). `keyPath` is an absolute path, and `Aspsps` lists the banks you can connect:

```json
{
  "EnableBanking": {
    "applicationId": "<application-id>",
    "redirectUrl": "https://localhost:4444/api/callback/auth/aspsp",
    "keyPath": "/home/<you>/Desktop/Zenith-Fin/core/zenith/EnableBanking/Keys/<application-id>.pem",
    "Aspsps": [
      { "Nordea": { "country": "SE", "psuType": "personal" } },
      { "Handelsbanken": { "country": "SE", "psuType": "personal" } },
      { "Marginalen Bank": { "country": "SE", "psuType": "personal" } },
      { "Swedbank": { "country": "SE", "psuType": "personal" } }
    ]
  }
}
```

The always-on deployment uses the same file but replaces `redirectUrl` with `https://zenith.fin.se/api/callback/auth/aspsp` automatically.

## 4. Backend configuration

`core/zenith/appsettings.json` is committed and holds the defaults: the Kestrel ports (4445/4446), the path to `workspace.json` and the AI defaults.

Create `core/zenith/appsettings.Development.json` (ignored by git) with your secrets:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=zenith;Username=zenith;Password=<password>"
  },
  "AI": {
    "Enabled": true,
    "Model": "<model>",
    "ApiKey": "<api-key>",
    "BaseUrl": "https://api.groq.com/openai/v1",
    "ReasoningEffort": "low"
  }
}
```

Set `"Enabled": false` to run without AI. Other settings you can add:

| Setting | Default | What it does |
| --- | --- | --- |
| `Sync:BackgroundIntervalHours` | `6` | How often the API fetches new transactions on its own. Banks allow about four background fetches per account per day. `0` turns it off. |
| `AI:BatchSize` | `40` | Transactions per AI request |
| `AI:MaxBatchesPerRun` | `5` | AI requests per sync |
| `Avanza:PublicKeyFile` | `/etc/avanza-sync/public.key` | The public key the API encrypts your Avanza login with. The Avanza setup script creates it ([step 9](#9-avanza-values-optional)). |
| `Avanza:PublicKey` | empty | The same key as base64 text, used instead of the file when set |

Things the API creates by itself in `core/zenith` (all ignored by git):

- **`key-*.xml`**: the encryption keys for login sessions and saved personal numbers. Keep them. Deleting them logs everyone out and makes saved personal numbers unreadable.
- the HTTPS development certificate for `https://localhost:4446`. Create it once with `dotnet dev-certs https`.

## 5. Run it for development

**API**: open `core/zenith` in Rider and run the `ZenithFin` profile, or:

```bash
cd core/zenith
dotnet dev-certs https
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

The API listens on `https://localhost:4446` (Swagger at `/swagger` in development).

**Website**: the development server needs a localhost certificate in `www`:

```bash
cd www
npm install
mkcert localhost 127.0.0.1 ::1
npm run dev
```

`mkcert` creates `localhost+2.pem` and `localhost+2-key.pem`, which `astro.config.ts` reads. Open `https://localhost:4444`, create an account and connect your banks under **Accounts → Add a bank**.

Useful website commands:

| Command | What it does |
| --- | --- |
| `npm run dev` | Development server on `https://localhost:4444` |
| `npx astro check` | Type check |
| `npm run build` | Lint with fixes, format with Prettier, then build |

The website reads two optional environment variables: `ZENITH_API_URL` (default `https://localhost:4446/api/v1`) and, at build time, `ZENITH_DOMAIN` (default `zenith.fin.se`), the address Astro accepts form posts from.

## 6. Always-on deployment

`deploy/deploy.sh` turns this computer into the Zenith server. It runs as your own user (no sudo) and:

1. publishes the API to `~/.local/share/zenith-fin/api` and copies `appsettings.Development.json` there as `appsettings.Production.json` (readable only by you)
2. builds the website to `~/.local/share/zenith-fin/web`
3. creates a localhost certificate in `~/.config/zenith-fin/tls` for the internal HTTPS between nginx, the website and the API
4. writes two systemd user services:
   - `zenith-api`: Production mode on localhost 4455/4456. Its working folder is `core/zenith`, so it shares `workspace.json`, the bank key and the encryption keys with development.
   - `zenith-web`: the website on `127.0.0.1:4480`
5. writes the nginx site to `~/.config/zenith-fin/nginx/zenith.conf`, listening only on this computer's LAN address, and tells you when it needs installing
6. enables both services and "linger", so Zenith starts at boot even before anyone logs in

```bash
./deploy/deploy.sh
```

Run it again after every update. The previous build is kept as `api.previous` / `web.previous`, and the website's build log is in `~/.local/share/zenith-fin/build.log`. You can override `ZENITH_DOMAIN`, `ZENITH_WEB_PORT`, `ZENITH_API_HTTP_PORT`, `ZENITH_API_HTTPS_PORT` and `ZENITH_ADDRESS` (the LAN address nginx listens on; by default the address `ip -4 route get 1.1.1.1` reports).

How to check, restart and read the logs of the services is in [step 10](#10-systemd-services).

## 7. HTTPS on your network with nginx

This makes Zenith reachable as `https://zenith.fin.se` from every device on your home network. The name only exists on your network, so the certificate comes from your own mkcert authority.

### 7.1 Certificate

```bash
mkdir -p ~/.config/zenith-fin/tls
mkcert -cert-file ~/.config/zenith-fin/tls/zenith.fin.se.pem -key-file ~/.config/zenith-fin/tls/zenith.fin.se-key.pem zenith.fin.se

sudo install -d -m 755 /etc/nginx/ssl
sudo install -m 644 ~/.config/zenith-fin/tls/zenith.fin.se.pem /etc/nginx/ssl/zenith.fin.se.pem
sudo install -m 600 ~/.config/zenith-fin/tls/zenith.fin.se-key.pem /etc/nginx/ssl/zenith.fin.se-key.pem
sudo install -m 644 "$(mkcert -CAROOT)/rootCA.pem" /etc/nginx/ssl/zenith-ca.crt
```

`zenith-ca.crt` is the public half of your certificate authority. nginx offers it at `http://zenith.fin.se/zenith-ca.crt` so other devices can install it. The private half, `rootCA-key.pem` in `mkcert -CAROOT`, must never leave this computer. mkcert certificates expire after about 27 months. Renew by running this step again.

### 7.2 nginx site

Run `./deploy/deploy.sh` first. It generates the site config, then install it:

```bash
sudo install -m 644 ~/.config/zenith-fin/nginx/zenith.conf /etc/nginx/sites-available/zenith
sudo ln -sf /etc/nginx/sites-available/zenith /etc/nginx/sites-enabled/zenith
sudo nginx -t && sudo systemctl reload nginx
```

The site listens only on the LAN address, redirects `http://` to `https://`, serves the certificate authority file, forwards everything else to the website, and refuses requests for any other host name.

### 7.3 Firewall

```bash
sudo ufw allow from 192.168.1.0/24 to any port 80,443 proto tcp
```

If AdGuard Home on this computer answers DNS for your network ([7.4](#74-dns-let-every-device-find-zenithfinse)), open its port for the same range:

```bash
sudo ufw allow from 192.168.1.0/24 to any port 53
```

Use your own network range if it isn't `192.168.1.0/24`. Never forward ports 80 or 443 on your router: Zenith is not meant to be reachable from the internet.

### 7.4 DNS: let every device find `zenith.fin.se`

Point the name at this computer's LAN address (`ip -4 route get 1.1.1.1` shows it after `src`, for example `192.168.1.178`).

**With AdGuard Home:** under **Filters → DNS rewrites**, add `zenith.fin.se` → `<lan-ip>`. Then devices have to use AdGuard as their DNS server:

- **If your router lets you set the DHCP DNS server:** set it to `<lan-ip>`. Every device picks it up automatically.
- **If it doesn't** (like many ISP routers), set it per device:
  - **Computers** (Linux, Mac, Windows): add `<lan-ip> zenith.fin.se` to the hosts file (`/etc/hosts`, or `C:\Windows\System32\drivers\etc\hosts` on Windows). This computer needs the line too.
  - **iPhone / iPad**: Settings → Wi-Fi → ⓘ → Configure DNS → Manual → only `<lan-ip>`.
  - **Android**: Wi-Fi network → Edit → Advanced → IP settings: Static, with a free IP address, gateway `192.168.1.1`, prefix length `24` and DNS 1 `<lan-ip>`.

Phones that use AdGuard look up every website through this computer while on your Wi-Fi, so keep it running.

### 7.5 Trust the certificate on each device

Open `http://zenith.fin.se/zenith-ca.crt` (plain `http`) on the device and install it:

- **iPhone / iPad**: allow the download, then Settings → Profile Downloaded → Install. Then Settings → General → About → Certificate Trust Settings → turn it on.
- **Android**: Settings → Security → Encryption & credentials → Install a certificate → CA certificate.
- **Windows**: open the file → Install Certificate → Local Machine → Trusted Root Certification Authorities.
- **Mac**: open the file in Keychain Access → Trust → Always Trust.

`https://zenith.fin.se` now opens with a padlock.

### 7.6 Enable Banking

Make sure `https://zenith.fin.se/api/callback/auth/aspsp` is one of the application's redirect URLs ([step 3](#3-enable-banking)). Without it, connecting a bank from the deployed site fails.

## 8. Away from home with Tailscale

Zenith is never opened to the internet. To use it away from home, put this computer and your own devices in the same [Tailscale](https://tailscale.com) network (tailnet). Tailscale carries the traffic to this computer's LAN address, so `https://zenith.fin.se`, the certificate and the Enable Banking redirect work exactly as at home.

### 8.1 This computer

```bash
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up
sudo tailscale set --advertise-routes=192.168.1.178/32
```

Use your own LAN address. The `/32` route shares this computer only, not the rest of your network. Then, in the [admin console](https://login.tailscale.com/admin/machines):

1. **Machines** → this computer → **⋯** → **Edit route settings** → approve `192.168.1.178/32`.
2. **⋯** → **Disable key expiry**, so the server doesn't drop out of the tailnet when its key expires.

### 8.2 DNS for `zenith.fin.se`

In the admin console under **DNS** → **Nameservers** → **Add nameserver** → **Custom**, enter this computer's LAN address (`192.168.1.178`, where AdGuard Home answers, [7.4](#74-dns-let-every-device-find-zenithfinse)) and turn on **Restrict to domain** with `zenith.fin.se`. Your devices then ask AdGuard for `zenith.fin.se` through Tailscale and keep their usual DNS for everything else.

`tailscale dns status` on this computer lists the route as `zenith.fin.se -> 192.168.1.178` under **Split DNS Routes**.

### 8.3 Your devices

1. Install Tailscale and sign in to the same tailnet. Keep **Use Tailscale DNS** on (the default).
2. Phones, tablets, Windows and Mac use the approved route automatically. On Linux run `sudo tailscale set --accept-routes`.
3. Install the certificate authority as in [7.5](#75-trust-the-certificate-on-each-device) if the device hasn't already.

With Tailscale connected, open `https://zenith.fin.se` from anywhere.

### 8.4 Keep it private

- Don't use Tailscale Funnel or `tailscale serve` for Zenith. Funnel publishes a service on the internet. `tailscale funnel status` should say `No serve config`.
- Tailscale's access controls decide which devices in your tailnet may use the route. The default policy lets all of them in, so tighten it if you share your tailnet with others.

## 9. Avanza values (optional)

The **Avanza** page shows the total value of each of your Avanza accounts and how it changes over time, and the accounts count in the total on **Accounts** and in its CSV export. Avanza has no official API, so a separate fetcher uses an unofficial one. That breaks Avanza's terms of use, so the fetcher only reads the account overview and logs in at most twice a week.

Set it up after the first deployment:

```bash
./deploy/deploy.sh
sudo ./deploy/avanza-sync/setup.sh
```

The script creates the `avanza-sync` system user, installs the fetcher in `/opt/avanza-sync`, creates the key pair (the public key in `/etc/avanza-sync/public.key`, the private key encrypted with `systemd-creds`), creates the database role and installs and starts the timer. If it says the service user could not connect to the database, add this line above the other `local` lines in `pg_hba.conf` and reload PostgreSQL:

```
local   zenith   avanza-sync   peer
```

Then open **Avanza** in Zenith and enter your username, password and TOTP secret. The API only holds the public key, so it can encrypt your details but never read them back. Only the fetcher can decrypt them.

How the keys are kept, the schedule and its limits, the statuses, key rotation, the tests and how to remove it are described in [deploy/avanza-sync/README.md](deploy/avanza-sync/README.md).

## 10. systemd services

| Unit | Kind | Unit file | What it does |
| --- | --- | --- | --- |
| `zenith-api.service` | user | `~/.config/systemd/user/`, written by `deploy.sh` | Runs `~/.local/share/zenith-fin/api/ZenithFin.dll` in Production on localhost 4455/4456 from `core/zenith` |
| `zenith-web.service` | user | `~/.config/systemd/user/`, written by `deploy.sh` | Runs the website (`~/.local/share/zenith-fin/web/dist/server/entry.mjs`) on `127.0.0.1:4480` after the API |
| `avanza-sync.service` | system | `/etc/systemd/system/`, copied from `deploy/avanza-sync` by `setup.sh` | Fetches Avanza values once as the `avanza-sync` user in a strict sandbox, then exits |
| `avanza-sync.timer` | system | `/etc/systemd/system/`, copied from `deploy/avanza-sync` by `setup.sh` | Starts the fetch on Monday and Thursday at 07:00 with up to 90 minutes of random delay |

The user services restart by themselves if they crash, and they start at boot because `deploy.sh` turns on linger for your user (`loginctl enable-linger`). `deploy.sh` rewrites their unit files on every run, so change the script, not the files. To change the Avanza units, edit them in `deploy/avanza-sync` and run `setup.sh` again.

```bash
systemctl --user status zenith-api zenith-web
journalctl --user -u zenith-api -u zenith-web -f
systemctl --user restart zenith-api zenith-web
systemctl --user stop zenith-web zenith-api
systemctl --user cat zenith-api

systemctl list-timers avanza-sync.timer
journalctl -u avanza-sync.service -n 20 --no-pager
sudo systemctl start avanza-sync.service
systemd-analyze security avanza-sync.service
```

Zenith also relies on these system services: `postgresql`, `nginx`, `tailscaled` ([step 8](#8-away-from-home-with-tailscale)), AdGuard Home if it answers your DNS, and `systemd-timesyncd` or `chrony`, because the Avanza login codes need the right time.

## 11. Everyday tasks

**Update Zenith**

```bash
git pull
cd tools && npx prisma migrate deploy && cd ..
./deploy/deploy.sh
```

If the update changed `deploy/avanza-sync`, also run `sudo ./deploy/avanza-sync/setup.sh`. It keeps the existing key, so your saved Avanza details stay valid.

**Connect or reconnect a bank**: Accounts → Connected banks → Reconnect, or Add a bank. Tick every account you want Zenith to see. An account the bank no longer shares is marked "no longer shares this account".

**Export to CSV**: **Accounts → Export CSV** gives one row per account with balances and monthly figures. **Calendar → Export CSV** gives the transactions of any days, months or years, up to 10 years at a time. Choose **For Excel** (semicolons and decimal commas, for Swedish Excel) or **Standard CSV** (commas and decimal points, for LibreOffice, Google Sheets, Python and R).

**Plan your months**: **Recurring → Monthly plan** shows what comes in, goes out and is left for this month and the next 11. Add income and fixed costs as your own rows (every few months or years from the date they last happened, with an optional end), or add payments from your history, which then follow your bank's amount and dates until you change them.

**Start over with an empty database** (deletes all users, banks, transactions and Avanza values):

```bash
systemctl --user stop zenith-web zenith-api
cd tools && npx prisma migrate reset && cd ..
systemctl --user start zenith-api zenith-web
```

Remove your banks in Zenith first if you also want to end Zenith's access at the banks. If you use Avanza, run `sudo ./deploy/avanza-sync/setup.sh` afterwards to give the fetcher its database access back.

## 12. Troubleshooting

| Problem | What to check |
| --- | --- |
| `502 Bad Gateway` right after a deploy | The website takes a second to start. Reload. |
| Page never loads on a phone | DNS ([7.4](#74-dns-let-every-device-find-zenithfinse)): does `http://zenith.fin.se` reach this computer? Then check the firewall ([7.3](#73-firewall)). |
| Works at home but not away | Is Tailscale connected on the device? Is the route approved ([8.1](#81-this-computer)), and accepted on Linux ([8.3](#83-your-devices))? Does `zenith.fin.se` resolve through the split DNS ([8.2](#82-dns-for-zenithfinse))? If the connection times out, let Tailscale traffic through ufw: `sudo ufw allow in on tailscale0 to any port 53` and `sudo ufw allow in on tailscale0 to any port 80,443 proto tcp`. |
| Certificate warning | The device hasn't installed `zenith-ca.crt` yet ([7.5](#75-trust-the-certificate-on-each-device)). |
| Connecting a bank fails after the bank login | The redirect URL for the address you use isn't registered at Enable Banking ([7.6](#76-enable-banking)). |
| A credit or virtual account stays at 0 | The linked account isn't shared with Zenith anymore. Reconnect the bank and tick the account. |
| "Avanza is not set up on this server yet" | Run `sudo ./deploy/avanza-sync/setup.sh` ([step 9](#9-avanza-values-optional)). It creates `/etc/avanza-sync/public.key`, which the API reads. |
| Avanza says "Login refused" or "Enter your details again" | Avanza refused the login (`AUTH_FAILED`), or the key was replaced (`KEY_MISMATCH`). Enter your details again on the Avanza page. The fetcher doesn't try again until you do. |
| Avanza says "Last fetch failed" | The run hit an error, shown by its class (for example `ConnectionError`). It tries again at the next scheduled time. See `journalctl -u avanza-sync.service`. |
| An exported CSV opens in one column or with broken numbers | Export again as **Standard CSV**, or tell the import that the separator is a semicolon and the decimal sign a comma. |
| Logs | `journalctl --user -u zenith-api -u zenith-web -f`, `journalctl -u avanza-sync.service`, `/var/log/nginx/error.log` |
