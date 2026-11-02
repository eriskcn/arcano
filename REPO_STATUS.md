# Arcano Repository Status Report

**Assessment date:** 2026-11-02
**Repository state:** `main` is at `2407f7f` (group mode and sharing merged after `v0.1.0`; the `v0.1.0` tag, commit `7374246`, is the earlier personal-use cut). Branch `fix/doc-findings` is 3 commits ahead of `main` and pushed to origin; no pull request exists yet.

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

- **Backend:** builds; 143 domain tests and 49 API tests pass (re-run 2026-11-02). The integration suite has 33 tests; in the last run (an agent run on throwaway containers) 28 passed and 5 S3 tests were skipped. It needs a real PostgreSQL and SeaweedFS (`ARCANO_IT_S3=1`, `ARCANO_IT_PG`; each test class uses its own throwaway database) and was **not re-run** on 2026-11-02; the earlier 33-pass result was on 2026-11-01.
- **Frontend:** 215 unit tests in 20 files pass (re-run 2026-11-02). The production build was last run with the 200-spec state (WBS 9.16) and was not re-run for this report.
- **Production stack, run twice on throwaway volumes (2026-10-31):** `docker-compose.prod.yml` came up with only `web` published; migrations created the schema on an empty database; the API container runs as a non-root user (`uid=1654`), its Compose healthcheck gates `web`, and `/health` reports the database and storage. Through nginx: a 90 MB single upload and a 20 MiB chunked upload with matching hashes; sign-up on/off (historical: before group mode, see below); change password (the old token stops working at once), sign out everywhere, refresh; folder delete (contents go to the Trash) and zip download; account deletion (no rows left); the background cleanup job removing an aged trashed file and an abandoned upload session; backup and restore.
- **Browser pass (2026-10-31):** sign-up and its errors, the Trash view, drag and drop, previews of text, image, audio, video (PDF shows download only), the progress bar on a 60 MB chunked upload, marquee selection, mobile and tablet layouts. The fixes made afterwards (below) are covered by unit tests; the confirmation dialogs, double-click preview and the new Settings dialog were not re-checked in a browser. Fixes from 2026-11-02 (sign-up key notice kept after redirect, Ctrl/Shift selection, notices when folders are skipped in bulk actions, folder-zip too-large message, public links of a disabled owner) are covered by unit tests only, not browser-tested. The client-IP fix behind a proxy (`set_real_ip_from` in `frontend/nginx.conf`, `docs/DEPLOY.md` section 5) was checked on throwaway containers with the real API and a plain-HTTP nginx proxy, not with real TLS.

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
5. **Sign-up (changed by group mode, backend 1.16):** `Auth:AllowRegistration` is gone. The first account on an empty install becomes admin; after that sign-up needs a single-use invite key from an admin (403 `Auth.InviteInvalid` otherwise). Backend tested (unit and real PostgreSQL); not yet run on a production stack; the admin UI is built and unit-tested; not browser-tested.

Still not covered: real TLS in front, `web` behind a real proxy, a restore of the file volume while uploads are running (use `backup.sh --stop-api` for a consistent pair), and the Kerberos library warning in the API image log (harmless: no Kerberos is used).

## Should-fix round (2026-10-31)

Done:
- **Accounts:** password length 8; the Identity security stamp is in every token, so a password change or `POST api/Auth/logout` ("sign out everywhere") revokes all other tokens at once; change password and delete account (data, files and uploads are removed) in a new Settings dialog.
- **Cleanup:** a background job (every `Cleanup:IntervalMinutes`, default 60) removes abandoned upload sessions older than 24 hours and trashed files and soft-deleted folders older than 30 days for all users.
- **Folders:** deleting a non-empty folder moves everything inside to the Trash (after a confirmation); a folder downloads as a zip (up to 5,000 files).
- **Operations:** `/health` checks the database and storage; non-root API image with persisted data-protection keys; Compose healthcheck; the dev API port is loopback-only; `security.toml.example` for fresh clones; a GitHub Actions workflow (`.github/workflows/ci.yml`, triggers on push to and pull requests against `main`; it has not been seen running).
- **Tests:** real-PostgreSQL integration tests.
- **Bugs this round's production run found:** the folder zip returned a truncated archive on the real server (Kestrel forbids the synchronous writes `ZipArchive` makes; the unit tests used a `MemoryStream`, which allows them), now fixed by allowing synchronous IO for that one response; and one Postgres test sorted culture-sensitively.

Not done:
- **No email verification** (nothing can send email). Forgotten passwords are reset with an admin-issued Reset key (`POST api/Auth/reset`).
- **Folders** can be shared with other users (read-only) but have no public links and cannot be copied; no content search.
- **Shared views:** the old "Shared" filter for public links stays removed (links are created per file). A "Shared with me" view now lists user-to-user shares.
- **Several API instances** are not supported (upload lock and migrations are per process).
- **Token renewal** still keeps a stolen token usable for up to 24 hours unless the owner signs out everywhere.
- **CI:** `fix/doc-findings` was pushed on 2026-11-02, but no pull request exists and CI results are unknown (the `gh` CLI is not installed here, so nothing could be checked).

