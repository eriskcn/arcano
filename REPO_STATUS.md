# Arcano Repository Status Report

**Assessment date:** 2026-10-31
**Repository state:** `main` at `6a6acb6`, plus uncommitted changes to the copy name, content-type error and Dockerfile (audit findings 12-14).

## Overview

Arcano is a monorepo for a personal cloud-storage application: an ASP.NET Core (.NET 10) backend with PostgreSQL and SeaweedFS (S3), and an Angular 22 frontend.

## MVP verdict

**Feature-complete for a personal-use MVP, and verified end to end on a local stack. Not ready to deploy.** Every core workflow works in a real browser and against the real database and storage. What is missing is deployment and operations work, not features.

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

- **Backend:** builds; 109 domain tests and 6 auth tests pass. 3 integration tests pass against the real SeaweedFS (`ARCANO_IT_S3=1`).
- **Frontend:** 126 unit tests pass; the production build succeeds.
- **Docker image (smoke test, 2026-10-31):** the API image built and ran on the Compose network against a separate database. Register, login, small upload, hash-checked 9 MiB chunked upload, and a share link with public download all passed. The test container and database were removed afterwards.
- **Browser pass (2026-10-31):** sign-up and its errors, trash (restore, delete forever, Shift+Delete, empty), drag and drop, previews of text, image, audio, video (PDF shows download only), progress bar on a 60 MB chunked upload, marquee selection and Ctrl+A/Escape, mobile and tablet layouts, and console output (no script errors).

## Bugs found by the browser pass (not fixed)

1. **The "Share" button in the details panel is clipped** at 1400 px wide: the three buttons need 312 px in a 287 px panel (`app.html`, panel is `hidden xl:flex`).
2. **On mobile and tablet the floating "+" overlaps the "Shared" tab** in the bottom navigation.
3. **Dragging a file with a real mouse worked once and then failed on later tries** (no request sent), although synthetic drag events always worked. This may be a limit of the automation tool; check by hand.
4. Smaller: an audio file is labelled "Document"; double-click on a file row does nothing (folders open); the page heading says "My Files" inside a folder; the sign-up error text is the same for an invalid email and a weak password; Delete forever and Empty trash ask for no confirmation.

## Blockers before a real release

1. **Nothing applies database migrations.** There is no `Migrate()` call, so a fresh deploy (including the Compose `api` service) starts against an empty database and fails on the first query. Apply the migrations at startup or in a one-off step.
2. **No production setup.** The compose API serves plain HTTP only, `apiBaseUrl` in the frontend is hard-coded to `http://localhost:5000/api` with one environment file, and there is no TLS or reverse-proxy configuration. Compose also does not pass `Cors__AllowedOrigins`, `ForwardedHeaders__KnownProxies__0`, `Storage__QuotaBytes` or `Storage__MaxUploadBytes` to the API.
3. **Development credentials.** `seaweedfs/s3.json` still holds `any_key`/`any_secret` with Admin rights, the database password has a development default, and the old dev JWT key is in git history.
4. **No backups** of the `postgres_data` and `seaweedfs_data` volumes.
5. **Sign-up is open to anyone** who can reach the app, each account with 5 GiB. Acceptable only on a private network; otherwise it needs an invite or a switch.

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
