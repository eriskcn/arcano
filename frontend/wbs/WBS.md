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
- [x] **1.2 Selection management — Complete**
  - Single, Ctrl-toggle, Shift-range, and Escape-to-clear selection.
  - Pointer-drag marquee selection in list and grid views.

### 2. Authentication and User Access
- [x] **2.1 Authentication and session — Complete**
  - Login/register UI, token/session handling, and protected routes.
  - Login and registration use the verified backend contracts; protected and anonymous routes are guarded.
  - No role-specific UI is exposed because the current token has no role claims; API authorization remains authoritative.
- [ ] **2.2 User profile and quota — Blocked on backend quota contract**
  - The signed-in email is displayed from the authenticated token. Usage display and upload quota enforcement require a backend endpoint and defined quota contract, which are not currently available.

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
- [x] **4.3 Drag and drop — Completed (unit-tested, not verified in a browser)**
  - No backend work was needed: upload with `folderId`, `PUT /api/Files/{id}/folder`, and `PUT /api/Folders/{id}` already exist.
  - [x] **4.3.1 Drop files to upload — Completed.** Dropping OS files on the list or grid area uploads them into the current folder, using the same sequential path as the Upload button. A drop on a folder row uploads into that folder.
  - [x] **4.3.2 Drag a file onto a folder — Completed.** Moves the file with `moveFile`. Dragging a selected file moves the whole selection.
  - [x] **4.3.3 Drop onto the breadcrumb or root — Completed.** Breadcrumb items are drop targets, with root as `folderId` null. Items already in that folder are skipped. Tests cover root, folder, and same-folder drops.
  - [x] **4.3.4 Drag a folder onto a folder — Completed.** Moves the folder with `FoldersService.update`. A drop on itself is rejected. Backend errors such as `Folder.Cycle` and `Folder.NameTaken` show in the existing action error.
  - [x] **4.3.5 Drop feedback — Completed.** Valid folder and breadcrumb targets get a blue ring while a drag is over them. Dropping on a file does nothing.
  - [x] **4.3.6 Drag tests — Completed.** Eleven tests cover the upload drop (background and folder row), file-to-folder move, folder-to-folder move, self-drop, drop on a file, a backend cycle error, and the three breadcrumb drops.

- [~] **4.4 Folder navigation — Built, not verified in a browser (backend 1.7 complete)**
  - Backend tasks are in [backend/wbs/WBS.md](../../backend/wbs/WBS.md) section 1.7.
  - Folder tree and breadcrumb navigation, create and rename folders, upload into the current folder, and move files between folders.
  - Wire the existing "Folders" filter chip to real folder data instead of the mock; the chip is not implemented against the API yet.

### 5. File Sharing (Planned)
- [ ] **5.1 Share-link management — Not Started**
  - Generate and manage links with supported access, password, and expiration options.
- [ ] **5.2 Public share view — Not Started**
  - Recipient view depends on backend public-share and access-control contracts.

### 6. Media and Document Previews (Planned)
- Current state: the preview panel shell exists (desktop, toggled from the toolbar) and shows the selected item's details. It does not render file content yet.
- Approach: fetch the file bytes through the authenticated download endpoint, then show them with a local object URL. No backend change is needed.
- Constraint: never render uploaded HTML or SVG inline. Uploads come from other users, so inline rendering would run their script in the app's origin. Only images, PDF, text, audio, and video are previewed; everything else shows the download action.
- [x] **6.1 Preview host — Completed (unit-tested, not verified in a browser).** A Preview button in the details panel opens `app-file-preview` in a popup (`app-modal`) for one selected file. The side panel shows details only. The preview has loading, error, and ready states, cancels stale requests, and revokes object URLs.
- [x] **6.2 Image viewer — Completed.** PNG, JPEG, GIF, WebP, and BMP render from an object URL.
- [x] **6.3 Audio/video player — Completed.** Native `audio` and `video` elements. Files over 25 MB show "Preview unavailable" with the download action.
- [ ] **6.4 PDF viewer — Deferred (not supported now; revisit later).** PDFs show the download action only. Revisit the library or browser-native approach when this is prioritized.
- [x] **6.5 Text/code viewer — Completed for plain text.** Text, Markdown, CSV, and JSON show the first 100 KB with a truncation note. Code-specific highlighting is not included.
- [x] **6.6 Preview tests — Completed.** Covers the type rules, blocked HTML and SVG, the size cap, PDF (no request), request cancellation, the error state, and object URL revocation.

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
- **Frontend workflow status: In Progress.** The desktop shell (1.1), selection management (1.2), and authentication/session (2.1) are implemented. User quota work (2.2) is blocked on a backend contract; remaining workflows are not started.
- **End-to-end product status: Not Complete.** Backend readiness and verification are tracked separately in the backend WBS.

## Scope and Prioritization
The listed admin dashboard, advanced keyboard operations, password-protected sharing, and broad media support are planned backlog, not first-release blockers unless explicitly prioritized. No scope reduction or new product requirements are approved by this WBS review.

## Review Record
Reviewed with the backend WBS on 2026-10-30. This is a documentation alignment pass; implementation and verification were not performed. Keep statuses evidence-based and update them after code changes and relevant checks.
