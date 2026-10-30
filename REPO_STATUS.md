# Arcano Repository Status Report

**Assessment date:** 2026-10-30
**Repository state:** `main` at `7c4fe47` ("feat: implement pagination for file listing and enforce upload size limit"). Earlier this day: audit remediation in `c4ae916`. Two WBS files are staged but not committed; `REPO_STATUS.md` is untracked.

## Overview

Arcano is a monorepo for a personal cloud-storage application. It contains an ASP.NET Core backend and an Angular frontend. The backend API for files and authentication works end to end. The frontend has a working file explorer that uses the API, but several planned UI features are not started.

## Current stage

**Working file-storage MVP for local development.** Sign-up, sign-in, and the file API (upload, list, download, delete) work against the real PostgreSQL and SeaweedFS stack. The frontend lists, uploads, downloads, and deletes files through the API. Folders, sharing, previews, and admin features are planned, not built.

## What is verified

- **Backend build and tests:** the solution builds with no errors. Tests pass: 49 domain tests and 2 auth tests.
- **End-to-end upload (local, port 5001):** sign-up, sign-in, upload, list, download (bytes match), delete, cross-user access (404), and no-token access (401) all behaved as expected. The database row and stored object were checked.
- **Frontend:** 29 unit tests pass, and the production build succeeds.

## What is not verified

- The web app has not been checked in a browser. The paging fix is covered only by unit tests.
- The 100 MB upload limit has not been tested with a real oversized request.
- The backend has no integration tests for the database and storage paths.

## Gaps and risks to resolve

1. **A dev JWT signing key is still in git history.** The key was in `backend/src/ArcanoCore.API/appsettings.Development.json` from the first commit. The working copy no longer has it, but the file is still tracked. Rotate the key if it is used in any real environment, and decide whether to untrack the file.
2. **Compose credentials are fixed.** The database and storage credentials in `backend/docker-compose.yml` are for local development only. Replace them before any deployment.
3. **The frontend WBS is out of date.** Section 4.1 (core file listing and actions) still says "Not Started", but the explorer already lists, uploads, downloads, and deletes through the API. Update it to match.
4. **Folders are planned, with one open decision.** The backend has the folder data model but no folder API. Deleting a non-empty folder still needs a decision: reject it, or cascade to its files (backend WBS task 1.7.5).
5. **Upload failure handling is partly done.** Failed uploads clean up stored objects, and failed deletes save the database change first. Failure handling is not yet verified with integration tests.

## Recommended next steps

1. Decide on the JWT key: rotate it and untrack the dev settings file.
2. Test the web app in a browser: sign in, upload, page through more than 50 files, delete.
3. Add integration tests for upload, delete, and failure paths against the Compose stack.
4. Update the frontend WBS section 4.1 to match the working explorer.
5. Decide the folder-delete behavior (backend WBS 1.7.5), then build folders.
6. Replace fixed Compose credentials with a development-safe configuration pattern before deployment.

## Verification scope

This update reflects the work done on 2026-10-30: backend and frontend builds and tests, the end-to-end upload check against the local stack, and the code review of commit `c4ae916`. The browser UI and the 100 MB limit were not tested.
