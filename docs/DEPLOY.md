# Production deployment

Stack: `docker-compose.prod.yml` (project `arcano`). Only `web` publishes a port (default `127.0.0.1:8080`); postgres, seaweedfs and api are internal. The dev compose in `backend/` is separate.

## 1. Prerequisites
Linux host with Docker Engine + Compose v2, `openssl`, `git`, and a domain with a TLS reverse proxy (step 5).

## 2. Configure
```
cp .env.production.example .env.production
```
Store `.env.production` in a password manager as well: it holds every secret, and without it a restored backup cannot be run.
Fill every value. Generate secrets with `openssl rand -hex 32` (use `openssl rand -hex 16` for `S3_ACCESS_KEY`).
Never reuse dev values (`any_key`, `any_secret`, `postgrespassword`). The old dev JWT key remains in git history and must never be used.

## 3. Generate storage config and start
```
scripts/gen-s3-config.sh .env.production
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
```
Run these from the repo root. `gen-s3-config.sh` writes `backend/seaweedfs/s3.prod.json` (mode 600, gitignored). If you change `S3_*` later, re-run it and `up -d` again. `backend/seaweedfs/security.toml` is not needed in production.

## 4. First account
Open the site and sign up. On an empty install the first account needs no key and becomes the admin. Do this right after the first start, before anyone else can reach the site.

After that, sign-up needs a single-use invite key. As admin, open the admin page, create a Join key (optionally with a storage quota for the new member), and send it to them; it is shown once and expires after 7 days by default. A member who forgot their password gets a Reset key the same way (there is no email, so this is how passwords are reset). Admins can also set quotas, disable or delete users there. Admins cannot read other users' files, and the last admin cannot be deleted or disabled.

### Forgot the admin password
If the only admin forgot the password, nobody can issue a Reset key in the UI, so create one in the database. A Reset key is the SHA-256 of the key text, as UPPERCASE hex, stored in table `InviteKey`. It is single-use and expires after 1 hour. Run from the repo root:
```
KEY=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')
HASH=$(printf %s "$KEY" | sha256sum | cut -d' ' -f1 | tr a-f A-F)
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml exec -T postgres \
  psql -U postgres -d arcanocore -c "INSERT INTO \"InviteKey\" (\"Id\",\"KeyHash\",\"Type\",\"TargetUserId\",\"ExpiresAtUtc\",\"CreatedById\",\"CreatedAtUtc\") SELECT gen_random_uuid(), '$HASH', 'Reset', \"Id\", now() + interval '1 hour', \"Id\", now() FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = 'ADMIN@EXAMPLE.COM'"
echo "$KEY"
```
Replace `ADMIN@EXAMPLE.COM` with the admin's email in UPPERCASE; `INSERT 0 1` means it found the user. Then set the new password (at least 8 characters) through the public URL:
```
curl -i -H 'Content-Type: application/json' -d "{\"key\":\"$KEY\",\"newPassword\":\"<new password>\"}" https://drive.example.com/api/Auth/reset
```
`204` means done; `403` means the key is wrong, used or expired. Every old session is signed out.

Five wrong passwords for one email from one address block that pair for 5 minutes (kept in memory, so restarting the API clears it). If login fails right after the reset, wait 5 minutes or restart the API. Admin and reset actions are logged as `Audit` lines in the API log.

## 5. TLS in front
`web` speaks plain HTTP; the proxy must forward `X-Forwarded-For` and `X-Forwarded-Proto` to `WEB_BIND:WEB_PORT`. Never expose `WEB_BIND` publicly (e.g. `0.0.0.0` without a firewall): the nginx in `web` trusts `X-Forwarded-Proto`, so direct clients could spoof it. `PUBLIC_ORIGIN` must equal the public https URL.

Caddy:
```
drive.example.com {
    reverse_proxy 127.0.0.1:8080
    header Strict-Transport-Security "max-age=31536000"
}
```
(Caddy sets X-Forwarded-For/Proto itself.)

nginx:
```
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 0;
    add_header Strict-Transport-Security "max-age=31536000" always;
}
```
`proxy_http_version 1.1` is needed because nginx talks HTTP/1.0 to the app by default and then cannot pass on a broken download, so a failed folder zip could look complete.
Add `listen 443 ssl;` and certificates as usual.

HSTS: the `Strict-Transport-Security` line in both examples makes browsers use HTTPS only for a year. Add it only after HTTPS works, because browsers will refuse plain HTTP for that long.

