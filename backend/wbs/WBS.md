# Work Breakdown Structure (WBS) - Arcano Core

## Project Objectives
Build codebase for personal cloud storage application including Backend API (ASP.NET Core using Clean Architecture) and Frontend Dashboard for file management.

## Key Deliverables
1. **API Backend**: Full layers following Clean Architecture, integrating SeaweedFS storage via S3 API, managing file metadata via database, and handling JWT security.
2. **Frontend Dashboard**: Web application displaying file list, managing folders, supporting drag-and-drop upload, and sharing files.

## Work Breakdown Structure (Task Hierarchy)
- **1. Backend API Development (ASP.NET Core - Clean Architecture)**
  - [ ] 1.1 **Domain Layer**
    - [ ] 1.1.1 Define `FileMetadata` entity to store file metadata.
    - [ ] 1.1.2 Set up foundation entities: `BaseEntity`, `IAuditableEntity`.
    - [ ] 1.1.3 Implement `Result` structure to handle return values.
  - [ ] 1.2 **Application Layer**
    - [ ] 1.2.1 Create interfaces: `IStorageService`, `IApplicationDbContext`, `IUnitOfWork`.
    - [ ] 1.2.2 Design DTOs: `FileUploadDto`, `FileResponseDto`.
    - [ ] 1.2.3 Build storage coordination logic in `FileService`.
  - [ ] 1.3 **Infrastructure Layer**
    - [ ] 1.3.1 Implement `SeaweedStorageService` calling SeaweedFS via AWS SDK S3.
    - [ ] 1.3.2 Set up EF Core `ApplicationDbContext` for database connection.
    - [ ] 1.3.3 Implement Unit of Work & Repository patterns.
    - [ ] 1.3.4 Build stub to retrieve current user info `CurrentUser`.
  - [ ] 1.4 **API Layer**
    - [ ] 1.4.1 Write `FilesController` providing file upload, download, and delete APIs.
    - [ ] 1.4.2 Set up `ExceptionHandlingMiddleware` for centralized error handling.
    - [ ] 1.4.3 Configure JWT Authentication, DI container setup in `Program.cs`.
- **2. Frontend Client Dashboard Development**
  - [ ] 2.1 **Initialization and App Scaffolding**
    - [ ] 2.1.1 Initialize project (React/Vue/Blazor) and configure Routing.
    - [ ] 2.1.2 Build HTTP Client for Backend API communication.
  - [ ] 2.2 **Authentication**
    - [ ] 2.2.1 Build Login and Register user interfaces.
    - [ ] 2.2.2 Store JWT Token and protect private routes.
  - [ ] 2.3 **File Explorer Interface**
    - [ ] 2.3.1 Display file and folder list in grid or list view.
    - [ ] 2.3.2 Build functionality for creating folders, deleting files/folders, renaming.
    - [ ] 2.3.3 Preview common file formats (images, text, pdf).
  - [ ] 2.4 **File Upload & Download**
    - [ ] 2.4.1 Support drag-and-drop file upload.
    - [ ] 2.4.2 Display progress bar during upload.
    - [ ] 2.4.3 Implement file downloading.
  - [ ] 2.5 **File Sharing**
    - [ ] 2.5.1 Generate public file sharing links.
    - [ ] 2.5.2 Configure expiration limits for shared links.

## Dependencies
- **1.2 (Application Layer)** depends on **1.1 (Domain Layer)**.
- **1.3 (Infrastructure Layer)** depends on **1.2 (Application Layer)**.
- **1.4 (API Layer)** depends on **1.3 (Infrastructure Layer)**.
- **2. (Frontend Client)** operates based on API specifications from **1.4 (API Layer)**.

## Success Criteria
- Backend API builds successfully without errors, passing all unit tests.
- File upload to SeaweedFS succeeds via API, metadata saved to database correctly.
- Web interface correctly displays folders/files of currently logged-in user.
- Users require no complex environment setup other than Docker Compose to run entire codebase.

## Task Status
- **1. Backend API Development**: In Progress (Scaffolding and core files initialized)
- **2. Frontend Client Dashboard Development**: Not Started
