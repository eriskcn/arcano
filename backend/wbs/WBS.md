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
    - [ ] Require a configured JWT secret of at least 32 UTF-8 bytes; remove committed JWT secret values and fallbacks. Reopened by the 2026-10-30 audit: `appsettings.Development.json` is still tracked, and its working copy holds an uncommitted key (see 1.6.1, item 1).
    - [ ] Apply explicit upload-size and validation limits; `[DisableRequestSizeLimit]` remains on the upload endpoint and no approved maximum is defined.
    - [ ] Define and verify failure handling for database/object-storage operations.
  - **1.5 Verification and readiness — Not Started**
    - [ ] Build and run available tests; record actual results.
    - [ ] Verify the Docker Compose stack and documented local quickstart; Compose syntax validates, but runtime startup has not been exercised.
  - **1.6 Audit remediation — In Progress**
    - Source: [2026-10-30 ponytail audit](reports/2026-10-30-2323-ponytail-audit.md). Item numbers below match the report. Build and tests verified: 48 domain tests and 2 new auth tests pass.
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
    - [x] **1.7.5 Folder delete — Completed (decision: reject non-empty).** Deleting a folder that has child folders or files returns `Folder.NotEmpty`. No cascade, so no storage cleanup is needed here. Empty folders are soft-deleted.
    - [x] **1.7.6 Folder tests — Completed.** Unit tests cover duplicate names, cross-owner access, cycle rejection, delete rules, and folder-scoped listing and move.
    - [x] **1.7.7 Folder name index — Completed.** The unique `(OwnerId, ParentId, Name)` index is filtered to live rows (`IsDeleted = false`) by migration `FolderUniqueLiveName`, so a deleted name can be reused under the same parent.
  - **1.8 File management and account basics — In Progress** (from the Google Drive / OneDrive comparison, sections 1–4; verified with 83 backend tests and a live smoke test against Postgres and SeaweedFS)
    - [x] **1.8.1 File basics — Completed.** Rename a file (`PUT /api/Files/{id}/name`), favorites (`PUT /api/Files/{id}/star`, `isStarred` in the list), list views `view=starred|recent`, and server-side name search `q`. No bulk endpoints: the client loops over single calls.
    - [x] **1.8.2 Storage quota — Completed.** `GET /api/Account/storage` returns used and limit bytes. The limit is `Storage:QuotaBytes` (default 5 GiB). Upload is rejected with `Storage.QuotaExceeded` over the limit. Trashed files still count.
    - [x] **1.8.3 Trash — Completed.** Delete moves a file to the trash and keeps the stored object. Endpoints to list, restore, delete permanently, and empty the trash. A restored file goes to the root if its folder was deleted. Trash older than 30 days is purged when the trash is listed (a shortcut; move it to a background job if needed). No ADR was written for the retention decision.
    - [ ] **1.8.4 Account safety — Partly done.** Done: sign-in and sign-up are rate limited (10 per minute per IP, then 429), and an account locks after 5 wrong passwords. Blocked: password reset and email verification need an email sender, and refresh tokens need a token table and a design decision. None of these are built.
    - Migration `FileStarAndTrash` adds `IsStarred` and `DeletedAtUtc` to `FileMetadata`.
    - Known limits: the rate limit keys on the remote IP, so behind a reverse proxy without forwarded headers all clients share one bucket. The trash restore and permanent-delete lookups use an explicit query, because `FindAsync` still applies the "not deleted" filter on the real database; the in-memory test provider does not, so tests alone did not catch it.
  - **1.9 File sharing — In Progress** (file links only; 6 new unit tests plus a live smoke test)
    - [x] **1.9.1 Share links for files — Completed.** `POST /api/Share` (fileId, optional expiry), `GET /api/Share?fileId=`, `DELETE /api/Share/{id}` (revoke), all owner-only. Uses the existing `SharedLink` table, so no migration. The token is 32 random bytes (hex).
    - [x] **1.9.2 Public download — Completed.** `GET /api/Public/{token}/info` returns file name, type, and size; `GET /api/Public/{token}` needs no login and always sends `Content-Disposition: attachment`. Revoked, expired, unknown, or trashed-file links return 404.
    - [ ] **1.9.3 Folder sharing, link passwords, public listing page — Not Started.** No ADR was written for the anonymous-access model; recommended before extending it. The public endpoint has no rate limit (the token is unguessable) and no download count.

  - **1.11 Large files — In Progress** (96 domain tests; live checks against Postgres and SeaweedFS)
    - [x] **1.11.1 Configurable upload limit — Completed.** `Storage:MaxUploadBytes` (default 100 MB) sets both the Kestrel body limit (plus 1 MB for multipart overhead) and the multipart form limit. The fixed `[RequestSizeLimit]` is removed. An oversized upload returns 413 `File.TooLarge`. Checked live: with a 1 MB limit, a 0.5 MB file passes and 1.5 MB and 3 MB return 413. With a 200 MB limit, a 150 MB file uploads and downloads with the right size. The Kestrel limit now applies to every endpoint, not only upload.
    - [x] **1.11.2 Parallel uploads and quota — Completed for one API instance.** See 1.10 item 3.
    - [x] **1.11.3 Resumable (chunked) uploads — Completed for retry within one session.** `POST /api/Uploads` starts a session (checks name, type, folder, and quota), `PUT /api/Uploads/{id}/parts/{n}` stores one 8 MiB part, `GET /api/Uploads/{id}` lists stored parts, `POST /api/Uploads/{id}/complete` joins them into a file, `DELETE /api/Uploads/{id}` aborts. It uses S3 multipart and a new `UploadSession` table (migration `UploadSession`). Part sizes are checked exactly, and the complete step checks all parts and the quota again under the per-user lock. Five unit tests; checked live with a 30 MB file (resume after parts 1 and 2, wrong-size and out-of-range parts rejected, other user gets 404, hash identical after download). No ADR was written for the design.
    - [ ] **1.11.4 Chunked upload limits — Not Started.** Abandoned sessions and their stored parts are never cleaned up (a `shortcut:` comment in `UploadService`). A page reload loses the session, so the file restarts. File size is limited only by quota and S3's 10,000 parts (about 80 GB). `MaxUploadBytes` does not limit chunked files; the limit on each request is its part size.
  - **1.10 Code review findings (2026-10-31 whole-project review) — Mostly done** (91 domain and 3 auth tests pass)
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

