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
    - [x] Require a configured JWT secret of at least 32 UTF-8 bytes; remove committed JWT secret values and fallbacks.
    - [ ] Apply explicit upload-size and validation limits; `[DisableRequestSizeLimit]` remains on the upload endpoint and no approved maximum is defined.
    - [ ] Define and verify failure handling for database/object-storage operations.
  - **1.5 Verification and readiness — Not Started**
    - [ ] Build and run available tests; record actual results.
    - [ ] Verify the Docker Compose stack and documented local quickstart; Compose syntax validates, but runtime startup has not been exercised.

## Dependencies
- 1.2 depends on 1.1.
- 1.3 depends on 1.2.
- 1.4 depends on 1.3.
- 1.5 depends on the API and infrastructure tasks it verifies.
- Frontend integration depends on stable API contracts and authorization behavior.

## Success Criteria
- Backend builds and available automated tests pass.
- Authenticated users can upload, retrieve, and delete only their own files.
- Metadata and stored objects remain consistent through successful operations and handled failures.
- Upload constraints and production authentication secrets are explicitly configured.
- The documented Docker Compose setup starts the stack and supports the basic API workflow.

## Overall Status
**In Progress.** File endpoint authentication and owner checks plus JWT secret validation are implemented. Upload size limits, storage/metadata failure consistency, integration/security tests, and Compose runtime verification remain incomplete. Status reflects the repository review dated 2026-10-30; update it as implementation and verification progress.
