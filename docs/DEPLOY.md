# Production deployment

Stack: `docker-compose.prod.yml` (project `arcano`). Only `web` publishes a port (default `127.0.0.1:8080`); postgres, seaweedfs and api are internal. The dev compose in `backend/` is separate.

## 1. Prerequisites
Linux host with Docker Engine + Compose v2, `openssl`, `git`, and a domain with a TLS reverse proxy (step 5).

## 2. Configure
```
cp .env.production.example .env.production
```
Fill every value. Generate secrets with `openssl rand -hex 32` (use `openssl rand -hex 16` for `S3_ACCESS_KEY`).
Never reuse dev values (`any_key`, `any_secret`, `postgrespassword`). The old dev JWT key remains in git history and must never be used.

## 3. Generate storage config and start
```
scripts/gen-s3-config.sh .env.production
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
```
Run these from the repo root. `gen-s3-config.sh` writes `backend/seaweedfs/s3.prod.json` (mode 600, gitignored). If you change `S3_*` later, re-run it and `up -d` again. `backend/seaweedfs/security.toml` is not needed in production.

## 4. First account
Sign-up is disabled by default. Create the first account: set `ALLOW_REGISTRATION=true` in `.env.production`, run the `up -d` command again, register in the browser, then set it back to `false` and run `up -d` again.

## 5. TLS in front
`web` speaks plain HTTP; the proxy must forward `X-Forwarded-For` and `X-Forwarded-Proto` to `WEB_BIND:WEB_PORT`. `PUBLIC_ORIGIN` must equal the public https URL.

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

## 6. Backup and restore
Both scripts read `.env.production` (override with `ENV_FILE=path`) because the compose file has required variables.
```
scripts/backup.sh               # db.dump + seaweedfs.tar.gz in backups/<UTC time>/, keeps BACKUP_KEEP=7
scripts/backup.sh --stop-api    # stops api briefly for a consistent DB/files pair
```
Cron (daily 03:00): `0 3 * * * cd /opt/arcano && scripts/backup.sh --stop-api >> backups/cron.log 2>&1`
Copy `backups/` off the host; it lives on the same disk otherwise.

Restore (destructive; stops web/api, replaces DB and files, restarts):
```
scripts/restore.sh backups/<timestamp> --yes
```

## 7. Upgrade
```
git pull
docker compose -p arcano --env-file .env.production -f docker-compose.prod.yml up -d --build
```
Database migrations run when the API starts. Take a backup first.

## Not covered
- No high availability; run a single API instance only.
- No email, so no password reset.
- Tokens cannot be revoked; rotating `JWT_SECRET` logs everyone out.
- No monitoring or log shipping; backups are not encrypted or copied off-host.
