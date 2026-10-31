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
- [x] **2.2 User profile and quota — Completed (unit-tested, not verified in a browser)**
  - The signed-in email is displayed from the authenticated token. The sidebar storage box shows used and limit from `GET /api/Account/storage` and refreshes after upload, delete, restore, permanent delete, and empty trash. A quota error on upload shows "Not enough storage space."

### 3. Admin Storage and User Management (Planned)
- [ ] **3.1 System storage overview — Not Started**
- [ ] **3.2 User quota management — Not Started**
- [ ] **3.3 User lifecycle and role controls — Not Started**

### 4. File Explorer Workflows
- [x] **4.1 Core file listing and actions — Completed (unit-tested, not verified in a browser)**
  - Lists the current user's files; download, rename, favorite, and delete (to the trash) use the secured backend APIs.
  - Favorites, Recent, and Home load the matching server views. The search box searches by name on the server across all folders.
  - The Trash view lists trashed files with Restore and Delete forever, plus Empty trash. Folders are still starred locally only, because the backend has no folder favorite.
  - Folder navigation, create, rename, and move are tracked in 4.4.
- [x] **4.2 Keyboard actions — Completed (unit-tested, not verified in a browser)**
  - Ctrl+A selects all; Delete moves selected files to the trash; Shift+Delete deletes forever (Trash view only); Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste files into the current folder; Ctrl+Z undoes the last delete, move or paste (in memory, last 20); F2 renames. Ignored while typing in a field or with a dialog open.
  - Files only: folders are not copied, cut or deleted by keyboard. The context-menu "Move to" is not undoable. Ctrl+C and Ctrl+X take over the browser shortcut whenever files are selected. Undo is not cleared on sign-out.
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
- [x] **5.1 Share-link management — Completed for files (unit-tested, not verified in a browser).** A Share button in the details panel opens a dialog to create a link (never, 1, 7, or 30 days), copy it, and revoke it. Replaces the earlier mock dialog. Password and folder sharing are not included.
- [x] **5.2 Public share view — Completed (unit-tested, not verified in a browser).** Route `/share/:token` needs no login. It shows the file name and size, a Download button, and an inline preview (image, media, text only), or "This link is invalid or has expired." Download errors show no message yet.

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

### 7. Code Review Findings (2026-10-31 whole-project review)
- Source: `/code-review` of the whole project on 2026-10-31. Findings were not verified. Numbers match the review; backend items are in [backend/wbs/WBS.md](../../backend/wbs/WBS.md) section 1.10.
- [x] **7.1 Item 8: download error and revoke timing — Completed (unit-tested, not verified in a browser).** `share-page.component.ts` `download()` has no error handler, so a failed shared download shows nothing. It and `app.ts` `downloadFile` revoke the object URL right after `click()`, which some browsers treat as a cancel. **Fixed:** A failed shared download shows "Download failed. Please try again.", and the object URL is revoked after 1 s in both places.
- [x] **7.2 Item 9: share error messages — Completed (unit-tested, not verified in a browser).** `shareErrorMessage` in `app.ts` reads `detail`, `title` and `message`, but the backend sends `{code, description}`, so messages such as "Expiry must be in the future." never show. **Fixed:** `description` is read first.
- [x] **7.3 Item 10 (frontend part): duplicated error parsing — Completed (unit-tested, not verified in a browser).** `fileErrorMessage`, `folderErrorMessage` and `shareErrorMessage` repeat the same error-code extraction. This is a cleanup, not a bug. **Fixed:** One `errorBody()` helper in `app.ts`.

### 8. Large Files
- [x] **8.1 Upload progress — Completed (unit-tested, not verified in a browser).** `FilesService.upload` emits 0-100. A block above the file list shows the file name, "n of m", and a progress bar for both the Upload button and drag-and-drop. A 413 shows "File is too large." with no hard-coded size.
- [x] **8.2 Resumable uploads — Completed for retry within one session (unit-tested, not verified in a browser).** Files over 8 MiB upload in 8 MiB chunks through `uploadChunked`. A failed chunk waits 500 ms, asks the server which parts are stored, and retries up to 3 times; if it still fails, the session is aborted. Progress stays at 99% until the file is complete. A page reload does not resume.

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
- **Frontend workflow status: In Progress.** The desktop shell (1.1), selection management (1.2), and authentication/session (2.1) are implemented. User quota (2.2), core file actions (4.1), folders (4.4), drag and drop (4.3), and previews (6) are built and unit-tested; none are verified in a browser. Admin, keyboard actions, and sharing are not started.
- **End-to-end product status: Not Complete.** Backend readiness and verification are tracked separately in the backend WBS.

## Scope and Prioritization
The listed admin dashboard, advanced keyboard operations, password-protected sharing, and broad media support are planned backlog, not first-release blockers unless explicitly prioritized. No scope reduction or new product requirements are approved by this WBS review.

## Review Record
Reviewed with the backend WBS on 2026-10-30. This is a documentation alignment pass; implementation and verification were not performed. Keep statuses evidence-based and update them after code changes and relevant checks.