Client IP (per-IP rate limits): `frontend/nginx.conf` trusts `X-Forwarded-For` only from `set_real_ip_from 172.28.0.1`, the Docker gateway of the compose subnet, which is where a proxy on the Docker host arrives from. It uses the rightmost entry, the one your proxy appends, so the proxy must append or replace the header (both examples above do). If the proxy runs elsewhere, change that address and rebuild `web`:
- On another host (`WEB_BIND` not loopback, firewalled to the proxy): the proxy's IP as `web` sees it.
- A container on the `internal` network: give it a fixed IP and use that.
- A changed compose subnet: its gateway.

Check: in `docker compose -p arcano logs web`, the first field of a request through the proxy must be the client's IP, not the proxy's or 172.28.0.1. A wrong address does not allow spoofing, but all clients then share one rate-limit bucket. Processes on the Docker host can set their own client IP through the published port, so keep it closed to anything else.

The nginx inside the `web` image caps request bodies at 110m (`client_max_body_size` in `frontend/nginx.conf`). If you set `MAX_UPLOAD_BYTES` above ~110 MiB, raise that value too and rebuild.

### Cloudflare Tunnel (no open ports, no certificates)
Cloudflare terminates HTTPS and `cloudflared` on the host connects out to it, so the host needs no port forwarding or certificate. Keep a small Caddy between the tunnel and `web`: it sets the client IP and the scheme itself, so nothing a client sends can reach `web` as a trusted header.

Flow: `browser -> Cloudflare -> cloudflared -> Caddy 127.0.0.1:8081 -> web 127.0.0.1:8080`

Caddy (`/etc/caddy/Caddyfile`; plain HTTP on loopback only, Cloudflare provides the HTTPS):
```
{
    auto_https off
}
http://127.0.0.1:8081 {
    reverse_proxy 127.0.0.1:8080 {
        header_up X-Forwarded-For {header.CF-Connecting-IP}
        header_up X-Forwarded-Proto https
    }
}
```
`cloudflared` (`/etc/cloudflared/config.yml`, after `cloudflared tunnel login`, `tunnel create arcano` and `tunnel route dns arcano drive.example.com`):
```
tunnel: <TUNNEL_ID>
credentials-file: /etc/cloudflared/<TUNNEL_ID>.json
ingress:
  - hostname: drive.example.com
    service: http://127.0.0.1:8081
  - service: http_status:404
```
Set `PUBLIC_ORIGIN=https://drive.example.com`. Run `cloudflared` and Caddy as services so they start on boot.

- **Client IP:** without the `CF-Connecting-IP` line every request arrives from Cloudflare, all users share one rate-limit bucket, and anyone who knows an email can lock that account out of login for 5 minutes. Do the check from the paragraph above: the first field of a `web` log line must be the client's IP.
- **Never expose Caddy's port:** it trusts `CF-Connecting-IP` from whoever connects. Keep it on `127.0.0.1`, and do not point the tunnel at `web` directly.
- **Limits:** Cloudflare's Free and Pro plans cap one request body at 100 MB. Uploads go in 8 MiB chunks, so this does not affect them.
- **Terms and privacy:** Cloudflare can read traffic it terminates. Its terms restrict serving large amounts of non-HTML content on some plans; read the current terms before storing lots of media this way. If the tunnel or the account has a problem, remote access stops and LAN access still works.
- This setup has not been tested against a real Cloudflare account.

## 6. Backup and restore
Both scripts read `.env.production` (override with `ENV_FILE=path`) because the compose file has required variables.
```
scripts/backup.sh               # db.dump, seaweedfs.tar.gz, api_keys.tar.gz, env.production (mode 600), seaweedfs-image.txt in backups/<UTC time>/, keeps BACKUP_KEEP=7
scripts/backup.sh --stop-api    # stops api and seaweedfs while copying (restarted before the off-host copy, also on failure; `restart: always` brings them back after a reboot mid-backup): consistent DB/files pair, uploads fail meanwhile
```
Without `--stop-api` the script warns on stderr: the database is dumped first and the files are tarred while uploads continue, so the restore can contain orphan objects (files without a database row; measured: 26 during an upload loop). They are harmless leaked storage: nothing reclaims them. Use `--stop-api` (and in cron) for a consistent pair.
The backups contain secrets (`env.production`); `backups/` is created with mode 700 and is gitignored.

