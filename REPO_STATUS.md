# Arcano Repository Status Report

**Assessment date:** 2026-10-31
**Repository state:** `main`; the release-blocker work (production compose, scripts, frontend image, migrate on startup, sign-up switch) may still be partly uncommitted; check `git status`.

## Overview

Arcano is a monorepo for a personal cloud-storage application: an ASP.NET Core (.NET 10) backend with PostgreSQL and SeaweedFS (S3), and an Angular 22 frontend.

## MVP verdict

**Feature-complete for a personal-use MVP, verified end to end in a browser and on a throwaway production stack. The five release blockers are addressed; TLS in front is still yours to set up, and the Should-fix list below remains.**

| MVP area | Status | Evidence |
|---|---|---|
| Sign-up, sign-in, sign-out, session renewal | Done | Browser: sign-up, errors, sign-out. API: refresh (24 h cap) |
| Upload, download, rename, star, move, copy | Done | Browser and API |
| Folders, drag and drop | Done | Browser (see drag note below) |
| Trash: restore, delete forever, Shift+Delete, empty | Done | Browser |
| Storage quota and usage | Done | Browser and API |
| Large files: limit, progress bar, chunked and resumable upload | Done | Browser: 60 MB progress bar; hash-checked 20 MiB; real S3 integration test |
| Share links: create, expire, revoke, public page | Done (files only) | Browser and API |
| Previews: image, text, audio, video | Done (PDF: download only) | Browser |
| Keyboard shortcuts and right-click menu | Done | Browser |
| Docker image | Builds and runs | Smoke test below |

## What is verified

- **Backend:** builds; 109 domain tests and 7 auth tests pass. 3 integration tests pass against the real SeaweedFS (`ARCANO_IT_S3=1`).
- **Frontend:** 128 unit tests pass; the production build succeeds.
- **Docker image (smoke test, 2026-10-31):** the API image built and ran on the Compose network against a separate database. Register, login, small upload, hash-checked 9 MiB chunked upload, and a share link with public download all passed. The test container and database were removed afterwards.
- **Browser pass (2026-10-31):** sign-up and its errors, trash (restore, delete forever, Shift+Delete, empty), drag and drop, previews of text, image, audio, video (PDF shows download only), progress bar on a 60 MB chunked upload, marquee selection and Ctrl+A/Escape, mobile and tablet layouts, and console output (no script errors).

## Bugs found by the browser pass (not fixed)

1. **The "Share" button in the details panel is clipped** at 1400 px wide: the three buttons need 312 px in a 287 px panel (`app.html`, panel is `hidden xl:flex`).
2. **On mobile and tablet the floating "+" overlaps the "Shared" tab** in the bottom navigation.
3. **Dragging a file with a real mouse worked once and then failed on later tries** (no request sent), although synthetic drag events always worked. This may be a limit of the automation tool; check by hand.
4. Smaller: an audio file is labelled "Document"; double-click on a file row does nothing (folders open); the page heading says "My Files" inside a folder; the sign-up error text is the same for an invalid email and a weak password; Delete forever and Empty trash ask for no confirmation.

## Release blockers (2026-10-31): now addressed, verified on a throwaway production stack

Verified by bringing up `docker-compose.prod.yml` with throwaway secrets (project `arcano`, fresh volumes, one loopback port), then removing it. Your dev containers and data were not touched.

1. **Migrations:** the API now applies them at startup (`Database:MigrateOnStartup`, 5 retries). On an empty database it created all 12 tables. Several API instances starting together could race (single instance only).
2. **Production setup:** `docker-compose.prod.yml` publishes only the `web` service (nginx: serves the app and proxies `/api` and `/health` on the same origin, so no CORS and no hard-coded API address). Checked through nginx: a 90 MB single upload and a 20 MiB chunked upload both download with matching hashes, the public rate limit returns 429, and the app shell and deep links load. **TLS is not included:** put a TLS-terminating proxy or load balancer in front of `web` (see `docs/DEPLOY.md`).
3. **Credentials:** the production compose has no defaults for `POSTGRES_PASSWORD`, `JWT_SECRET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` or `PUBLIC_ORIGIN`; `scripts/gen-s3-config.sh` writes the SeaweedFS identity from the S3 keys and refuses the dev defaults. The old dev JWT key is still in git history (not rewritten); production must use a fresh `JWT_SECRET`.
4. **Backups:** `scripts/backup.sh` and `scripts/restore.sh` were run for real: a backup, then a restore that removed a file added after the backup and kept the older files, which still downloaded. The check found and fixed a bug (the scripts did not pass the env file). Backups stay on the same host until you copy them off; there is no automatic schedule (cron example in `docs/DEPLOY.md`).
5. **Open sign-up:** `Auth:AllowRegistration` (production compose default: off, `ALLOW_REGISTRATION`). With it off, sign-up returns 403 `Auth.RegistrationDisabled` and the sign-up page shows "Sign-up is disabled on this server."; existing users still sign in. Create the first account by starting once with it on (see `docs/DEPLOY.md`).

Still not covered: real TLS in front, `web` behind a real proxy, a restore of the file volume while uploads are running (use `backup.sh --stop-api` for a consistent pair), and the Kerberos library warning in the API image log (harmless: no Kerberos is used).

## Should fix

- **Accounts:** no password reset, email verification, change password or account deletion; the minimum password length is 6 with no complexity rule; there is no way to revoke a token, and a stolen one can be renewed for up to 24 hours.
- **Cleanup:** no background jobs. Abandoned upload sessions are swept only when their owner starts another upload, and the 30-day trash purge runs only when someone opens the Trash.
- **Folders:** a non-empty folder cannot be deleted (move or trash the files first); folders cannot be copied, downloaded or shared.
- **Operations:** `/health` returns a fixed response and does not check the database or storage; the compose `api` service has no healthcheck; the API container runs as root and is published on all interfaces (`5000:8080`); there is no CI.
- **Tests:** PostgreSQL behaviour is not covered (tests use an in-memory database; EF `FindAsync` behaves differently on the real one).
- **Fresh clone:** `backend/seaweedfs/security.toml` is mounted by Compose but ignored by git, so a fresh clone has no such file.
- **Docs:** README files are stale (see below).

## Later

File versioning, folder sharing, per-user quotas, admin features, PDF preview, content search, resume of an upload after a page reload, running more than one API instance (the upload lock is in process), structured logging.

## Stale documentation (from the docs review, not yet fixed)

- Root `README.md` still describes the frontend as a scaffold and the file API as "list, upload, download, and delete".
- `backend/README.md` tells you to run `dotnet ef migrations add InitialCreate` (migrations already exist) and pins an old `dotnet-ef` preview; its endpoint list omits most routes (folders, share, public, uploads, copy, trash, refresh, account); it still mentions `[RequestSizeLimit]`; and its configuration section omits `Storage:QuotaBytes`, `Storage:MaxUploadBytes`, `Cors:AllowedOrigins` and `Jwt:*`. Neither README tells you to apply migrations before the first start.
- `frontend/README.md` documents `ng e2e`, but no e2e target exists.
- `backend/wbs/WBS.md` sections 1.5 and 1.6 and `frontend/wbs/WBS.md` sections 1.1, 4.4, 5, 6 and the Status paragraph still carry statuses from before the recent work.

## Not verified

- Starting the real Compose `api` service (only the same image run by hand).
- Real touch input on mobile, keyboard accessibility, and the 30-day trash expiry.
- Behaviour behind a real TLS reverse proxy, and after an API restart.
- Test API instances: one is left running on port 5000; the Angular dev server is on port 4200.
