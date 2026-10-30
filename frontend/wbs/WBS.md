# Work Breakdown Structure (WBS) - Arcano Personal Cloud Client

## Objective
Build the Angular web client for Arcano personal cloud storage. Backend implementation and readiness tasks are tracked in [backend/wbs/WBS.md](../../backend/wbs/WBS.md). The feature list below records planned scope; it does not imply that each feature is required for the first usable release.

## Deliverables
- Desktop-oriented file-management client with authentication and core file workflows.
- User and administrative storage-management capabilities.
- Sharing and common media/document preview workflows.

## Task Hierarchy
### 1. Foundation and Layout
- [x] **1.1 Desktop-style UI shell — Complete**
  - Sidebar, breadcrumbs, toolbar, file grid/list, and details panel.
  - Responsive sidebar and navigation; file context menus support details and favorite actions.
  - Quota usage is not displayed because no backend quota contract is available.
- [ ] **1.2 Selection management — Not Started**
  - Single, Ctrl-toggle, Shift-range, and Escape-to-clear selection.
  - Marquee selection.

### 2. Authentication and User Access
- [ ] **2.1 Authentication and session — Not Started**
  - Login/register UI, token/session handling, and protected routes.
  - Align role-based UI with backend authorization; UI permissions do not replace API enforcement.
- [ ] **2.2 User profile and quota — Not Started**
  - Display user storage usage and warn/prevent uploads according to backend quota contracts.

### 3. Admin Storage and User Management (Planned)
- [ ] **3.1 System storage overview — Not Started**
- [ ] **3.2 User quota management — Not Started**
- [ ] **3.3 User lifecycle and role controls — Not Started**

### 4. File Explorer Workflows
- [ ] **4.1 Core file listing and actions — Not Started**
  - List the current user's files; support download and delete using secured backend APIs.
  - Add folder navigation, create/rename/move, and trash only when matching backend contracts exist.
- [ ] **4.2 Keyboard actions — Not Started**
  - Shortcuts for selection, rename, copy/move, undo, and delete, limited to supported operations.
- [ ] **4.3 Drag and drop — Not Started**
  - Upload external files and move internal items when the corresponding API operations are available.

### 5. File Sharing (Planned)
- [ ] **5.1 Share-link management — Not Started**
  - Generate and manage links with supported access, password, and expiration options.
- [ ] **5.2 Public share view — Not Started**
  - Recipient view depends on backend public-share and access-control contracts.

### 6. Media and Document Previews (Planned)
- [ ] **6.1 Preview host — Not Started**
- [ ] **6.2 Image viewer — Not Started**
- [ ] **6.3 Audio/video player — Not Started**
- [ ] **6.4 PDF viewer — Not Started**
- [ ] **6.5 Text/code viewer — Not Started**

## Dependencies
- The frontend API client and protected routes depend on stable backend routes, request/response contracts, and authentication behavior.
- Core file workflows depend on backend authorization and ownership enforcement.
- Folders, quota, roles, sharing, trash, moves, and previews depend on corresponding backend contracts and permissions.
- Public share views depend on backend share-link lifecycle and public-access contracts.

## Success Criteria
1. Signed-in users can list and manage only their own files through the secured API.
2. Authentication and role-dependent UI match backend authorization; security is enforced server-side.
3. Keyboard selection and actions behave consistently with the supported file operations.
4. Drag-and-drop uploads and internal moves work against verified backend endpoints.
5. Sharing and previews work for formats and access policies supported by the backend.
6. Responsive layout remains usable on desktop and smaller screens.

## Status
- Angular scaffold and desktop UI shell are present; backend-connected workflows have not been verified as complete.
- **Frontend workflow status: In Progress.** The desktop-style UI shell (1.1) is implemented; remaining workflows are not started.
- **End-to-end product status: Not Complete.** Backend readiness and verification are tracked separately in the backend WBS.

## Scope and Prioritization
The listed admin dashboard, advanced keyboard operations, password-protected sharing, and broad media support are planned backlog, not first-release blockers unless explicitly prioritized. No scope reduction or new product requirements are approved by this WBS review.

## Review Record
Reviewed with the backend WBS on 2026-10-30. This is a documentation alignment pass; implementation and verification were not performed. Keep statuses evidence-based and update them after code changes and relevant checks.