Off-host copy: install and configure `rclone`, then set `BACKUP_RCLONE_REMOTE` (e.g. `s3remote:arcano-backups`) in the environment of the script. After each backup it runs `rclone copy <dir> $BACKUP_RCLONE_REMOTE/<timestamp>`; if that fails the script exits non-zero (and skips pruning).

Cron (daily 03:00): `0 3 * * * cd /opt/arcano && BACKUP_RCLONE_REMOTE=s3remote:arcano-backups scripts/backup.sh --stop-api >> backups/cron.log 2>&1`
(Run `scripts/backup.sh` once by hand first so `backups/` exists with the right mode.) With `--stop-api` the services are running again before `rclone` starts. Without `BACKUP_RCLONE_REMOTE` backups stay on the same disk.

Disk sizing: with 7 backups on the same disk as the data, plan for disk >= users x quota x ~8 (live data plus 7 archives).

Restore (destructive; validates the archives first, then stops web/api, replaces DB, files and api keys, restarts):
```
scripts/restore.sh backups/<timestamp> --yes
```
`restore.sh` exits before stopping anything if `backend/seaweedfs/s3.prod.json` is missing (Docker would create a directory there instead of mounting the file).

Restore on a fresh host (`s3.prod.json` is generated, not part of the backup; `env.production` is):
```
git clone <repo> /opt/arcano && cd /opt/arcano
cp <backup dir>/env.production .env.production
scripts/gen-s3-config.sh .env.production
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
scripts/restore.sh <backup dir> --yes
```

## 7. Upgrade
```
scripts/backup.sh --stop-api
git pull
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
```
Database migrations run when the API starts.

A v0.1.0 install takes this pre-upgrade backup with the v0.1.0 script, which stops only the API and has no api keys or env copy, so keep `.env.production` separately.

Rollback: `scripts/restore.sh backups/<timestamp> --yes` with the backup taken before the upgrade. It empties the database schema first, so a backup from before the upgrade restores over the upgraded database; accounts, keys and shares created after that backup are lost, and files are restored too. Then start the code you want: the upgraded code applies its migrations again at startup.

Tested on throwaway stacks: the same steps by hand (drop the schema, restore a v0.1.0 backup, start the current code) gave identical data and file hashes, with migrations re-applied; `restore.sh` itself with a same-version backup (row count and file hash matched); and its database commands against an older-schema dump on a throwaway Postgres. Not tested: `restore.sh` with an actual v0.1.0 backup, and running the old code against the restored database.

If `restore.sh` fails it leaves the services stopped; fix the error and run it again.

Upgrading an install that already has accounts (from before invite keys): the migration gives every existing account a profile but makes nobody admin. Set `BOOTSTRAP_ADMIN_EMAIL=you@example.com` in `.env.production` (it becomes `Auth:BootstrapAdminEmail`) and run `up -d` again. It only promotes when no admin exists yet; remove it afterwards. Without it the startup rule below decides. `ALLOW_REGISTRATION` no longer has any effect and can be removed from `.env.production`.

Startup rule when no admin exists: if `Auth:BootstrapAdminEmail` is set, that user is promoted (skipped if disabled or locked out: a warning is logged and nobody is promoted). With no email and exactly one user, that user is promoted automatically (a warning is logged). With several users and no email an error is logged and nobody is promoted, so set `BOOTSTRAP_ADMIN_EMAIL`.

## Not covered
- No high availability; run a single API instance only.
- The `chrislusf/seaweedfs` image is pinned by digest in `docker-compose.prod.yml` (the dev compose still uses `:latest`, and no version tag could be determined offline); bump the digest deliberately and take a backup first. Each backup records the image used in `seaweedfs-image.txt`.
- No email: forgotten passwords are reset with an admin-issued Reset key (step 4); if the only admin forgot theirs, see "Forgot the admin password".
- Sessions last at most 24 hours; access tokens expire after `Jwt:ExpiryInMinutes` (default 60).
- Tokens are revoked by sign-out-everywhere, a password change or reset, and disabling a user; rotating `JWT_SECRET` logs everyone out.
- Trust model: an admin can issue a Reset key for any user, including other admins, and sign in as them. Admins cannot otherwise read other users' files. Members can share files and folders with each other by email (read-only) and can probe whether an email is registered. Admin and key actions are written as `Audit` lines to the API container log (`docker compose logs api | grep Audit`); log rotation keeps about 30 MB (3 files of 10 MB), so audit history is limited to that. Fine for a trusted group.
- No monitoring or log shipping; backups are not encrypted, and are copied off-host only if `BACKUP_RCLONE_REMOTE` is set.
