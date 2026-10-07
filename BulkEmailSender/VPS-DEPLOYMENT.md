# VPS deployment (optional; not used by Render)

This document describes the earlier VPS + Docker Compose + Nginx architecture. It is optional and is not part of the Render deployment. For Render Free, follow the repository [README.md](../README.md).

## Architecture and prerequisites

Compose runs Nginx and one ASP.NET Core 8 app container. ASP.NET serves the Angular production build, API, SQLite, OAuth and background worker. Only Nginx publishes 80/443; the app port 8080 is internal. Nginx and app share an isolated internal bridge; app also joins a separate outbound bridge for Gmail API traffic. SQLite and ASP.NET Data Protection keys persist in `deployment/data`.

Use Ubuntu 24.04 with Docker Engine/Compose, Git, Certbot, curl and sqlite3. Set an A record `mail.example.com` to the VPS IPv4. Set AAAA only if VPS IPv6 is configured. Open TCP 22, 80, 443 only; keep key-protected SSH enabled. There is no host Nginx; Nginx runs in Compose.

## Secrets and app login

Copy `deployment/.env.example` to `deployment/.env`, chmod 600, fill APP_URL, Google Client ID/Secret, GOOGLE_REDIRECT_URI (`https://mail.example.com/api/auth/google/callback`) and APP_PASSWORD_HASH. Generate the PBKDF2 hash on a trusted Python 3 machine; the password will not echo:

```sh
python3 - <<'PY'
import base64, getpass, hashlib, secrets
p=getpass.getpass('Application password: ').encode(); s=secrets.token_bytes(16); n=600000
print(f"pbkdf2-sha256${n}${base64.b64encode(s).decode()}${base64.b64encode(hashlib.pbkdf2_hmac('sha256',p,s,n)).decode()}")
PY
```

Surround the APP_PASSWORD_HASH value with single quotes in `.env` because it includes `$`. Never commit `.env`, passwords, secrets or data. Container UID 1654 needs write access: `sudo mkdir -p deployment/data && sudo chown -R 1654:1654 deployment/data`. The application login is separate from Gmail OAuth. Login uses a HttpOnly, SameSite=Strict, Secure under HTTPS cookie with a 12-hour expiry.

## Google OAuth

In Google Cloud configure a web OAuth client, enable Gmail API, configure consent and scopes `openid`, `email`, `profile`, `https://www.googleapis.com/auth/gmail.send`, and register the exact HTTPS redirect URI. Add the intended user as a test user while consent is in Testing; review production verification requirements. Keep the local callback if needed for development. Never expose the client secret to Angular.

**Rotate/revoke the old OAuth client secret before production.** It was committed in appsettings; removed from current config, but it may remain in Git history. Put the replacement only in `.env`.

## HTTPS and first deploy

Replace `mail.example.com` in `deployment/nginx/default.conf` and `deployment/deploy.sh`. With DNS live and port 80 open, issue the cert before Nginx starts:

```sh
sudo certbot certonly --standalone -d mail.example.com
sudo chown -R 1654:1654 deployment/data
sh deployment/deploy.sh
curl --fail https://mail.example.com/health
```

Certbot host renewal should reload container Nginx. Install a deploy hook (replace clone path):

```sh
sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-bulk-email-sender >/dev/null <<'EOF'
#!/bin/sh
cd /path/to/WorksSuite && docker compose --env-file deployment/.env -f deployment/docker-compose.yml exec -T nginx nginx -s reload
EOF
sudo chmod 755 /etc/letsencrypt/renewal-hooks/deploy/reload-bulk-email-sender
sudo certbot renew --dry-run
```

Nginx redirects HTTP to HTTPS, forwards the original scheme, sets security headers and disables buffering on SSE with a one-hour timeout. ASP.NET serves SPA routes; unmatched API routes return 404. No CSP is set without compatibility testing.

## Persistence and health

SQLite is `/app/data/bulk-email-sender.db`, mounted from `deployment/data`, with WAL and 30-second busy timeout. Data Protection keys/logs use the same persistent directory. Production startup applies pending EF Core migrations and does not recreate/delete the database. Keep data across updates; never use `docker compose down -v`. `/health` checks SQLite and does not depend on Gmail being connected. Docker health gates Nginx startup.

## Backup/update/rollback

Install sqlite3 on the VPS. `sh deployment/backup.sh` makes a timestamped consistent online backup in `deployment/backups`, mode 600. Back up `.env` and `deployment/data/keys` separately with encrypted access control; keep multiple versions.

```sh
sh deployment/backup.sh
git pull --ff-only
sh deployment/deploy.sh
curl --fail https://mail.example.com/health
```

Before update save `docker image inspect works-suite-app --format '{{.Id}}'`. For rollback restore old code, tag saved image ID `works-suite-app`, then run `docker compose --env-file deployment/.env -f deployment/docker-compose.yml up -d --no-build`. If a migration ran, restore the pre-update DB before old code. Restore matching Data Protection keys and `.env` too.

Useful checks: `docker compose --env-file deployment/.env -f deployment/docker-compose.yml ps`, same command with `logs -f app` or `logs -f nginx`, and `curl --fail https://mail.example.com/health`. For 502/startup errors inspect app health/logs and data permissions. `redirect_uri_mismatch` means Google and `.env` callbacks differ. SQLite and Data Protection keys must be restored together for Gmail tokens to decrypt.

## Development

Existing `ng serve --proxy-config proxy.conf.json`, `dotnet run`, localhost callback and tests remain. Angular uses relative `/api/...` paths in production and development.

