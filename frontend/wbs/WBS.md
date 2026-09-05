# Work Breakdown Structure (WBS) - Arcano Personal Cloud Client

## Objective
Build web client for personal cloud storage. Desktop-grade UX: keyboard shortcuts, multi-select, drag-and-drop, link sharing, role-based access control (Admin/User), storage quota management, and multi-format media previewers.

---

## Task Hierarchy

### 1. Foundation & Layout Shell
- [ ] **1.1 Desktop-Style UI Shell** `Status: Not Started`
  - Split view: sidebar navigation, path breadcrumbs, action toolbar, main grid/list view, inspector/details panel.
  - User quota display widget on sidebar (used / total limit progress bar).
  - Responsive layout, context menus, right-click override on file grid.
- [ ] **1.2 Selection Management System** `Status: Not Started`
  - Single click select.
  - `Ctrl + Click` toggle individual items.
  - `Shift + Click` range selection.
  - Marquee (rubber band / box) drag-select.
  - `Esc` clear selection.

### 2. Authentication & Role-Based Access Control (RBAC)
- [ ] **2.1 Role & Session Management** `Status: Not Started`
  - Roles: `Admin`, `User`.
  - Route guards & UI permission directives based on active role.
- [ ] **2.2 User Profile & Quota Meter** `Status: Not Started`
  - Current user view: storage usage breakdown (by file type: images, video, docs, other).
  - Upload prevention / warning alerts when approaching/exceeding quota.

### 3. Admin Storage & User Management Dashboard
- [ ] **3.1 System-Wide Storage Overview** `Status: Not Started`
  - Total system disk usage, aggregate user usage graphs, top storage consumers list.
- [ ] **3.2 User Quota Management** `Status: Not Started`
  - User listing table with columns: Name, Email, Role, Used Storage, Quota Limit, Status.
  - Set default global quota (default: 5 GB).
  - Override custom quota limit per individual user (GB/TB scale or unlimited).
- [ ] **3.3 User Lifecycle Controls** `Status: Not Started`
  - Promote/demote role (`Admin` <-> `User`), suspend user, force storage purge/cleanup.

### 4. File Explorer & Keyboard Engine
- [ ] **4.1 Shortcut & Action Dispatcher Engine** `Status: Not Started`
  - Global listener scoping (disable inside input fields/dialogs).
  - `Ctrl + C`: Copy items to virtual clipboard.
  - `Ctrl + X`: Cut items (visual dim state).
  - `Ctrl + V`: Paste items (execute copy/move API).
  - `Ctrl + Z`: Undo last file operation (move/rename/delete).
  - `F2`: Inline rename active item.
  - `Del`: Move item(s) to Trash.
  - `Shift + Del`: Permanent delete prompt & execution.
- [ ] **4.2 Drag and Drop (DnD) Engine** `Status: Not Started`
  - OS-to-Browser DnD: upload files/folders via drop onto current folder/subfolders.
  - Internal DnD: drag selected items into folders or breadcrumb nodes (move/copy).
  - Visual drop target cues and drag ghost image.

### 5. Sharing Engine (OneDrive / Google Drive Style)
- [ ] **5.1 Share Link Generation & Management** `Status: Not Started`
  - Public / password-protected / expiration-date links.
  - Permission scopes: View Only vs Edit/Upload.
  - Quick copy link to clipboard with toast notification.
- [ ] **5.2 Public Share View Portal** `Status: Not Started`
  - Lightweight recipient view for shared files/folders without full login requirement.
  - Direct download and preview.

### 6. Media & Document Previewers
- [ ] **6.1 Preview Modal Host** `Status: Not Started`
  - Modal overlay with next/previous item navigation, zoom, rotate, fullscreen, and download.
- [ ] **6.2 Image Viewer** `Status: Not Started`
  - Formats: PNG, JPG, WEBP, SVG, GIF, RAW thumbnails.
  - Zoom, pan, slideshow mode.
- [ ] **6.3 Video & Audio Player** `Status: Not Started`
  - Formats: MP4, WebM, MP3, WAV, FLAC.
  - Custom stream player controls, playback speed, subtitle support.
- [ ] **6.4 PDF Viewer** `Status: Not Started`
  - Native PDF rendering (pdf.js or canvas-based viewer), page navigation, text search.
- [ ] **6.5 Text & Code Viewer/Editor** `Status: Not Started`
  - Plain text, Markdown (rendered preview), source code syntax highlighting (JSON, YAML, JS/TS, Python, etc.).

---

## Dependencies
- **1.2** depends on **1.1**
- **2.2** depends on **2.1**
- **3.1, 3.2, 3.3** depend on **2.1**
- **4.1, 4.2** depend on **1.2**
- **5.2** depends on **5.1**
- **6.2 - 6.5** depend on **6.1**

---

## Success Criteria
1. Role-based access control enforces `Admin` vs `User` views and API access.
2. Default storage quota (5GB) auto-assigned on user creation.
3. Admin dashboard accurately displays system usage and modifies per-user quotas.
4. Full keyboard workflow operable without mouse (`F2`, `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Del`, `Shift+Del`, `Ctrl+Z`, `Esc`).
5. Range (`Shift`) and toggle (`Ctrl`) multi-select match desktop OS file manager behavior.
6. Drag-and-drop handles internal file movement and external file uploads.
7. Share dialog creates parameterized links (expiry/password/view-only).
8. Previews load seamlessly for image, audio/video, PDF, and code/text formats.