## Later

File versioning, public links for folders, folder copy, PDF preview, content search, resume of an upload after a page reload, running more than one API instance (the upload lock is in process), structured logging.

## Group mode

The first sign-up on an empty install becomes admin; after that sign-up needs a single-use invite key. Admins issue invite and Reset keys, set per-user quotas, and disable, enable or delete users.

Verified end to end on the API against a throwaway PostgreSQL database (all scenarios, including disable, Reset key, the last-admin guard and rate limits).

Not verified: the UI in a browser, S3 object removal when a member is deleted, and multi-instance operation.

## Sharing and hardening

- **User-to-user sharing:** files and folders can be shared read-only with a registered user by email (`api/UserShares`, `api/Shared`). Access is re-checked on every request. Members can probe whether an email is registered (accepted for a trusted group). Account and admin delete remove the user's shares. UI: People section in the Share dialog and a "Shared with me" view (not on the mobile bottom bar).
- **Downloads:** owner and public downloads support HTTP Range (206 checked against real SeaweedFS in an integration test).
- **Uploads:** failed multipart aborts keep the session and cleanup retries them; a retried `complete` after success returns the existing file; cancellation is no longer misreported.
- **Audit log:** `Audit {Event} actor= target=` lines in the API log for key, user and quota actions; no table, no key material.
- Not verified: the new UI and flows in a browser (a browser pass is running; the maintainer will add its result); HTTP status mapping of UserShares/Shared is covered by stub-service unit tests (Admin and Auth key paths were already in GroupModeTests); `/api/Shared`, `/api/UserShares` and `/api/Admin` are rate limited (policy `user-api`, 120 per minute per user; `POST api/UserShares` also `user-share-create`, 20 per minute per user), checked by reading `Program.cs` and the controller attributes; the 429 over HTTP was not run; the share zip over real Kestrel; the bootstrap audit line; whether the new tests fail before the change.

## Documentation

The README files and both WBS files were brought up to date after the release-blocker and should-fix work (route table, configuration table, migrations, tests, environments, status sections). All projects now use the released 10.0.4 packages (`ArcanoCore.Application.csproj` was moved off the two preview packages).

## Ops hardening

Backup now includes api keys, env copy and image record, with optional rclone off-host copy; restore validates archives first; prod compose pins SeaweedFS by digest and rotates logs.

## Not verified

- Real TLS in front of `web`, and `web` behind a real proxy.
- The GitHub Actions workflow on GitHub (the YAML parses; no run seen, no pull request).
- The sharing UI (People section, "Shared with me") in a real browser.
- The Settings dialog, the confirmation dialogs and double-click preview in a real browser (unit tests only).
- Real touch input on mobile, keyboard accessibility, and a restore of the file volume while uploads are running.
- Everything in the 2026-11-02 fixes list above in a browser (unit tests only).
- The integration suite on 2026-11-02 (not re-run).
- Test instances: the Angular dev server may still be running on port 4200 and an API on port 5000.

## Known open issues

From the latest read-only review (2026-11-02). Each was confirmed by reading the code or scripts; none was reproduced.

- **Dead session:** `auth.interceptor.ts` has no 401 handling, so after the token is revoked or expires, requests fail with errors and there is no redirect to sign-in (only the route guard signs out).
- **Touch:** the row "more actions" button is `opacity-0 group-hover:opacity-100` (list and grid), so it is invisible without a mouse hover.
- **Upload slot held after a closed tab:** an open chunked upload session reserves its file size against the quota and counts towards `MaxOpenSessions` (3) until it is aborted or swept after 24 hours; the UI has no cancel button (only an automatic abort when a request errors in the page).
- **Failed logins lock users out:** Identity's default lockout (5 failures) applies, and token validation rejects locked users, so anyone who can guess an email (10 attempts per minute per IP) can sign a user out of every session for the lockout period.
- **Navigation:** `selectNav` does nothing when the section is already active, so clicking My Files inside a folder does not go to the root; below `sm` the breadcrumb is hidden, so phones have no way up a folder except the browser back button or changing section.
- **Upload errors:** `fileErrorMessage` maps only 413, `Storage.QuotaExceeded` and `File.InvalidName`; other server reasons (for example `Upload.TooManyOpen`) show the generic "Unable to upload ..." text.

Data-safety risks, not tested:

- **Restore over an upgraded database:** `scripts/restore.sh` runs `pg_restore --clean --if-exists` without `--single-transaction`. `--clean` only drops objects that exist in the dump, so a dump taken before an upgrade leaves tables added by later migrations in place, and a failure part-way is not rolled back. `docs/DEPLOY.md` step 7 takes a backup before upgrading but describes no rollback; a restore is the only path back.
- **Backup while running:** `scripts/backup.sh` tars the SeaweedFS volume while SeaweedFS is running (`--stop-api` stops only the API), so the archive can be inconsistent.
- **`s3.prod.json`:** not in the backup. It is generated from `S3_ACCESS_KEY` and `S3_SECRET_KEY` by `scripts/gen-s3-config.sh`, and `env.production` is in the backup, so it can be regenerated.
