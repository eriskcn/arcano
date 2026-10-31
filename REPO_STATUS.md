# Arcano Repository Status Report

**Assessment date:** 2026-10-31
**Repository state:** `main` at `d795494` plus uncommitted keyboard shortcuts and file copy.

## Overview

Arcano is a monorepo for a personal cloud-storage application: an ASP.NET Core (.NET 10) backend with PostgreSQL and SeaweedFS (S3), and an Angular 22 frontend.

## Current stage

**Feature-rich local-development build, not yet verified in a browser.** The backend API and the frontend are built and unit-tested for:

- Sign-up, sign-in, protected routes.
- Files: upload, list, download, rename, star, move, trash, restore, delete forever, and search.
- Folders: create, rename, move, tree and breadcrumb navigation, drag and drop.
- Storage quota and usage display.
- Share links for files (create, expiry, revoke) and a public share page at `/share/:token`.
- Previews for images, audio, video, and plain text (PDF is deferred).
- Large files: configurable upload limit (`Storage:MaxUploadBytes`, 413 on oversize), upload progress, and resumable chunked uploads (8 MiB parts over S3 multipart, retry within a session). Abandoned sessions older than 24 hours are swept when their owner starts another upload.

Also built: keyboard shortcuts (Ctrl+A/C/X/V/Z, Delete, Shift+Delete, F2) and file copy (`POST api/Files/{id}/copy`).

Planned, not built: admin features, folder sharing, share-link passwords, PDF preview, password reset, email verification, refresh tokens.

## What is verified

- **Backend:** builds with no errors; 100 domain tests and 3 auth tests pass.
- **Frontend:** 103 unit tests pass; the production build succeeds.
- **Live API checks (local stack):** sign-up/sign-in, upload, download, cross-user 404, quota, 413 on oversize, share links, and a 30 MB chunked upload (resume, bad-part rejection, matching SHA-256, abort).

## What is not verified

- The web app has not been checked in a browser. This includes sharing, previews, drag and drop, upload progress, and chunked upload.
- The abandoned-session sweep has only a unit test, not a run against SeaweedFS.
- The backend has no integration tests for the database and storage paths.

## Gaps and risks

1. **A dev JWT signing key is still in git history.** `appsettings.Development.json` is still tracked. Rotate the key if used anywhere real, and decide whether to untrack the file.
2. **Compose credentials are fixed** for local development. Replace them before any deployment.
3. **The upload lock and quota race fix work on a single API instance only.** The lock is in process.
4. **Chunked upload limits:** no resume after a page reload; abandoned uploads of owners who never return are not swept; `MaxUploadBytes` does not cap chunked files (bounded by quota and about 80 GB).
5. **Review item 1 is partly fixed:** a failed save in trash purge can leave an entity tracked. Item 10 (`CurrentOwnerId` copies) remains.
6. **Public share endpoint has no rate limit.** The token is 32 random bytes, so guessing is impractical, but abuse is not throttled.
7. **413 message** also shows for 413s from a proxy.
8. **No ADRs** exist for the anonymous sharing model, the resumable upload design, or trash retention.

## Recommended next steps

1. Check the web app in a browser end to end (sign in, upload including a file over 8 MiB, folders, drag and drop, share, preview, trash).
2. Decide on the JWT key: rotate it and untrack the dev settings file.
3. Add integration tests for upload, chunked upload, and delete paths against the Compose stack.
4. Write ADRs for sharing and resumable uploads before extending them (folder sharing, passwords).
5. Add a rate limit on the public share endpoints.

## Verification scope

Reflects work through 2026-10-31: backend and frontend builds and tests run on `d795494`, plus the live API checks listed above. Nothing was tested in a browser.
