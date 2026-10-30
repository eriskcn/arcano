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
  - **1.6 Audit remediation — Not Started**
    - Source: [2026-10-30 ponytail audit](reports/2026-10-30-2323-ponytail-audit.md). Item numbers below match the report.
    - **1.6.1 Must fix — Not Started**
      - [ ] Item 1: untrack `appsettings.Development.json` (`git rm --cached`), move `Jwt:Secret` to user secrets or env, rotate the key. Open: HEAD no longer holds the secret, but the working copy has an uncommitted 64-character `Secret` in this tracked file. Needs an owner decision before committing; the old key stays in history until rotated.
      - [x] Item 2: `seaweedfs/security.toml` is in the root `.gitignore` (line 13), added in commit 919cac3.
      - [x] Item 3: `FileService.DeleteFileAsync` saves `IsDeleted` before calling `DeleteAsync`.
      - [ ] Item 4: replace `[DisableRequestSizeLimit]` with `[RequestSizeLimit(n)]`; the value of `n` needs a product decision.
      - [x] Item 5: fixed error messages in `FileService` (upload, download, delete) and in `ExceptionHandlingMiddleware`.
    - **1.6.2 Should fix — Not Started**
      - [ ] Item 6: delete the stored object when an upload's database save fails.
      - [ ] Item 7: rewrite the delete-failure test so the save path runs (after item 3).
      - [ ] Item 8: add sign-in and sign-up tests.
      - [ ] Item 9: decide on account-enumeration handling for register and login.
      - [ ] Item 10: remove the in-memory bloom filters (covers the multi-instance bug); see item 14.
      - [ ] Item 11: remove `GetUrlAsync` and `FileResponseDto.Url` from the upload path.
      - [ ] Item 12: add paging to the file list.
      - [ ] Item 13: drop `--host 0.0.0.0` from the dev frontend start in `run-dev.sh`. Frontend-side item; tracked here for the audit record.
      - [ ] Item 14: delete the bloom filter classes, DI registrations, and their tests (about 250 lines).
      - [ ] Item 15: move the duplicated Download/Delete lookup into one helper.
      - [ ] Item 16: parse the owner ID once.
    - **1.6.3 Nice to have — Not Started**
      - [ ] Item 17: create the storage bucket once at startup, not on every upload.
      - [ ] Item 18: update the backend README line about the SeaweedFS identity file.
      - [ ] Item 19: check the JWT secret once at startup.

## Dependencies
- 1.2 depends on 1.1.
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
