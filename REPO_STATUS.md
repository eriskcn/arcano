# Arcano Repository Status Report

**Assessment date:** 2026-10-31
**Repository state:** the `v0.1.0` tag (commit `7374246`) on `main`.

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

- **Backend:** builds; 118 domain tests and 16 API tests pass. 25 integration tests pass against the real SeaweedFS and a real PostgreSQL (`ARCANO_IT_S3=1`, `ARCANO_IT_PG`; each test class uses its own throwaway database).
- **Frontend:** 147 unit tests pass; the production build succeeds.
- **Production stack, run twice on throwaway volumes (2026-10-31):** `docker-compose.prod.yml` came up with only `web` published; migrations created the schema on an empty database; the API container runs as a non-root user (`uid=1654`), its Compose healthcheck gates `web`, and `/health` reports the database and storage. Through nginx: a 90 MB single upload and a 20 MiB chunked upload with matching hashes; sign-up on/off; change password (the old token stops working at once), sign out everywhere, refresh; folder delete (contents go to the Trash) and zip download; account deletion (no rows left); the background cleanup job removing an aged trashed file and an abandoned upload session; backup and restore.
- **Browser pass (earlier today):** sign-up and its errors, the Trash view, drag and drop, previews of text, image, audio, video (PDF shows download only), the progress bar on a 60 MB chunked upload, marquee selection, mobile and tablet layouts. The fixes made afterwards (below) are covered by unit tests; the confirmation dialogs, double-click preview and the new Settings dialog were not re-checked in a browser.

## Browser-pass bugs: fixed, and what is left

Fixed (unit-tested): the clipped Share button, the floating "+" covering the "Shared" tab, audio files labelled "Document", double-click now opens the preview, the folder page heading, the sign-up error message (the server's message is shown), and confirmations for Delete forever, Empty trash and folder delete.

Also fixed: **uploading from a phone** (the floating button on phones now opens the file picker; from tablet width up the header Upload button is used). Checked in a browser at 375 and 768 px, including an upload through the same input.

Still open:
1. **Dragging with a real mouse** onto a folder worked once and then failed on later tries, although synthetic drag events always worked. This may be a limit of the automation tool; check by hand.

## Release blockers (2026-10-31): now addressed, verified on a throwaway production stack

Verified by bringing up `docker-compose.prod.yml` with throwaway secrets (project `arcano`, fresh volumes, one loopback port), then removing it. Your dev containers and data were not touched.

1. **Migrations:** the API now applies them at startup (`Database:MigrateOnStartup`, 5 retries). On an empty database it created all 12 tables. Several API instances starting together could race (single instance only).
2. **Production setup:** `docker-compose.prod.yml` publishes only the `web` service (nginx: serves the app and proxies `/api` and `/health` on the same origin, so no CORS and no hard-coded API address). Checked through nginx: a 90 MB single upload and a 20 MiB chunked upload both download with matching hashes, the public rate limit returns 429, and the app shell and deep links load. **TLS is not included:** put a TLS-terminating proxy or load balancer in front of `web` (see `docs/DEPLOY.md`).
3. **Credentials:** the production compose has no defaults for `POSTGRES_PASSWORD`, `JWT_SECRET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` or `PUBLIC_ORIGIN`; `scripts/gen-s3-config.sh` writes the SeaweedFS identity from the S3 keys and refuses the dev defaults. The old dev JWT key is still in git history (not rewritten); production must use a fresh `JWT_SECRET`.
4. **Backups:** `scripts/backup.sh` and `scripts/restore.sh` were run for real: a backup, then a restore that removed a file added after the backup and kept the older files, which still downloaded. The check found and fixed a bug (the scripts did not pass the env file). Backups stay on the same host until you copy them off; there is no automatic schedule (cron example in `docs/DEPLOY.md`).
5. **Open sign-up:** `Auth:AllowRegistration` (production compose default: off, `ALLOW_REGISTRATION`). With it off, sign-up returns 403 `Auth.RegistrationDisabled` and the sign-up page shows "Sign-up is disabled on this server."; existing users still sign in. Create the first account by starting once with it on (see `docs/DEPLOY.md`).

Still not covered: real TLS in front, `web` behind a real proxy, a restore of the file volume while uploads are running (use `backup.sh --stop-api` for a consistent pair), and the Kerberos library warning in the API image log (harmless: no Kerberos is used).

## Should-fix round (2026-10-31)

Done:
- **Accounts:** password length 8; the Identity security stamp is in every token, so a password change or `POST api/Auth/logout` ("sign out everywhere") revokes all other tokens at once; change password and delete account (data, files and uploads are removed) in a new Settings dialog.
- **Cleanup:** a background job (every `Cleanup:IntervalMinutes`, default 60) removes abandoned upload sessions older than 24 hours and trashed files and soft-deleted folders older than 30 days for all users.
- **Folders:** deleting a non-empty folder moves everything inside to the Trash (after a confirmation); a folder downloads as a zip (up to 5,000 files).
- **Operations:** `/health` checks the database and storage; non-root API image with persisted data-protection keys; Compose healthcheck; the dev API port is loopback-only; `security.toml.example` for fresh clones; a GitHub Actions workflow (`.github/workflows/ci.yml`, active in the repo; as far as verified it has not yet run on GitHub).
- **Tests:** real-PostgreSQL integration tests.
- **Bugs this round's production run found:** the folder zip returned a truncated archive on the real server (Kestrel forbids the synchronous writes `ZipArchive` makes; the unit tests used a `MemoryStream`, which allows them), now fixed by allowing synchronous IO for that one response; and one Postgres test sorted culture-sensitively.

Not done:
- **No password reset or email verification** (nothing can send email), so a forgotten password needs a manual database reset.
- **Folders** cannot be copied or shared; no content search.
- **Shared view removed:** the backend only supports share links you create, so there is no "Shared" nav item or filter; links are created per file.
- **Several API instances** are not supported (upload lock and migrations are per process).
- **Token renewal** still keeps a stolen token usable for up to 24 hours unless the owner signs out everywhere.
- **CI:** push and check the CI run; the workflow has not run on GitHub as far as verified.

## Later

File versioning, folder sharing, per-user quotas, admin features, PDF preview, content search, resume of an upload after a page reload, running more than one API instance (the upload lock is in process), structured logging.

## Documentation

The README files and both WBS files were brought up to date after the release-blocker and should-fix work (route table, configuration table, migrations, tests, environments, status sections). All projects now use the released 10.0.4 packages (`ArcanoCore.Application.csproj` was moved off the two preview packages).

## Not verified

- Real TLS in front of `web`, and `web` behind a real proxy.
- The GitHub Actions workflow on GitHub (the YAML parses; it has not run as far as verified).
- The Settings dialog, the confirmation dialogs and double-click preview in a real browser (unit tests only).
- Real touch input on mobile, keyboard accessibility, and a restore of the file volume while uploads are running.
- Test instances: the Angular dev server may still be running on port 4200 and an API on port 5000.
