# Work Breakdown Structure (WBS) - Arcano Core

## Objective
Provide the backend API and storage services for Arcano, a personal cloud-storage application. Frontend tasks are tracked in [frontend/wbs/WBS.md](../../frontend/wbs/WBS.md).

## Deliverables
1. ASP.NET Core API using the existing Domain, Application, Infrastructure, and API projects.
2. PostgreSQL metadata persistence and SeaweedFS object-storage integration.
3. File-management endpoints consumed by the frontend.

## Task Hierarchy
- **1. Backend API (ASP.NET Core)**
  - **1.1 Domain layer — Completed**
    - [x] Define file metadata, folder, tag, shared-link, base, and audit entities.
    - [x] Provide the `Result` type.
  - **1.2 Application layer — Completed (foundation)**
    - [x] Define storage and database-context interfaces and file DTOs.
    - [x] Implement file-storage coordination in `FileService`.
  - **1.3 Infrastructure layer — In Progress**
    - [x] Implement SeaweedFS S3 integration.
    - [x] Configure EF Core context and migrations.
    - [x] Read current-user identity from claims.
    - [ ] Verify persistence and storage behavior with integration tests.
  - **1.4 API layer — In Progress**
    - [x] Add file upload, download, and delete endpoints.
    - [x] Add centralized exception handling.
    - [x] Configure dependency injection and JWT authentication.
    - [x] Require authentication on file endpoints.
    - [x] Restrict upload, download, and delete to the authenticated file owner; reject missing/invalid user IDs.
    - [x] Require a configured JWT secret of at least 32 UTF-8 bytes; remove committed JWT secret values and fallbacks. The secret is no longer in `appsettings.Development.json`. Open: the old key is still in git history and must be rotated if it was used anywhere real (see 1.6.1, item 1).
    - [x] Apply explicit upload-size and validation limits. The limit is configurable (`Storage:MaxUploadBytes`, default 100 MB); see 1.11.1.
    - [~] Define and verify failure handling for database/object-storage operations. Partly done: see 1.6.2 items 6 and 7 and 1.10 items 1 and 6. Open: a failed save can still leave the entity tracked in the trash purge loop (1.10 item 1).
  - **1.5 Verification and readiness — In Progress**
    - [x] Build and run available tests; results are recorded in 1.10, 1.11 and 1.15 (see also `REPO_STATUS.md` for current counts).
    - [~] Verify the Docker Compose stack. Live checks against Postgres and SeaweedFS are recorded in 1.8, 1.9 and 1.11, and a throwaway production stack was run twice (1.14, 1.15). The real Compose `api` service itself was exercised through `docker-compose.prod.yml`: migrations ran on an empty database, `/health` reported the database and storage, and uploads worked through nginx. The dev Compose file (`backend/docker-compose.yml`) full-stack start is not recorded in this file.
  - **1.6 Audit remediation — In Progress**
    - Source: [2026-10-30 ponytail audit](reports/2026-10-30-2323-ponytail-audit.md). Item numbers below match the report. Build and tests were verified at the time of the audit (48 domain tests and 2 new auth tests passed); later counts are in 1.10, 1.11 and 1.15.
    - **1.6.1 Must fix — In Progress**
      - [ ] Item 1: partly done. The `Jwt:Secret` value is removed from `appsettings.Development.json`, and the file stays tracked because it holds shared dev defaults. Open: the old key `4a7e…` is still in git history and must be rotated if it is used in any real environment.
      - [x] Item 2: `seaweedfs/security.toml` is in the root `.gitignore` (line 13), added in commit 919cac3.
      - [x] Item 3: `FileService.DeleteFileAsync` saves `IsDeleted` before calling `DeleteAsync`.
      - [x] Item 4: `[RequestSizeLimit(104857600)]` (100 MB) replaces `[DisableRequestSizeLimit]`. The 100 MB value is an assumption; change it if you want a different limit.
      - [x] Item 5: fixed error messages in `FileService` (upload, download, delete) and in `ExceptionHandlingMiddleware`.
    - **1.6.2 Should fix — In Progress**
      - [x] Item 6: an upload's stored object is deleted when the database save fails.
      - [x] Item 7: the delete-failure test seeds the row, so the save path runs and expects `File.DeleteFailed`.
      - [x] Item 8: two sign-in and sign-up tests in the new `ArcanoCore.API.UnitTests` project.
      - [x] Item 9: register returns "Could not create account." for duplicate emails, and sign-in runs a dummy password check when the user is not found.
      - [x] Item 10: the bloom filters are removed from the file and auth flows, so every check reads the database.
      - [x] Item 11: `GetUrlAsync` and `FileResponseDto.Url` are removed from the upload path.
      - [x] Item 12: the file list takes `skip` and `take` (default 50, max 100). The Angular client still requests only the first page; see the open question below.
      - [x] Item 13: `run-dev.sh` starts the dev server without `--host 0.0.0.0`.
      - [x] Item 14: the bloom filter classes, interfaces, DI registrations, and their tests are deleted.
      - [x] Item 15: Download and Delete share `FindOwnedFileAsync`.
      - [x] Item 16: the owner ID is parsed once, in `CurrentOwnerId`.
    - **1.6.3 Nice to have — In Progress**
      - [x] Item 17: the bucket check runs once per process (`_bucketReady`).
      - [x] Item 18: the backend README describes the SeaweedFS identity file correctly.
      - [x] Item 19: the JWT secret is checked once, at startup.
    - **Open question:** the frontend requests only the first page of files. Users with more than 50 files will not see the rest until the client supports paging.
  - **1.7 Folders — In Progress** (API, placement, and tests done; verified against the local API on port 5002, not in a browser)
    - Scope: nested folders per user, with files placed in folders. The frontend side is tracked in [frontend/wbs/WBS.md](../../frontend/wbs/WBS.md) section 4.4.
    - [x] **1.7.1 Data model — Completed.** `Folder` entity (owner, parent, name), EF configuration with unique `(OwnerId, ParentId, Name)` index, and `FileMetadata.FolderId` are in the initial migration.
    - [x] **1.7.2 Folder API — Completed.** `POST/GET/PUT/DELETE /api/Folders`. Create, list children, rename or move (PUT). Duplicate names under the same parent are rejected (`Folder.NameTaken`), including root.
    - [x] **1.7.3 Folder placement — Completed.** Upload takes an optional `folderId`; `GET /api/Files?folderId=` lists one folder (no `folderId` means root files only); `PUT /api/Files/{id}/folder` moves a file.
    - [x] **1.7.4 Folder ownership and safety — Completed.** Every folder operation checks the caller owns the folder and any target parent. Moving a folder into itself or a descendant returns `Folder.Cycle`.
    - [x] **1.7.5 Folder delete — Completed (decision: reject non-empty).** Deleting a folder that has child folders or files returns `Folder.NotEmpty`. No cascade, so no storage cleanup is needed here. Empty folders are soft-deleted. Changed in 1.15: deleting a non-empty folder now moves its contents to the Trash.
    - [x] **1.7.6 Folder tests — Completed.** Unit tests cover duplicate names, cross-owner access, cycle rejection, delete rules, and folder-scoped listing and move.
    - [x] **1.7.7 Folder name index — Completed.** The unique `(OwnerId, ParentId, Name)` index is filtered to live rows (`IsDeleted = false`) by migration `FolderUniqueLiveName`, so a deleted name can be reused under the same parent.
  - **1.8 File management and account basics — In Progress** (from the Google Drive / OneDrive comparison, sections 1–4; at the time, verified with 83 backend tests and a live smoke test against Postgres and SeaweedFS)
    - [x] **1.8.1 File basics — Completed.** Rename a file (`PUT /api/Files/{id}/name`), favorites (`PUT /api/Files/{id}/star`, `isStarred` in the list), list views `view=starred|recent`, and server-side name search `q`. No bulk endpoints: the client loops over single calls.
    - [x] **1.8.2 Storage quota — Completed.** `GET /api/Account/storage` returns used and limit bytes. The limit is `Storage:QuotaBytes` (default 5 GiB). Upload is rejected with `Storage.QuotaExceeded` over the limit. Trashed files still count.
    - [x] **1.8.3 Trash — Completed.** Delete moves a file to the trash and keeps the stored object. Endpoints to list, restore, delete permanently, and empty the trash. A restored file goes to the root if its folder was deleted. Trash older than 30 days is purged when the trash is listed (a shortcut; the background cleanup job in 1.15 now also purges it). No ADR was written for the retention decision.
    - [ ] **1.8.4 Account safety — Partly done.** Done: sign-in and sign-up are rate limited (10 per minute per IP, then 429), and an account locks after 5 wrong passwords. `POST api/Account/password` and `DELETE api/Account` use the same "auth" rate limit and count wrong passwords toward lockout; account delete removes the data first, then the Identity user (the owner foreign keys are Restrict, so the user cannot go first). Also done later: `POST api/Auth/refresh` renews a still-valid token (1.13 finding 5), and logout, change password and security-stamp revocation exist (1.15). Not built: email verification (it needs an email sender; password reset is replaced by admin Reset keys, 1.16.4), and a refresh-token store (`Auth/refresh` renews the access token; no refresh tokens are stored).
    - Migration `FileStarAndTrash` adds `IsStarred` and `DeletedAtUtc` to `FileMetadata`.
    - Known limits: the rate limit keys on the remote IP, so behind a reverse proxy without forwarded headers all clients share one bucket. The trash restore and permanent-delete lookups use an explicit query, because `FindAsync` still applies the "not deleted" filter on the real database; the in-memory test provider does not, so tests alone did not catch it.
  - **1.9 File sharing — In Progress** (file links only; 6 new unit tests plus a live smoke test)
    - [x] **1.9.1 Share links for files — Completed.** `POST /api/Share` (fileId, optional expiry), `GET /api/Share?fileId=`, `DELETE /api/Share/{id}` (revoke), all owner-only. Uses the existing `SharedLink` table, so no migration. The token is 32 random bytes (hex).
    - [x] **1.9.2 Public download — Completed.** `GET /api/Public/{token}/info` returns file name, type, and size; `GET /api/Public/{token}` needs no login and always sends `Content-Disposition: attachment`. Revoked, expired, unknown, or trashed-file links return 404.
    - [ ] **1.9.3 Folder sharing, link passwords, public listing page — Not Started.** No ADR was written for the anonymous-access model; recommended before extending it. The public endpoints are rate limited (1.13 finding 6); there is no download count.

  - **1.10 Code review findings (2026-10-31 whole-project review) — Mostly done** (at the time, 109 domain and 3 auth tests passed)
    - Source: `/code-review` of the whole project on 2026-10-31. Findings were deduped but not verified. Numbers match the review.
    - [~] **Item 1: trash purge order.** `FileService.PurgeAsync` deletes the row and then the stored object in one try block. A storage failure is reported as a failed delete although the row is gone, and a failed save leaves the entity tracked, so the rest of the EmptyTrash and ListTrash loop fails too. **Partly fixed:** The row delete saves first, then the stored object is deleted, and a storage failure is ignored. Not fixed: a failed save still leaves the entity tracked (`shortcut:` comment, the context interface has no `Entry` API).
    - [x] **Item 2: Recent view order.** `ApplicationDbContext.SaveChangesAsync` sets `LastModifiedAtUtc` on every change, so starring or moving a file moves it to the top of Recent. **Fixed:** The save hook skips `LastModifiedAtUtc` when only `IsStarred` or `FolderId` changed. Checked live: starring an old file leaves it below a newer one in Recent.
    - [~] **Item 3: quota race.** The quota check in upload is check-then-act, so parallel uploads can each pass and together go over the limit. **Partly fixed:** one upload per user at a time, using an in-process lock (a `shortcut:` comment in `FileService`). It is only race-free on a single API instance. One unit test covers it.
    - [x] **Item 4: upload field lengths.** File name (255) and content type (100) are not validated before the S3 upload. The save fails, and the user gets a generic error that repeats on every retry. **Fixed:** `File.NameTooLong` and `File.ContentTypeTooLong` are returned before the S3 upload. Checked live with a 300-character name.
    - [x] **Item 5: share expiry time zone.** `ShareService` writes `ExpiresAtUtc` without normalizing `DateTime.Kind`. A value without `Z`, or with an offset, makes Npgsql throw, so the client gets a 500 instead of a link or a 400. **Fixed:** Expiry is normalized to UTC. Checked live with a value without `Z`.
    - [x] **Item 6: exception middleware.** `ExceptionHandlingMiddleware` does not check `Response.HasStarted` and logs client aborts (`OperationCanceledException`) as 500s. A failure mid-download throws a second exception. **Fixed:** Client aborts are not logged as errors, and nothing is written once the response has started. Unit tests do not cover this; not exercised live.
    - [x] **Item 7: storage key.** `SeaweedStorageService` builds the key as `{Guid}_{fileName}`. Names with `/`, `..` or long multi-byte text can go over S3's 1024-byte key limit or nest paths. The Guid alone is enough. **Fixed:** The key is now only a Guid. Checked live with a file named `a/..weird name.txt`: it uploads and downloads.
    - [~] **Item 10 (backend part): duplicated code.** `FilesController` repeats the NotFound/BadRequest mapping that `ToNoContent` already does, and the `CurrentOwnerId` parsing is copied in `FileService`, `FolderService` and `ShareService`. This is a cleanup, not a bug. **Partly fixed:** `FilesController` uses `ToNoContent` for Delete and MoveFolder. The `CurrentOwnerId` copies stay; sharing them needs a new abstraction.
    - [x] **Below the cutoff:** files trashed before migration `FileStarAndTrash` have a null `DeletedAtUtc` and are never auto-purged. The CORS origin is hard-coded to `localhost:4200`. **Fixed:** Trashed files with a null `DeletedAtUtc` are purged using `LastModifiedAtUtc ?? CreatedAtUtc`. The CORS origins now come from `Cors:AllowedOrigins`.
  - **1.11 Large files — In Progress** (109 domain tests at the time; live checks against Postgres and SeaweedFS)
    - [x] **1.11.1 Configurable upload limit — Completed.** `Storage:MaxUploadBytes` (default 100 MB) sets both the Kestrel body limit (plus 1 MB for multipart overhead) and the multipart form limit. The fixed `[RequestSizeLimit]` is removed. An oversized upload returns 413 `File.TooLarge`. Checked live: with a 1 MB limit, a 0.5 MB file passes and 1.5 MB and 3 MB return 413. With a 200 MB limit, a 150 MB file uploads and downloads with the right size. The Kestrel limit now applies to every endpoint, not only upload.
    - [x] **1.11.2 Parallel uploads and quota — Completed for one API instance.** See 1.10 item 3.
    - [x] **1.11.3 Resumable (chunked) uploads — Completed for retry within one session.** `POST /api/Uploads` starts a session (checks name, type, folder, and quota), `PUT /api/Uploads/{id}/parts/{n}` stores one 8 MiB part, `GET /api/Uploads/{id}` lists stored parts, `POST /api/Uploads/{id}/complete` joins them into a file, `DELETE /api/Uploads/{id}` aborts. It uses S3 multipart and a new `UploadSession` table (migration `UploadSession`). Part sizes are checked exactly, and the complete step checks all parts and the quota again under the per-user lock. Five unit tests; checked live with a 30 MB file (resume after parts 1 and 2, wrong-size and out-of-range parts rejected, other user gets 404, hash identical after download). No ADR was written for the design.
    - [ ] **1.11.4 Chunked upload limits — In Progress.** Abandoned sessions older than 24 hours are aborted and removed when their owner starts another upload (unit-tested, not run against SeaweedFS); an owner who never returns leaves theirs (a `shortcut:` comment in `UploadService`). A page reload loses the session, so the file restarts. File size is limited only by quota and S3's 10,000 parts (about 80 GB). `MaxUploadBytes` does not limit chunked files; the limit on each request is its part size.
  - [x] **1.12 File copy — Completed.** `POST api/Files/{id}/copy` with `{folderId}` duplicates a file (new id and storage key) into a folder or the root, under the per-user upload lock with a quota check. The object is streamed through the API (`shortcut:` comment: switch to S3 CopyObject if copies get heavy). 3 unit tests; checked live (201, same content, usage doubled). Folders cannot be copied.
  - [x] **1.13 Audit fixes (2026-10-31 `ponytail-audit`, findings 1-14) — Completed**
    - **Finding 1: unfinished uploads could fill storage.** At most 3 open upload sessions per user (`Upload.TooManyOpen`), and the sizes of open sessions count in the early quota check. A short per-user lock makes the cap hold under concurrent starts. Checked live: 30 concurrent starts, 3 accepted. 2 unit tests. Still open: abandoned sessions of users who never return are never cleaned up.
    - **Finding 2: Compose exposed the database and file store.** Postgres and SeaweedFS ports are bound to 127.0.0.1; the Postgres password and the API's S3 keys come from `POSTGRES_PASSWORD`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` (dev defaults). SeaweedFS still reads the static `seaweedfs/s3.json` identity, which must be replaced and kept in step with the S3 variables before any deployment. README "Before deploying" notes added. Not run: `docker compose up` with the new file.
    - **Finding 3: see frontend WBS 9.5.**
    - **Finding 4: file saved into a deleted folder.** Single upload, copy and chunked complete re-check the target folder just before saving and fall back to the root if it was deleted meanwhile; the chunked start check now ignores deleted folders. 3 unit tests. Not reproduced live (needs a folder deleted mid-upload).
    - **Finding 5: login token expired during long uploads.** New `POST api/Auth/refresh` returns a fresh token for a still-valid one. Tokens carry a `session_start` claim and refresh is refused 24 hours after login (`Auth.SessionExpired`), or for a missing claim, a missing or locked-out user. Old tokens without the claim cannot refresh. 3 unit tests; checked live (200, same `session_start`, no token gives 401). Tradeoff: a stolen token can be renewed for up to 24 hours; there is still no revocation or refresh token.
    - **Finding 6: no rate limit on public share endpoints.** `api/Public/{token}/info` is limited to 60 and the download to 20 requests a minute per IP. `ForwardedHeaders:KnownProxies` turns on forwarded headers only for listed proxy IPs. Checked live: 429 after the limit, and a spoofed `X-Forwarded-For` did not dodge it. Not checked behind a real proxy.
    - **Findings 12, 13 and 14: small fixes.** (12) A copied file is named `Copy of <name>`, cut to 255 characters; 2 tests. (14) An empty content type returns `File.ContentTypeRequired`; `File.ContentTypeTooLong` is kept for more than 100 characters; 3 tests. Both checked live. (13) The Dockerfile uses the stable `10.0` image tags (checked that both exist). Building the image then showed it was already broken: `dotnet restore` ran on the whole solution, but the test projects were never copied into the image. It now restores only the API project, and `docker build` succeeds. Not run: starting the built image against the Compose stack.
    - **Findings 9 and 10: unused code removed.** The `FileTag` entity, configuration, `DbSet` and `FileMetadata.Tags` are gone, and so are `SharedLink.FolderId`, `SharedLink.Folder` and `Folder.SharedLinks` (folder sharing is not built). Migration `RemoveUnusedFileTagAndShareFolder` drops the `FileTag` table and the `SharedLink.FolderId` column; it was applied to the local database, where both were empty. Checked live: list, share create, public info and download, list links, revoke. Deploying needs the migration applied, and rolling back needs `Down` (it recreates the empty table and column).
    - **Finding 7: no automated test of the real S3 upload path.** New project `ArcanoCore.IntegrationTests` runs the real `UploadService` and `SeaweedStorageService` against the Compose SeaweedFS: 3-part upload with a matching SHA-256, resume with parts out of order, abort. It skips unless `ARCANO_IT_S3=1`; run it with `docker compose up -d seaweedfs` then `ARCANO_IT_S3=1 dotnet test backend/test/ArcanoCore.IntegrationTests` (3 tests pass). It covers storage and the upload service, not the HTTP layer or PostgreSQL (the test database is in memory), and it leaves objects in the `arcano-integration-tests` bucket.
  - [x] **1.14 Release blockers (2026-10-31) — Completed, verified on a throwaway production stack**
    - Migrations run at API startup (`Database:MigrateOnStartup`, default on, 5 retries); `Auth:AllowRegistration` (default on; production compose default off) made sign-up return 403 `Auth.RegistrationDisabled`; replaced by invite keys in 1.16.
    - `docker-compose.prod.yml`, `.env.production.example`, `scripts/gen-s3-config.sh`, `scripts/backup.sh`, `scripts/restore.sh`, `docs/DEPLOY.md`. Backup and restore were run for real (restore removed a file added after the backup); this found and fixed a bug (the scripts did not pass the env file).
    - Not covered: TLS (terminate it in front of `web`), several API instances, an automatic backup schedule or off-host copies, rewriting the git history that contains the old dev JWT key.
  - [x] **1.15 Should-fix round (2026-10-31) — Completed except the items listed as not done**
    - Accounts: password length 8, security-stamp revocation (change password and logout revoke all tokens), `POST api/Auth/logout`, `POST api/Account/password`, `DELETE api/Account`.
    - Background cleanup job (`Cleanup:Enabled`, `Cleanup:IntervalMinutes`): removes abandoned upload sessions older than 24 hours, trashed files older than 30 days, and soft-deleted folders older than 30 days once empty (see below), for all users. This supersedes the "never cleaned up" notes in 1.11.4 and 1.13 finding 1.
    - Deep `/health` (database and storage, 503 on failure); non-root API image with persisted DataProtection keys (`DataProtection:KeysPath`); Compose healthcheck on `api`; CI workflow (`.github/workflows/ci.yml`); `backend/seaweedfs/security.toml.example`.
    - Folder delete moves its contents to the Trash instead of rejecting non-empty folders; folder zip download (`GET api/Folders/{id}/download`, at most 5,000 files).
    - Folder favorites: `PUT api/Folders/{id}/star` with `{isStarred}` (204), `isStarred` in `FolderDto`; needs a migration for `Folder.IsStarred`. The cleanup job also hard-deletes soft-deleted folders older than 30 days once no file or child folder row references them (leaf first, repeated until none). Unit tests added (they pass; see the current totals below).
    - Real-PostgreSQL integration tests (`ARCANO_IT_PG`), 25 integration tests together with the S3 ones. Current totals, verified at the v0.1.0 tag: 118 domain tests, 16 API tests and 25 integration tests (S3 + PostgreSQL). Current totals (2026-11-01, with group mode): 125 domain, 27 API and 28 integration tests.
    - Two bugs found by the production-stack run: the folder zip needed `AllowSynchronousIO` because `ZipArchive` writes synchronously (found only against real Kestrel, not in unit tests); and a Postgres test sorted culture-sensitively (now ordinal).
    - Not done: email-based password reset and verification (need email sending; admin Reset keys exist since 1.16.4), folder copy and folder sharing, content search, multi-instance safety (in-process upload lock, migrations at every start).
  - [ ] **1.16 Group mode: admin and invite keys — In Progress (implemented and tested; 1.16.6 partly open)**
    - Evidence: an end-to-end API run on 2026-11-01 against a throwaway PostgreSQL database covered the behavior of 1.16.2-1.16.7 (all scenarios, including disable, Reset key, last-admin guard and rate limits). Not covered: the UI in a browser, S3 object removal on member delete, multi-instance.
    - Goal: 3-10 trusted people on one server without email. No deployment switch: a personal cloud is a group cloud with one user. Closes the group gaps found by the 2026-11-01 suitability review (no way to add users, no admin, one global quota, no password reset).
    - Design choice: `IdentityUser<Guid>` is unchanged. A `UserProfile` table keyed by user id holds `IsAdmin`, nullable `QuotaBytes` and `CreatedAtUtc`; "disabled" is an Identity lockout (`LockoutEnd` = max plus a security-stamp rotation), so existing login and token checks already refuse it.
    - [x] **1.16.1 Data model.** Migration `GroupMode`: `InviteKey` (hash, type, quota, target user, expiry, used-at, created-by) and `UserProfile`; the migration backfills a non-admin profile for every existing user. Verified on real PostgreSQL (migrating a database that already had a user).
    - [x] **1.16.2 First account is admin.** Empty install: the first sign-up ignores the key and becomes admin (`GET /api/Auth/setup`, `requiresKey`). Existing installs: `Auth:BootstrapAdminEmail` promotes that user only if no admin exists; with users but no admin and no email it logs a warning and promotes nobody. Unit-tested. Sign-ups are serialized by an in-process lock (single instance only).
    - [x] **1.16.3 Join keys.** 32 random bytes, only the SHA-256 hash is stored, single use (a concurrency token on `UsedAtUtc`; 8 parallel redeems of one key gave exactly one success on PostgreSQL), 7 days default and 30 max, optional quota for the member. A failed sign-up (weak password) releases the key. `Auth:AllowRegistration` is removed from code, appsettings, README, prod compose, `.env` example and DEPLOY.md.
    - [x] **1.16.4 Reset keys.** `POST /api/Auth/reset`; the password reset rotates the security stamp; wrong type, expired and used keys give 403. Unit and PostgreSQL tests.
    - [x] **1.16.5 Admin endpoints (admin only).** `AdminController` behind an authorization policy that reads the profile on every request (the token's `role: Admin` claim is a UI hint only). Users, quota, disable, enable, delete (data first), keys list, create, revoke. The policy handler is unit-tested; the HTTP-level 403 for non-admins was observed in the end-to-end API run on 2026-11-01.
    - [ ] **1.16.6 Guards.** Done and tested: the last active admin cannot be deleted or disabled (409 `Admin.LastAdmin`; a disabled admin does not count); no endpoint reads other users' files. Open: there is no demote endpoint (so nothing to guard); the `auth` rate limit is on sign-up and reset only, not on the admin endpoints (admin-only, not rate limited). The rate limits on sign-up and reset were observed over HTTP in the 2026-11-01 run.
    - [x] **1.16.7 Per-user quota.** `StorageQuotaService` reads the user's own limit, else `Storage:QuotaBytes`; upload, chunked start/complete and copy all go through it (the only readers of the global limit). Unit-tested at the service; the three service call sites were not re-tested with a per-user limit.
    - [x] **1.16.8 Tests and docs.** 7 more domain tests, 11 more API tests, 3 more PostgreSQL integration tests; README (routes, config), `docs/DEPLOY.md` (step 4, upgrade note), `.env.production.example`, `REPO_STATUS.md`. The production stack was not re-run with group mode.
    - Depends on: the frontend items 3.1-3.4 and 2.3 for the UI. Out of scope: user-to-user and folder sharing, forced password change on first login, email.
  - [x] **1.17 Group mode follow-ups (2026-11-01) — Completed (unit-tested; 7 new API tests, totals 125 / 34 / 28)**
    - [x] Last admin cannot self-delete (409 `Admin.LastAdmin`, unless they are the sole user).
    - [x] Startup bootstrap rules: with no admin, `Auth:BootstrapAdminEmail` promotes that user (skipped if disabled); with no email and exactly one user, that user is promoted (warning logged); with several users and no email an error is logged and nobody is promoted. Not run against a real upgraded database; the self-delete check counts `UserProfiles` rows, not Identity users. The two-admins race is left with a `shortcut:` comment (no transaction).
    - Known accepted limits: two admins disabling each other at the same instant; first-user race protection is per process; no rate limit on admin endpoints; invite keys are never purged; the user list is unpaged; login timing reveals disabled accounts; Reset key trust model (an admin can reset any user, including other admins, and sign in as them; no audit log).

## Dependencies
- 1.2 depends on 1.1.
- 1.7.2 depends on 1.7.1. 1.7.3 and 1.7.4 depend on 1.7.2. 1.7.5 depends on the decision in 1.7.5 and on item 1.4 failure handling.
- 1.3 depends on 1.2.
- 1.4 depends on 1.3.
- 1.5 depends on the API and infrastructure tasks it verifies.
- 1.6.2 item 7 depends on 1.6.1 item 3. 1.6.2 items 10 and 14 are both done (the bloom filters are removed).
- Frontend integration depends on stable API contracts and authorization behavior.

## Success Criteria
- Backend builds and available automated tests pass.
- Authenticated users can upload, retrieve, and delete only their own files.
- Metadata and stored objects remain consistent through successful operations and handled failures.
- Upload constraints and production authentication secrets are explicitly configured.
- The documented Docker Compose setup starts the stack and supports the basic API workflow.

## Overall Status
**In Progress.** File endpoint authentication and owner checks are implemented. The JWT secret check is implemented and the secret is out of the Development file; only rotating the old key in git history is open. Upload limits are configurable, and the 19 findings of the 2026-10-30 audit (1.6) are done, apart from that rotation. Release blockers (1.14) and the should-fix round (1.15) are done and were run on a throwaway production stack. Open: the failure-handling gaps in section 1.4, the items marked not done in 1.15, TLS in front of the stack, and multi-instance safety. 1.16 group mode (admin, invite keys, per-user quota) is implemented, tested and verified end to end on the API (2026-11-01); its open points and the 1.17 follow-ups are listed there. Email-based reset and verification are not built (admin Reset keys replace reset). Status reflects the work up to 2026-11-01; update it as implementation and verification progress.
