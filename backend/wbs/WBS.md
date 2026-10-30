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
  - **1.7 Folders (Planned) — Not Started**
    - Scope: nested folders per user, with files placed in folders. The frontend side is tracked in [frontend/wbs/WBS.md](../../frontend/wbs/WBS.md) section 4.4.
    - [x] **1.7.1 Data model — Completed.** `Folder` entity (owner, parent, name), EF configuration with unique `(OwnerId, ParentId, Name)` index, and `FileMetadata.FolderId` are in the initial migration.
    - [ ] **1.7.2 Folder API — Not Started.** Create, list children, and rename a folder for the signed-in user. Reject a name that already exists under the same parent.
    - [ ] **1.7.3 Folder placement — Not Started.** Upload with an optional `folderId`; list files by folder (root when omitted); move a file to another folder.
    - [ ] **1.7.4 Folder ownership and safety — Not Started.** Every folder operation checks that the folder and any target parent belong to the caller. Moving a folder into its own descendant is rejected.
    - [ ] **1.7.5 Folder delete — Not Started (decision needed).** Choose whether deleting a non-empty folder is rejected or cascades to its files and subfolders. Storage objects must be removed for cascaded files, so the choice affects the failure handling in item 1.4.
    - [ ] **1.7.6 Folder tests — Not Started.** Tests for duplicate names, cross-owner access, cycle rejection, and paging within a folder.

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