## Dependencies
- 1.2 depends on 1.1.
- 1.7.2 depends on 1.7.1. 1.7.3 and 1.7.4 depend on 1.7.2. 1.7.5 depends on the decision in 1.7.5 and on item 1.4 failure handling.
- 1.3 depends on 1.2.
- 1.4 depends on 1.3.
- 1.5 depends on the API and infrastructure tasks it verifies.
- 1.6.2 item 7 depends on 1.6.1 item 3. 1.6.2 item 10 supersedes item 14's removal of the bloom filters.
- Frontend integration depends on stable API contracts and authorization behavior.

## Success Criteria
- Backend builds and available automated tests pass.
- Authenticated users can upload, retrieve, and delete only their own files.
- Metadata and stored objects remain consistent through successful operations and handled failures.
- Upload constraints and production authentication secrets are explicitly configured.
- The documented Docker Compose setup starts the stack and supports the basic API workflow.

## Overall Status
**In Progress.** File endpoint authentication and owner checks are implemented. The JWT secret check is implemented, but the audit found a tracked dev key, so that item is reopened. Upload size limits, storage/metadata failure consistency, integration/security tests, and Compose runtime verification remain incomplete. The 2026-10-30 ponytail audit added section 1.6 with 19 findings, all Not Started. Status reflects the repository review dated 2026-10-30; update it as implementation and verification progress.
