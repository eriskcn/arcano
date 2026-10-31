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
  - The Trash view lists trashed files with Restore and Delete forever, plus Empty trash. Folder star is now saved by the backend (`PUT api/Folders/{id}/star`, optimistic with revert on failure).
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

- [x] **4.5 Context menu — Completed (unit-tested; menu, Make a copy and Preview checked in a browser on 2026-10-31).** Right-click menu in the style of Google Drive and OneDrive: icons, shortcut hints, dividers; Open or Preview, Download, Share, Rename, Make a copy, Copy, Cut, Move to, favorite, Details, Move to trash. Right-click selects an unselected item and acts on the whole selection where it can. Trash view has Restore and Delete forever. Right-click on empty space offers New folder, Upload files and Paste. Closes on Escape, click, scroll and resize; arrow keys and Enter work.
  - Limits: Move to trash on a multi-selection skips folders; the Move to submenu is not clamped to the viewport; no background menu when the list is empty; a right-click elsewhere does not close the menu; right-click in Trash does not change the selection and acts on the clicked file only; the unused `moveFile()` in `app.ts` was removed in 9.8.

- [x] **4.4 Folder navigation — Completed (unit-tested; folder navigation checked in a browser on 2026-10-31, see 9.1 and 9.3; backend 1.7 complete)**
  - Backend tasks are in [backend/wbs/WBS.md](../../backend/wbs/WBS.md) section 1.7.
  - Folder tree and breadcrumb navigation, create and rename folders, upload into the current folder, and move files between folders.
  - The "Folders" filter chip (`filterChips` in `app.ts`) filters the loaded items by `type === 'folder'`, so it works on real folder data and applies to the current folder only.

### 5. File Sharing
- [x] **5.1 Share-link management — Completed for files (unit-tested, not verified in a browser).** A Share button in the details panel opens a dialog to create a link (never, 1, 7, or 30 days), copy it, and revoke it. Replaces the earlier mock dialog. Password and folder sharing are not included.
- [x] **5.2 Public share view — Completed (unit-tested, not verified in a browser).** Route `/share/:token` needs no login. It shows the file name and size, a Download button, and an inline preview (image, media, text only), or "This link is invalid or has expired." Download errors show no message yet.

### 6. Media and Document Previews
- Current state: the side preview panel (desktop, toggled from the toolbar) shows the selected item's details; file content renders in the preview popup (6.1).
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
- The desktop UI shell and the backend-connected workflows are implemented; browser verification is recorded in section 9.
- **Frontend workflow status: In Progress.** The desktop shell (1.1), selection management (1.2), and authentication/session (2.1) are implemented. User quota (2.2), core file actions (4.1), folders (4.4), drag and drop (4.3), and previews (6) are built and unit-tested; the main flows were checked in a browser (section 9). Only admin (section 3) is not started. Uploading from a phone works (9.11).
- **End-to-end product status: Not Complete.** Backend readiness and verification are tracked separately in the backend WBS.

## Scope and Prioritization
The listed admin dashboard, advanced keyboard operations, password-protected sharing, and broad media support are planned backlog, not first-release blockers unless explicitly prioritized. No scope reduction or new product requirements are approved by this WBS review.

## Review Record
Reviewed with the backend WBS on 2026-10-30. This is a documentation alignment pass; implementation and verification were not performed. Keep statuses evidence-based and update them after code changes and relevant checks.

