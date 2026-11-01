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

## 5. TLS in front
`web` speaks plain HTTP; the proxy must forward `X-Forwarded-For` and `X-Forwarded-Proto` to `WEB_BIND:WEB_PORT`. Never expose `WEB_BIND` publicly (e.g. `0.0.0.0` without a firewall): the nginx in `web` trusts `X-Forwarded-Proto`, so direct clients could spoof it. `PUBLIC_ORIGIN` must equal the public https URL.

Caddy:
```
drive.example.com {
    reverse_proxy 127.0.0.1:8080
}
```
(Caddy sets X-Forwarded-For/Proto itself.)

nginx:
```
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 0;
}
```
Add `listen 443 ssl;` and certificates as usual.

The nginx inside the `web` image caps request bodies at 110m (`client_max_body_size` in `frontend/nginx.conf`). If you set `MAX_UPLOAD_BYTES` above ~110 MiB, raise that value too and rebuild.

## 6. Backup and restore
Both scripts read `.env.production` (override with `ENV_FILE=path`) because the compose file has required variables.
```
scripts/backup.sh               # db.dump, seaweedfs.tar.gz, api_keys.tar.gz, env.production (mode 600), seaweedfs-image.txt in backups/<UTC time>/, keeps BACKUP_KEEP=7
scripts/backup.sh --stop-api    # stops api briefly for a consistent DB/files pair
```
The backups contain secrets (`env.production`); `backups/` is created with mode 700 and is gitignored.

Off-host copy: install and configure `rclone`, then set `BACKUP_RCLONE_REMOTE` (e.g. `s3remote:arcano-backups`) in the environment of the script. After each backup it runs `rclone copy <dir> $BACKUP_RCLONE_REMOTE/<timestamp>`; if that fails the script exits non-zero (and skips pruning).

Cron (daily 03:00): `0 3 * * * cd /opt/arcano && BACKUP_RCLONE_REMOTE=s3remote:arcano-backups scripts/backup.sh --stop-api >> backups/cron.log 2>&1`
(Run `scripts/backup.sh` once by hand first so `backups/` exists with the right mode.) Without `BACKUP_RCLONE_REMOTE` backups stay on the same disk.

Disk sizing: with 7 backups on the same disk as the data, plan for disk >= users x quota x ~8 (live data plus 7 archives).

Restore (destructive; validates the archives first, then stops web/api, replaces DB, files and api keys, restarts):
```
scripts/restore.sh backups/<timestamp> --yes
```

## 7. Upgrade
```
scripts/backup.sh --stop-api
git pull
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
```
Database migrations run when the API starts.

Upgrading an install that already has accounts (from before invite keys): the migration gives every existing account a profile but makes nobody admin. Set `BOOTSTRAP_ADMIN_EMAIL=you@example.com` in `.env.production` (it becomes `Auth:BootstrapAdminEmail`) and run `up -d` again. It only promotes when no admin exists yet; remove it afterwards. Without it the API logs a warning at startup and sign-up stays closed.

Startup rule when no admin exists: if `Auth:BootstrapAdminEmail` is set, that user is promoted (skipped if disabled). With no email and exactly one user, that user is promoted automatically (a warning is logged). With several users and no email an error is logged and nobody is promoted, so set `BOOTSTRAP_ADMIN_EMAIL`.

## Not covered
- No high availability; run a single API instance only.
- The `chrislusf/seaweedfs` image is pinned by digest in `docker-compose.prod.yml` (the dev compose still uses `:latest`, and no version tag could be determined offline); bump the digest deliberately and take a backup first. Each backup records the image used in `seaweedfs-image.txt`.
- No email: forgotten passwords are reset with an admin-issued Reset key (step 4).
- Sessions last at most 24 hours; access tokens expire after `Jwt:ExpiryInMinutes` (default 60).
- Tokens are revoked by sign-out-everywhere, a password change or reset, and disabling a user; rotating `JWT_SECRET` logs everyone out.
- Trust model: an admin can issue a Reset key for any user, including other admins, and sign in as them. Admins cannot otherwise read other users' files. There is no audit log. Fine for a trusted group.
- No monitoring or log shipping; backups are not encrypted, and are copied off-host only if `BACKUP_RCLONE_REMOTE` is set.