### 9. Browser verification (2026-10-31)
- [x] **9.1 Main flows checked in a browser — Completed.** Sign-in, folder create, small and 20 MiB chunked upload (hash identical), Make a copy, right-click menu, image preview, share create/open anonymously/revoke, Delete and Ctrl+Z, Ctrl+X/Ctrl+V and Ctrl+Z.
- [x] **9.2 Escape closes dialogs — Fixed.** Found in the browser: Escape did not close the share or preview dialog and cleared the selection behind it. `ModalComponent` now closes on Escape and the page handler ignores Escape while a dialog is open. 2 new specs.
- [x] **9.3 Later browser pass — Completed.** Sign-up page, Trash view, drag and drop (real mouse drags were unreliable, but synthetic drag events worked), previews of text, image, audio and video (PDF shows download only), the upload progress bar, marquee selection and the mobile layout were checked. Not re-checked after the fixes in 9.4 and 9.10: the confirmation dialogs, double-click preview and the account settings dialog.
- [x] **9.4 Small issues seen — Fixed.** The clipped Share button, the floating "+" covering the "Shared" tab, audio files labelled "Document", double-click not opening the preview, the folder page heading, the sign-up error message and the missing delete confirmations were fixed afterwards (see 9.10). The "Copy of" file name is a backend change (backend WBS 1.13 finding 12). Not confirmed fixed: a 1-pixel image previews at 1 px.
- [x] **9.5 Chunked upload without a file type — Fixed (audit finding 3).** Browsers give an empty type for unknown extensions (`.iso`, `.log`), and the server rejected the upload start with 400. `uploadChunked` now sends `application/octet-stream` when the type is empty. 1 new spec; the server's 400 for an empty type was checked live.
- [x] **9.6 Token renewal during long uploads — Completed (audit finding 5; unit-tested, API checked live, not checked in a browser).** When the login token has under 10 minutes left, the HTTP interceptor renews it through `POST Auth/refresh` before sending the request; concurrent requests share one renewal, and a failed renewal falls through to the existing 401 handling. Chunked uploads benefit automatically. 4 new specs.
- [x] **9.7 `AppShell` split by job — Completed (audit finding 8; unit-tested, main flows re-checked in a browser).** `file-actions.service.ts` now holds the clipboard, the undo stack, the bulk, trash, paste and copy actions, and upload (progress, chunked switch, 413 message); `http-errors.ts` holds the shared error parsing; `context-menu.component.ts` holds the right-click menu (clamping, keyboard, move list); `share-dialog.component.ts` holds the share dialog. `app.ts` went from 1,352 to 1,137 lines and `app.html` from 1,006 to 932; 126 specs pass. No behaviour change intended. Still in `app.ts`: sign-in and sign-up pages, explorer loading, folder actions, `moveItems`, selection and marquee, drag and drop, and the shortcut handler; splitting those needs a shared explorer-state store.
- [x] **9.8 Dead `App.moveFile()` removed (audit finding 11).** Nothing in the template or specs called it; moves go through `moveItems` and the clipboard. 20 lines deleted; 126 specs and the build still pass.
- [x] **9.9 Production web image (release blocker; verified on a throwaway production stack).** `frontend/Dockerfile` builds the app with `environment.production.ts` (`apiBaseUrl: '/api'`, same origin) and serves it with nginx, which proxies `/api` and `/health` to the API; `auth.interceptor.ts` now compares against `location.origin`. Development still uses `http://localhost:5000/api`. A 90 MB upload through nginx worked. Sign-up shows the server's message when sign-up is disabled. 128 specs pass.
- [x] **9.10 Should-fix round (2026-10-31) — Completed (unit-tested).**
  - Account settings dialog (change password, sign out everywhere, delete account), opened from Settings in the sidebar.
  - Confirm dialog for Delete forever, Empty trash and folder delete.
  - Folder "Download as zip" in the context menu.
  - Audio files get the audio type and icon; double-click opens the preview; the folder page shows the folder name as its heading; the Share button in the details panel wraps instead of being clipped; the floating "+" sits above the bottom navigation; sign-up shows the server's error message.
  - The phone upload gap noted here was closed in 9.11.
- [x] **9.11 Phone upload (checked in a browser).** On phones (under 640 px) the floating button (now an upload icon, labelled "Upload files") opens the file picker through the same hidden input as the header Upload button; from 640 px up the header Upload button is used and the floating button is hidden. Checked at 375 and 768 px, including an upload through the input. 1 new spec; 143 specs pass.

- [x] **9.12 Shared view removed.** The "Shared" nav item and filter chip always showed nothing (the flag was never set by the API) and the empty text promised files shared with you, which the backend does not support. Both are removed; share links are created per file from the Share dialog. Trash now sits in the mobile bottom nav.
- [x] **9.13 Folder star persisted — Completed (unit-tested).** `FolderDto.isStarred` maps to `starred`; `FoldersService.star` is called by the same toggle as files (optimistic, reverts with an error on failure). The Favorites view still lists files only. 2 new specs.
