import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, HostListener, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router } from '@angular/router';
import { EMPTY, Observable, Subject, Subscription, catchError, concatMap, debounceTime, distinctUntilChanged, filter, forkJoin, from, of, tap } from 'rxjs';
import { environment } from '../environments/environment';
import { AccountService } from './account.service';
import { ContextMenuComponent, ContextMenuModel } from './context-menu.component';
import { FileActionsService } from './file-actions.service';
import { errorBody, fileErrorMessage, readBlobError } from './http-errors';
import { AuthService } from './auth.service';
import { FILES_PAGE_SIZE, FileDto, FilesService, FilesView } from './files.service';
import { FolderDto, FoldersService } from './folders.service';
import { ShareDialogComponent } from './share-dialog.component';
import { AccountDialogComponent } from './account-dialog.component';
import { AdminDialogComponent } from './admin-dialog.component';
import { FilePreviewComponent } from './preview/file-preview.component';
import { ModalComponent } from './shared/components/modal.component';
import { QuotaMeterComponent } from './shared/components/quota-meter.component';
import { ConfirmDialogComponent } from './shared/components/confirm-dialog.component';
import { SharedWithMeComponent } from './shared-with-me.component';
import { previewKind } from './preview/preview-kind';

export type Page = 'signin' | 'signup' | 'reset' | 'app';
export type ViewMode = 'list' | 'grid';
export type NavItem = 'home' | 'my-files' | 'recent' | 'favorites' | 'trash' | 'shared';

export interface MenuEntry {
  label: string;
  icon: string;
  run: () => void;
  shortcut?: string;
  disabled?: boolean;
  danger?: boolean;
  divider?: boolean; // separator before this entry
  submenu?: boolean; // opens the move-target list instead of running
}

export interface FileItem {
  id: string;
  name: string;
  type: 'folder' | 'document' | 'image' | 'video' | 'audio' | 'archive' | 'spreadsheet' | 'pdf';
  size: string | null;
  modified: string;
  starred: boolean;
  contentType?: string;
  fileSize?: number;
}

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB'];

const formatFileSize = (bytes: number): string => {
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < BYTE_UNITS.length - 1) {
    value /= 1024;
    unit++;
  }
  const text = unit === 0 || value >= 10 ? Math.round(value) : Number(value.toFixed(1));
  return `${text} ${BYTE_UNITS[unit]}`;
};

const fileTypeFromContentType = (contentType: string): FileItem['type'] => {
  const type = contentType.toLowerCase();
  if (type.startsWith('image/')) return 'image';
  if (type.startsWith('video/')) return 'video';
  if (type.startsWith('audio/')) return 'audio';
  if (type === 'application/pdf') return 'pdf';
  if (type.includes('spreadsheet') || type.includes('excel')) return 'spreadsheet';
  if (/zip|x-tar|x-rar|x-7z|compressed|archive/.test(type)) return 'archive';
  return 'document';
};

const formatDate = (isoDate: string): string =>
  new Date(isoDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

const toFileItem = (file: FileDto): FileItem => ({
  id: file.id,
  name: file.fileName,
  type: fileTypeFromContentType(file.contentType),
  size: formatFileSize(file.fileSize),
  modified: formatDate(file.lastModifiedAtUtc ?? file.createdAtUtc),
  starred: file.isStarred,
  contentType: file.contentType,
  fileSize: file.fileSize,
});

const toFolderItem = (folder: FolderDto): FileItem => ({
  id: folder.id,
  name: folder.name,
  type: 'folder',
  size: null,
  modified: formatDate(folder.createdAtUtc),
  starred: folder.isStarred,
});

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, ContextMenuComponent, FilePreviewComponent, ShareDialogComponent, AccountDialogComponent, AdminDialogComponent, ModalComponent, QuotaMeterComponent, ConfirmDialogComponent, SharedWithMeComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  providers: [FileActionsService],
})
export class App implements OnInit {
  readonly appName = environment.appName;
  private readonly auth = inject(AuthService);
  private readonly filesApi = inject(FilesService);
  private readonly foldersApi = inject(FoldersService);
  private readonly accountApi = inject(AccountService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly actions = inject(FileActionsService);
  readonly userEmail = this.auth.email;
  readonly userInitial = computed(() => this.userEmail()?.trim().charAt(0).toUpperCase() || '?');

  constructor() {
    this.actions.bind({
      files: () => this.files(),
      selectedIds: () => this.selectedIds(),
      currentFolderId: () => this.currentFolderId(),
      reload: () => this.loadFiles(),
      refreshStorage: () => this.refreshStorage(),
      move: (items, targetId, origin) => this.moveItems(items, targetId, origin),
    });
  }

  // Navigation & View State
  readonly page = signal<Page>('signin');
  readonly activeNav = signal<NavItem>('my-files');
  readonly sidebarCollapsed = signal<boolean>(false);
  readonly mobileSidebarOpen = signal<boolean>(false);
  readonly viewMode = signal<ViewMode>('list');
  readonly selectedIds = signal<ReadonlySet<string>>(new Set());
  readonly selectedId = signal<string | null>(null);
  readonly selectionAnchorId = signal<string | null>(null);
  readonly searchQuery = signal<string>('');
  readonly activeFilter = signal<string>('All');
  readonly previewOpen = signal<boolean>(true);
  /** Details overlay below xl; opened only by its toggle so tapping a file never covers the grid. */
  readonly detailsSheetOpen = signal<boolean>(false);
  readonly previewModalOpen = signal<boolean>(false);
  readonly contextMenu = signal<ContextMenuModel | null>(null);
  readonly uploadInput = viewChild<ElementRef<HTMLInputElement>>('uploadInput');
  readonly selectionRect = signal<{ left: number; top: number; width: number; height: number } | null>(null);
  private marqueeStart: { x: number; y: number } | null = null;
  private marqueeSurface: HTMLElement | null = null;
  private marqueeBaseSelection = new Set<string>();
  // In-memory clipboard for files (folders are never copied) and a capped undo stack.

  // Auth Form State
  readonly signInEmail = signal<string>('');
  readonly signInPassword = signal<string>('');
  readonly signInError = signal<string>('');
  readonly signUpError = signal<string>('');
  readonly signUpSuccess = signal<string>(this.auth.takeSignInNotice());
  readonly authBusy = signal<boolean>(false);
  readonly showSignInPassword = signal<boolean>(false);

  readonly signUpEmail = signal<string>('');
  readonly signUpPassword = signal<string>('');
  readonly showSignUpPassword = signal<boolean>(false);
  // Shown until the server says this is a fresh install (or the check fails: the server decides anyway).
  readonly requiresKey = signal<boolean | null>(null); // null until GET Auth/setup resolves
  readonly signUpKey = signal<string>('');

  readonly resetKey = signal<string>('');
  readonly resetPassword = signal<string>('');
  readonly resetConfirm = signal<string>('');
  readonly resetError = signal<string>('');

  readonly shareOpen = signal<boolean>(false);

  ngOnInit(): void {
    this.syncPageWithRoute();
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncPageWithRoute());
    // loadFiles cancels the previous request, so a stale search never overwrites a newer one.
    // Scroll events do not bubble, so listen in the capture phase.
    const closeMenu = () => this.contextMenu.set(null);
    document.addEventListener('scroll', closeMenu, true);
    this.destroyRef.onDestroy(() => document.removeEventListener('scroll', closeMenu, true));
    this.searchInput
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadFiles());
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);
    this.searchInput.next(value.trim());
  }

  // Static options
  readonly navItems: { key: NavItem; label: string; icon: string }[] = [
    { key: 'home', label: 'Home', icon: 'home' },
    { key: 'my-files', label: 'My Files', icon: 'folder' },
    { key: 'recent', label: 'Recent', icon: 'schedule' },
    { key: 'favorites', label: 'Favorites', icon: 'star' },
    { key: 'trash', label: 'Trash', icon: 'delete' },
    { key: 'shared', label: 'Shared with me', icon: 'group' },
  ];

  readonly filterChips = ['All', 'Folders', 'Documents', 'Images', 'Videos'];

  readonly navLabels: Record<NavItem, string> = {
    home: 'Home',
    'my-files': 'My Files',
    recent: 'Recent',
    favorites: 'Favorites',
    trash: 'Trash',
    shared: 'Shared with me',
  };

  readonly emptyMessages: Record<NavItem, { icon: string; title: string; sub: string }> = {
    home: { icon: 'home', title: 'Welcome home', sub: 'Your recent and starred files will appear here.' },
    'my-files': { icon: 'folder', title: 'No files yet', sub: 'Upload or create new files to get started.' },
    recent: { icon: 'schedule', title: 'No recent activity', sub: 'Files you open will appear here.' },
    favorites: { icon: 'star', title: 'No favorites yet', sub: 'Star files to find them quickly.' },
    trash: { icon: 'delete', title: 'Trash is empty', sub: 'Deleted files will appear here for 30 days.' },
    shared: { icon: 'group', title: 'Nothing shared', sub: 'Items others share with you will appear here.' },
  };

  readonly files = signal<FileItem[]>([]);
  readonly filesLoading = signal<boolean>(false);
  readonly filesError = signal<string>('');
  // The API returns no total, so more pages exist only while the last page came back full.
  readonly hasMoreFiles = signal<boolean>(false);
  private loadingMore = false;
  private moreSubscription?: Subscription;
  readonly actionError = this.actions.actionError;
  readonly uploadState = this.actions.uploadState;
  readonly cancelUpload = () => this.actions.cancelUpload();
  readonly actionBusy = this.actions.actionBusy;
  readonly clipboard = this.actions.clipboard;

  // Folder navigation: folders of the current folder, the path to it, and the create/rename editor.
  readonly folders = signal<FileItem[]>([]);
  readonly folderPath = signal<{ id: string; name: string }[]>([]);
  readonly folderEditor = signal<{ mode: 'create' } | { mode: 'rename'; folder: FileItem } | null>(null);
  readonly folderName = signal<string>('');
  readonly dropKey = signal<string | null>(null);
  private draggedItems: FileItem[] = [];
  private loadSubscription: Subscription | null = null;
  private readonly searchInput = new Subject<string>();
  readonly storage = signal<{ usedBytes: number; limitBytes: number } | null>(null);

  readonly currentFolderId = computed(() => this.folderPath().at(-1)?.id ?? null);
  readonly items = computed<FileItem[]>(() => [...this.folders(), ...this.files()]);
  readonly moveTargets = computed<{ id: string | null; name: string }[]>(() => [
    ...(this.currentFolderId() ? [{ id: null, name: 'My Files' }] : []),
    ...this.folders().map(folder => ({ id: folder.id, name: folder.name })),
  ]);

  readonly showFiles = computed(() => {
    const nav = this.activeNav();
    return nav === 'my-files' || nav === 'home' || nav === 'recent' || nav === 'favorites' || nav === 'trash';
  });

  readonly filteredFiles = computed(() => {
    const nav = this.activeNav();
    const filter = this.activeFilter();

    return this.items().filter(f => {
      if (nav === 'favorites') return f.starred;
      if (filter === 'Folders') return f.type === 'folder';
      if (filter === 'Documents') return f.type === 'document' || f.type === 'pdf' || f.type === 'spreadsheet';
      if (filter === 'Images') return f.type === 'image';
      if (filter === 'Videos') return f.type === 'video';
      return true;
    });
  });

  readonly selectedFile = computed(() => {
    const id = this.selectedId();
    return this.items().find(f => f.id === id) ?? null;
  });

  readonly breadcrumb = computed(() => {
    const nav = this.activeNav();
    const base = nav === 'my-files' ? [this.appName, 'My Files'] : [this.appName, this.navLabels[nav]];
    return [...base, ...this.folderPath().map(folder => folder.name)];
  });

  readonly pageTitle = computed(() => this.folderPath().at(-1)?.name ?? this.navLabels[this.activeNav()]);

  fileIcon(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'folder',
      document: 'description',
      image: 'image',
      video: 'movie',
      audio: 'music_note',
      archive: 'folder_zip',
      spreadsheet: 'table_chart',
      pdf: 'picture_as_pdf',
    };
    return map[type];
  }

  fileIconColor(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'text-amber-500',
      document: 'text-blue-500',
      image: 'text-violet-500',
      video: 'text-rose-500',
      audio: 'text-teal-500',
      archive: 'text-orange-500',
      spreadsheet: 'text-emerald-500',
      pdf: 'text-red-500',
    };
    return map[type];
  }

  fileBgColor(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'bg-amber-50',
      document: 'bg-blue-50',
      image: 'bg-violet-50',
      video: 'bg-rose-50',
      audio: 'bg-teal-50',
      archive: 'bg-orange-50',
      spreadsheet: 'bg-emerald-50',
      pdf: 'bg-red-50',
    };
    return map[type];
  }

  fileTypeName(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'Folder',
      document: 'Document',
      image: 'Image',
      video: 'Video',
      audio: 'Audio',
      archive: 'ZIP Archive',
      spreadsheet: 'Excel Spreadsheet',
      pdf: 'PDF Document',
    };
    return map[type];
  }

  // Actions
  navigateToPage(page: Exclude<Page, 'app'>): void {
    void this.router.navigateByUrl(`/${page}`);
  }

  selectNav(key: NavItem): void {
    const changed = key !== this.activeNav();
    this.activeNav.set(key);
    this.detailsSheetOpen.set(false);
    // Re-selecting the active section also returns to its root.
    if (changed || this.folderPath().length) {
      this.folderPath.set([]);
      this.loadFiles();
    }
  }

  openItem(file: FileItem, event?: MouseEvent): void {
    // A double-click with a modifier is two quick selection clicks, not an open.
    if (event?.ctrlKey || event?.metaKey || event?.shiftKey) {
      return;
    }
    if (file.type === 'folder') {
      this.openFolder(file);
    } else if (previewKind(file.contentType ?? '', file.fileSize ?? 0) !== 'none') {
      this.selectFile(file.id);
      this.openPreview();
    }
  }

  openFolder(folder: FileItem): void {
    this.folderPath.update(path => [...path, { id: folder.id, name: folder.name }]);
    this.loadFiles();
  }

  // depth 0 is the root; depth n keeps the first n folders of the path.
  goToFolder(depth: number): void {
    this.folderPath.update(path => path.slice(0, depth));
    this.loadFiles();
  }

  navigateCrumb(index: number): void {
    // Crumbs 0 and 1 are the app name and the section; folder crumbs start at index 2.
    if (index === 0 || index >= this.breadcrumb().length - 1) {
      return;
    }
    this.goToFolder(index - 1);
  }

  startCreateFolder(): void {
    this.folderEditor.set({ mode: 'create' });
    this.folderName.set('');
  }

  startRenameFolder(folder: FileItem): void {
    this.folderEditor.set({ mode: 'rename', folder });
    this.folderName.set(folder.name);
    this.contextMenu.set(null);
  }

  cancelFolderEditor(): void {
    this.folderEditor.set(null);
  }

  submitFolderEditor(): void {
    const editor = this.folderEditor();
    const name = this.folderName().trim();
    if (!editor || !name || this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');

    const request: Observable<unknown> = editor.mode === 'rename'
      ? (editor.folder.type === 'folder'
        ? this.foldersApi.update(editor.folder.id, name, this.currentFolderId())
        : this.filesApi.rename(editor.folder.id, name))
      : this.foldersApi.create(name, this.currentFolderId());
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.folderEditor.set(null);
        this.loadFiles();
      },
      error: error => {
        this.actionBusy.set(false);
        this.actionError.set(fileErrorMessage(error, this.folderErrorMessage(error, editor.mode === 'rename'
          ? `Unable to rename ${editor.folder.name}. Please try again.`
          : 'Unable to create the folder. Please try again.')));
      },
    });
  }

  deleteFolder(folder: FileItem): void {
    this.contextMenu.set(null);
    this.confirmTrash('Delete folder', `Delete ${folder.name} and everything in it? Files go to the Trash.`, () => this.runDeleteFolder(folder));
  }

  private runDeleteFolder(folder: FileItem): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    this.contextMenu.set(null);

    this.foldersApi.delete(folder.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.loadFiles();
      },
      error: error => {
        this.actionBusy.set(false);
        this.actionError.set(this.folderErrorMessage(error, `Unable to delete ${folder.name}. Please try again.`));
      },
    });
  }

  signOut(): void {
    this.auth.logout();
    this.files.set([]);
    this.folderPath.set([]);
    this.clearSelection();
    void this.router.navigateByUrl('/signin');
  }

  onSignInSubmit(e: Event): void {
    e.preventDefault();
    if (this.authBusy()) {
      return;
    }
    this.authBusy.set(true);
    this.signInError.set('');

    this.auth.login(this.signInEmail().trim(), this.signInPassword()).subscribe({
      next: () => {
        this.authBusy.set(false);
        this.signInPassword.set('');
        const returnUrl = this.router.parseUrl(this.router.url).queryParams['returnUrl'];
        const destination = typeof returnUrl === 'string' && returnUrl.startsWith('/') && !returnUrl.startsWith('//')
          ? returnUrl
          : '/app';
        void this.router.navigateByUrl(destination);
      },
      error: error => {
        this.authBusy.set(false);
        this.signInError.set(this.authErrorMessage(error, 'Unable to sign in. Please try again.'));
      },
    });
  }

  onSignUpSubmit(e: Event): void {
    e.preventDefault();
    if (this.authBusy()) {
      return;
    }
    this.authBusy.set(true);
    this.signUpError.set('');
    this.signUpSuccess.set('');

    this.auth.register(this.signUpEmail().trim(), this.signUpPassword(), this.requiresKey() !== false ? this.signUpKey().trim() : undefined).subscribe({
      next: () => {
        this.authBusy.set(false);
        this.signUpPassword.set('');
        this.signUpKey.set('');
        this.auth.setSignInNotice('Account created. Sign in to continue.');
        void this.router.navigateByUrl('/signin');
      },
      error: error => {
        this.authBusy.set(false);
        const body = errorBody(error);
        this.signUpError.set(
          body?.code === 'Auth.InviteInvalid'
            ? 'This invite key is invalid, expired or already used.'
            : (body?.code === 'Auth.RegistrationDisabled' || (error instanceof HttpErrorResponse && error.status === 400)) &&
            typeof body?.message === 'string'
            ? body.message
            : this.authErrorMessage(error, 'Unable to create your account. Please try again.'),
        );
      },
    });
  }

  onResetSubmit(e: Event): void {
    e.preventDefault();
    if (this.authBusy()) {
      return;
    }
    this.resetError.set('');
    if (!this.resetKey().trim()) {
      this.resetError.set('Enter your reset key.');
    } else if (this.resetPassword().length < 8) {
      this.resetError.set('The new password must be at least 8 characters.');
    } else if (this.resetPassword() !== this.resetConfirm()) {
      this.resetError.set('The new passwords do not match.');
    } else {
      this.authBusy.set(true);
      this.auth.reset(this.resetKey().trim(), this.resetPassword()).subscribe({
        next: () => {
          this.authBusy.set(false);
          this.resetKey.set('');
          this.resetPassword.set('');
          this.resetConfirm.set('');
          this.auth.setSignInNotice('Password reset. Sign in with your new password.');
          void this.router.navigateByUrl('/signin');
        },
        error: error => {
          this.authBusy.set(false);
          const body = errorBody(error);
          this.resetError.set(
            body?.code === 'Auth.InviteInvalid'
              ? 'This reset key is invalid, expired or already used.'
              : typeof body?.message === 'string' && body.message
                ? body.message
                : this.authErrorMessage(error, 'Unable to reset the password. Please try again.'),
          );
        },
      });
    }
  }

  selectFile(id: string, event?: MouseEvent): void {
    const files = this.filteredFiles();
    const isToggle = event?.ctrlKey || event?.metaKey;
    const currentSelection = this.selectedIds();

    if (event?.shiftKey) {
      const anchorId = this.selectionAnchorId();
      const anchorIndex = files.findIndex(file => file.id === anchorId);
      const targetIndex = files.findIndex(file => file.id === id);

      if (anchorIndex >= 0 && targetIndex >= 0) {
        const nextSelection = new Set(isToggle ? currentSelection : []);
        const [start, end] = [anchorIndex, targetIndex].sort((a, b) => a - b);
        for (const file of files.slice(start, end + 1)) {
          nextSelection.add(file.id);
        }
        this.setSelection(nextSelection, id);
      } else {
        this.setSelection(new Set([id]), id);
        this.selectionAnchorId.set(id);
      }
    } else if (isToggle) {
      const nextSelection = new Set(currentSelection);
      if (nextSelection.has(id)) {
        nextSelection.delete(id);
      } else {
        nextSelection.add(id);
      }
      this.setSelection(nextSelection, nextSelection.has(id) ? id : [...nextSelection].at(-1) ?? null);
      this.selectionAnchorId.set(id);
    } else {
      this.setSelection(new Set([id]), id);
      this.selectionAnchorId.set(id);
    }

    this.previewOpen.set(this.selectedIds().size > 0);
  }

  closeDetails(): void {
    this.previewOpen.set(false);
    this.detailsSheetOpen.set(false);
  }

  clearSelection(): void {
    this.setSelection(new Set(), null);
    this.selectionAnchorId.set(null);
    this.selectionRect.set(null);
    this.contextMenu.set(null);
    this.marqueeStart = null;
    this.marqueeSurface = null;
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.shareOpen() || this.previewModalOpen() || this.pendingTrash()) {
      return; // the modal closes itself
    }
    if (this.contextMenu()) {
      this.contextMenu.set(null);
      return;
    }
    if (this.detailsSheetOpen()) {
      this.detailsSheetOpen.set(false);
      return;
    }
    this.clearSelection();
  }

  @HostListener('window:resize')
  closeContextMenu(): void {
    this.contextMenu.set(null);
  }

  @HostListener('document:keydown.enter', ['$event'])
  onEnter(event: Event): void {
    // Ignore Enter typed into inputs and buttons; only act when nothing else has focus.
    if (event.target instanceof HTMLElement && event.target !== document.body) {
      return;
    }
    const selected = this.selectedFile();
    if (this.selectedIds().size === 1 && selected?.type === 'folder') {
      this.openFolder(selected);
    }
  }

  @HostListener('document:keydown', ['$event'])
  onShortcut(event: KeyboardEvent): void {
    const target = event.target;
    if (
      this.page() !== 'app' || this.shareOpen() || this.previewModalOpen() || this.pendingTrash() || this.folderEditor() ||
      (target instanceof Element && target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])'))
    ) {
      return;
    }
    const mod = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();
    const trash = this.activeNav() === 'trash';
    const selected = this.files().filter(file => this.selectedIds().has(file.id));
    const mutating = key === 'delete' || (mod && 'xvz'.includes(key));
    if (mutating && this.actionBusy()) {
      return;
    }

    let handled = false;
    if (mod && key === 'a') {
      const ids = this.filteredFiles().map(file => file.id);
      this.setSelection(new Set(ids), ids.at(-1) ?? null);
      this.selectionAnchorId.set(ids[0] ?? null);
      this.previewOpen.set(ids.length > 0);
      handled = true;
    } else if (key === 'delete' && event.shiftKey) {
      handled = trash && selected.length > 0;
      if (handled) {
        this.confirmTrash(
          'Delete forever',
          selected.length === 1 ? 'Delete forever? This file cannot be recovered.' : `Delete forever? These ${selected.length} items cannot be recovered.`,
          () => this.actions.bulk(selected, file => this.filesApi.deletePermanent(file.id), name => `Unable to delete ${name}. Please try again.`),
        );
      }
    } else if (key === 'delete') {
      handled = !trash && this.selectedIds().size > 0;
      if (handled) {
        const one = this.selectedFile();
        if (this.selectedIds().size === 1 && one?.type === 'folder') {
          this.deleteFile(one);
        } else {
          this.actions.trashSelected();
        }
      }
    } else if (key === 'f2' && !trash && this.selectedIds().size === 1 && this.selectedFile()) {
      this.startRenameFolder(this.selectedFile()!);
      handled = true;
    } else if (mod && (key === 'c' || key === 'x') && !trash && this.selectedIds().size > 0) {
      this.actions.toClipboard(key === 'c' ? 'copy' : 'cut');
      handled = true;
    } else if (mod && key === 'v' && this.activeNav() === 'my-files' && this.clipboard()) {
      handled = this.actions.paste();
    } else if (mod && key === 'z' && !event.shiftKey && this.actions.canUndo) {
      this.actions.undo();
      handled = true;
    }
    if (handled) {
      event.preventDefault();
    }
  }

  beginMarquee(event: PointerEvent): void {
    if (event.button !== 0 || !(event.currentTarget instanceof HTMLElement)) {
      return;
    }
    const surface = event.currentTarget;
    const target = event.target;
    if (target instanceof Element && target.closest('[data-file-id]')) {
      return;
    }

    event.preventDefault();
    surface.setPointerCapture(event.pointerId);
    this.marqueeSurface = surface;
    this.marqueeStart = { x: event.clientX, y: event.clientY };
    this.marqueeBaseSelection = event.ctrlKey || event.metaKey ? new Set(this.selectedIds()) : new Set();
    if (!event.ctrlKey && !event.metaKey) {
      this.setSelection(new Set(), null);
    }
    this.updateMarquee(event.clientX, event.clientY);
  }

  moveMarquee(event: PointerEvent): void {
    if (this.marqueeStart && event.currentTarget === this.marqueeSurface) {
      this.updateMarquee(event.clientX, event.clientY);
    }
  }

  endMarquee(event: PointerEvent): void {
    if (!this.marqueeStart || event.currentTarget !== this.marqueeSurface) {
      return;
    }
    this.updateMarquee(event.clientX, event.clientY);
    if (this.marqueeSurface?.hasPointerCapture(event.pointerId)) {
      this.marqueeSurface.releasePointerCapture(event.pointerId);
    }
    this.selectionAnchorId.set(this.selectedId());
    this.marqueeStart = null;
    this.marqueeSurface = null;
    this.selectionRect.set(null);
  }

  showContextMenu(file: FileItem, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.activeNav() !== 'trash' && !this.selectedIds().has(file.id)) {
      this.selectFile(file.id);
    }
    const items = this.activeNav() === 'trash' ? [file] : this.items().filter(item => this.selectedIds().has(item.id));
    this.openMenu(event, file.name, items, this.itemEntries(file));
  }

  // Right-click on empty space in the list or grid; items stop propagation themselves.
  showBackgroundMenu(event: MouseEvent): void {
    event.preventDefault();
    if (this.activeNav() === 'trash') {
      return;
    }
    this.openMenu(event, 'Folder actions', [], [
      { label: 'New folder', icon: 'create_new_folder', run: () => this.startCreateFolder() },
      { label: 'Upload files', icon: 'upload_file', run: () => this.uploadInput()?.nativeElement.click() },
      { label: 'Paste', icon: 'content_paste', shortcut: 'Ctrl+V', divider: true,
        disabled: !this.clipboard() || this.activeNav() !== 'my-files', run: () => void this.actions.paste() },
    ]);
  }

  runMenuEntry(entry: MenuEntry): void {
    this.contextMenu.set(null);
    entry.run();
  }

  moveMenuItems(items: FileItem[], targetId: string | null): void {
    this.contextMenu.set(null);
    this.moveItems(items, targetId);
  }

  private openMenu(event: MouseEvent, title: string, items: FileItem[], entries: MenuEntry[]): void {
    this.contextMenu.set({ title, items, entries, x: event.clientX, y: event.clientY });
  }

  private itemEntries(file: FileItem): MenuEntry[] {
    if (this.activeNav() === 'trash') {
      return [
        { label: 'Restore', icon: 'restore_from_trash', run: () => this.restoreFile(file) },
        { label: 'Delete forever', icon: 'delete_forever', danger: true, run: () => this.deleteForever(file) },
      ];
    }
    const selected = this.items().filter(item => this.selectedIds().has(item.id));
    const docs = selected.filter(item => item.type !== 'folder');
    const one = selected.length === 1 ? selected[0] : null;
    const oneFile = one && one.type !== 'folder' ? one : null;
    const oneFolder = one && one.type === 'folder' ? one : null;
    const unstar = selected.every(item => item.starred);
    const entries: (MenuEntry | false | null)[] = [
      oneFolder && { label: 'Open', icon: 'folder_open', shortcut: 'Enter', run: () => this.openFolder(oneFolder) },
      oneFile && { label: 'Preview', icon: 'visibility', run: () => this.openPreview() },
      oneFile && { label: 'Download', icon: 'download', run: () => this.downloadFile(oneFile) },
      oneFolder && { label: 'Download as zip', icon: 'folder_zip', run: () => this.downloadFolder(oneFolder) },
      one && { label: 'Share', icon: 'person_add', run: () => this.openShare() },
      one && { label: 'Rename', icon: 'edit', shortcut: 'F2', divider: true, run: () => this.startRenameFolder(one) },
      oneFile && { label: 'Make a copy', icon: 'file_copy', run: () => this.actions.copyItems([oneFile], this.currentFolderId()) },
      docs.length > 0 && { label: 'Copy', icon: 'content_copy', shortcut: 'Ctrl+C', run: () => this.actions.toClipboard('copy') },
      docs.length > 0 && { label: 'Cut', icon: 'content_cut', shortcut: 'Ctrl+X', run: () => this.actions.toClipboard('cut') },
      { label: 'Move to…', icon: 'drive_file_move', submenu: true, run: () => {} },
      {
        label: unstar ? 'Remove from favorites' : 'Add to favorites',
        icon: unstar ? 'star' : 'star_border',
        run: () => selected.filter(item => item.starred === unstar).forEach(item => this.toggleFileStar(item.id)),
      },
      one && { label: 'Show details', icon: 'info', divider: true, run: () => this.selectFile(one.id) },
      {
        label: 'Move to trash', icon: 'delete', shortcut: 'Del', danger: true, divider: !one,
        run: () => (oneFolder ? this.deleteFile(oneFolder) : this.actions.trashSelected()),
      },
    ];
    const result = entries.filter((entry): entry is MenuEntry => !!entry);
    // A leading separator would be empty.
    result[0] = { ...result[0], divider: false };
    return result;
  }

  toggleFileStar(id: string): void {
    const setStar = (starred: boolean) => (items: FileItem[]): FileItem[] =>
      items.map(file => file.id === id ? { ...file, starred } : file);
    const file = this.files().find(item => item.id === id);
    const folder = file ? undefined : this.folders().find(item => item.id === id);
    const target = file ?? folder;
    if (!target) return;
    const update = file ? this.files : this.folders;
    const request = file ? this.filesApi.star(id, !target.starred) : this.foldersApi.star(id, !target.starred);
    this.actionError.set('');
    update.update(setStar(!target.starred));
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {
        update.update(setStar(target.starred));
        this.actionError.set(`Unable to update favorites for ${target.name}. Please try again.`);
      },
    });
  }

  onUploadSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selected = Array.from(input.files ?? []);
    input.value = '';
    this.actions.uploadFiles(selected);
  }

  onDropFiles(event: DragEvent): void {
    event.preventDefault();
    this.dropKey.set(null);
    this.actions.uploadFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  // Accept OS file drags only; internal drags carry no files and are handled by the rows.
  dragOverFiles(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
    }
  }

  startDrag(event: DragEvent, file: FileItem): void {
    const selection = this.selectedIds();
    this.draggedItems = selection.has(file.id) ? this.items().filter(item => selection.has(item.id)) : [file];
    event.dataTransfer?.setData('text/plain', this.draggedItems.map(item => item.id).join(','));
  }

  endDrag(): void {
    this.draggedItems = [];
    this.dropKey.set(null);
  }

  // key identifies the highlighted target; targetId is the folder id, or null for root.
  dragOverTarget(event: DragEvent, key: string, targetId: string | null): void {
    if (!this.canDrop(this.draggedItems, targetId)) {
      return;
    }
    event.preventDefault();
    this.dropKey.set(key);
  }

  // Wrappers return void: a false return from a template handler would call preventDefault.
  dragOverFolder(event: DragEvent, folder: FileItem): void {
    if (folder.type === 'folder') {
      this.dragOverTarget(event, folder.id, folder.id);
    }
  }

  dropOnFolder(event: DragEvent, folder: FileItem): void {
    if (folder.type !== 'folder') {
      return;
    }
    const osFiles = Array.from(event.dataTransfer?.files ?? []);
    if (this.draggedItems.length === 0 && osFiles.length > 0) {
      // Stop the list's own drop handler from uploading the same files into the current folder.
      event.preventDefault();
      event.stopPropagation();
      this.dropKey.set(null);
      this.actions.uploadFiles(osFiles, folder.id);
      return;
    }
    this.dropItems(event, folder.id);
  }

  dragOverCrumb(event: DragEvent, index: number): void {
    if (index > 0) {
      this.dragOverTarget(event, `crumb${index}`, this.crumbTargetId(index));
    }
  }

  dropOnCrumb(event: DragEvent, index: number): void {
    if (index > 0) {
      this.dropItems(event, this.crumbTargetId(index));
    }
  }

  leaveTarget(key: string): void {
    if (this.dropKey() === key) {
      this.dropKey.set(null);
    }
  }

  dropItems(event: DragEvent, targetId: string | null): void {
    const items = this.draggedItems;
    this.endDrag();
    if (!this.canDrop(items, targetId)) {
      return;
    }
    event.preventDefault();
    this.moveItems(items, targetId);
  }

  isDropTarget(key: string): boolean {
    return this.dropKey() === key;
  }

  // Breadcrumb index 1 is the section root; deeper crumbs map to folderPath entries.
  crumbTargetId(index: number): string | null {
    return index <= 1 ? null : this.folderPath()[index - 2].id;
  }

  private canDrop(items: FileItem[], targetId: string | null): boolean {
    return items.length > 0 && targetId !== this.currentFolderId() && !items.some(item => item.id === targetId);
  }

  private moveItems(items: FileItem[], targetId: string | null, origin = this.currentFolderId(), record = true): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    const moved: FileItem[] = [];

    from(items)
      .pipe(
        concatMap(item => (item.type === 'folder'
          ? this.foldersApi.update(item.id, item.name, targetId)
          : this.filesApi.moveFile(item.id, targetId)
        ).pipe(
          tap(() => moved.push(item)),
          catchError(error => {
            this.actionError.set(this.folderErrorMessage(error, `Unable to move ${item.name}. Please try again.`));
            return EMPTY;
          }),
        )),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => {
          this.actionBusy.set(false);
          const error = this.actionError();
          this.loadFiles();
          this.actionError.set(error);
          if (record && moved.length > 0) {
            this.actions.pushUndo(() => this.moveItems(moved, origin, targetId, false));
          }
        },
      });
  }

  loadFiles(): void {
    this.moreSubscription?.unsubscribe();
    this.loadingMore = false;
    this.hasMoreFiles.set(false);
    this.files.set([]);
    this.folders.set([]);
    this.clearSelection();
    if (this.activeNav() === 'shared') {
      return; // the lazy shared view loads its own data
    }
    this.filesLoading.set(true);
    this.filesError.set('');
    this.actionError.set('');

    const parentId = this.currentFolderId();
    const nav = this.activeNav();
    const q = this.searchQuery().trim();
    const view: FilesView = nav === 'favorites' ? 'starred' : nav === 'recent' || nav === 'home' ? 'recent' : 'all';
    // Folders are listed only in the plain folder view; search, trash, starred and recent show files only.
    const withFolders = !q && view === 'all' && nav !== 'trash';
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = forkJoin({
      files: this.fetchFilesPage(0),
      folders: withFolders ? this.foldersApi.list(parentId) : of([]),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ files, folders }) => {
        this.folders.set(folders.map(toFolderItem));
        this.files.set(files.map(toFileItem));
        this.hasMoreFiles.set(files.length >= FILES_PAGE_SIZE);
        this.filesLoading.set(false);
      },
      error: () => {
        this.filesLoading.set(false);
        this.filesError.set('Unable to load your files. Please try again.');
      },
    });
  }

  private fetchFilesPage(skip: number): Observable<FileDto[]> {
    const nav = this.activeNav();
    if (nav === 'trash') {
      return this.filesApi.trash(skip);
    }
    const view: FilesView = nav === 'favorites' ? 'starred' : nav === 'recent' || nav === 'home' ? 'recent' : 'all';
    return this.filesApi.list(this.currentFolderId(), view, this.searchQuery().trim(), skip);
  }

  onFilesScroll(event: Event): void {
    const el = event.target as HTMLElement;
    if (el.scrollHeight - el.scrollTop - el.clientHeight < 200) {
      this.loadMoreFiles();
    }
  }

  loadMoreFiles(): void {
    if (!this.hasMoreFiles() || this.loadingMore || this.filesLoading()) {
      return;
    }
    this.loadingMore = true;
    this.moreSubscription = this.fetchFilesPage(this.files().length).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        // Local deletes shift the offset, so drop anything already shown.
        const known = new Set(this.files().map(file => file.id));
        this.files.update(files => [...files, ...page.filter(file => !known.has(file.id)).map(toFileItem)]);
        this.hasMoreFiles.set(page.length >= FILES_PAGE_SIZE);
        this.loadingMore = false;
      },
      error: () => {
        this.loadingMore = false;
        this.filesError.set('Unable to load more files. Please try again.');
      },
    });
  }

  openPreview(): void {
    this.previewModalOpen.set(true);
  }

  closePreview(): void {
    this.previewModalOpen.set(false);
  }

  downloadFile(file: FileItem): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');

    this.filesApi.download(file.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = file.name;
        anchor.click();
        // Deferred: some browsers start reading the URL after click() returns.
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        this.actionBusy.set(false);
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(`Unable to download ${file.name}. Please try again.`);
      },
    });
  }

  downloadFolder(folder: FileItem): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');

    this.foldersApi.download(folder.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `${folder.name}.zip`;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        this.actionBusy.set(false);
      },
      error: async (error: unknown) => {
        const message = this.folderErrorMessage(await readBlobError(error), `Unable to download ${folder.name}. Please try again.`);
        this.actionBusy.set(false);
        this.actionError.set(message);
      },
    });
  }

  deleteFile(file: FileItem): void {
    if (file.type === 'folder') {
      this.deleteFolder(file);
      return;
    }
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');

    this.filesApi.delete(file.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.files.update(files => files.filter(item => item.id !== file.id));
        const nextSelection = new Set(this.selectedIds());
        nextSelection.delete(file.id);
        this.setSelection(nextSelection, this.selectedId() === file.id ? [...nextSelection].at(-1) ?? null : this.selectedId());
        if (this.selectionAnchorId() === file.id) {
          this.selectionAnchorId.set(this.selectedId());
        }
        if (this.contextMenu()?.items.some(item => item.id === file.id)) {
          this.contextMenu.set(null);
        }
        this.actionBusy.set(false);
        this.refreshStorage();
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(`Unable to delete ${file.name}. Please try again.`);
      },
    });
  }

  restoreFile(file: FileItem): void {
    this.trashAction(this.filesApi.restore(file.id), `Unable to restore ${file.name}. Please try again.`);
  }

  deleteForever(file: FileItem): void {
    this.confirmTrash('Delete forever', 'Delete forever? This file cannot be recovered.', () =>
      this.trashAction(this.filesApi.deletePermanent(file.id), `Unable to delete ${file.name}. Please try again.`));
  }

  emptyTrash(): void {
    this.confirmTrash('Empty trash', `Empty trash? All ${this.files().length}${this.hasMoreFiles() ? '+' : ''} items will be permanently deleted.`, () =>
      this.trashAction(this.filesApi.emptyTrash(), 'Unable to empty the trash. Please try again.'));
  }

  // Destructive trash actions run only after the user confirms in the dialog.
  readonly pendingTrash = signal<{ title: string; message: string; run: () => void } | null>(null);

  private confirmTrash(title: string, message: string, run: () => void): void {
    this.pendingTrash.set({ title, message, run });
  }

  confirmPendingTrash(): void {
    const pending = this.pendingTrash();
    this.pendingTrash.set(null);
    pending?.run();
  }

  private trashAction(request: Observable<void>, failure: string): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    this.contextMenu.set(null);

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.loadFiles();
        this.refreshStorage();
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(failure);
      },
    });
  }

  private refreshStorage(): void {
    this.accountApi.storage().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: storage => this.storage.set(storage),
      error: () => {},
    });
  }

  private setSelection(ids: Set<string>, primaryId: string | null): void {
    this.selectedIds.set(ids);
    this.selectedId.set(primaryId);
    if (!ids.size) {
      this.detailsSheetOpen.set(false);
    }
  }

  private syncPageWithRoute(): void {
    const path = this.router.url.split('?')[0];
    const previous = this.page();
    const next = path === '/signup' ? 'signup' : path === '/reset' ? 'reset' : path === '/app' ? 'app' : 'signin';
    this.page.set(next);
    if (next === 'signup' && previous !== 'signup') {
      this.auth.setup().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: setup => this.requiresKey.set(setup.requiresKey),
        error: () => this.requiresKey.set(true),
      });
    }
    if (next === 'app' && previous !== 'app') {
      this.loadFiles();
      this.refreshStorage();
    }
  }

  private folderErrorMessage(error: unknown, fallback: string): string {
    switch (errorBody(error)?.code) {
      case 'Folder.NameTaken':
        return 'A folder with this name already exists here.';
      case 'Folder.InvalidName':
        return 'Enter a valid folder name.';
      case 'Folder.Cycle':
        return 'A folder cannot be moved into itself or one of its subfolders.';
      case 'Folder.TooLarge':
        return 'This folder has too many files to download as a zip.';
      default:
        return fallback;
    }
  }

  private authErrorMessage(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) {
      return fallback;
    }
    if (error.status === 0) {
      return 'Unable to connect to the server. Check your connection and try again.';
    }
    if (error.status === 429) {
      return 'Too many attempts. Wait a minute and try again.';
    }
    if (error.status === 401) {
      return 'Invalid email or password.';
    }

    const body: unknown = error.error;
    if (typeof body === 'object' && body !== null && 'description' in body && typeof body.description === 'string') {
      return body.description;
    }
    return fallback;
  }

  private updateMarquee(clientX: number, clientY: number): void {
    const start = this.marqueeStart;
    const surface = this.marqueeSurface;
    if (!start || !surface) {
      return;
    }

    const rect = {
      left: Math.min(start.x, clientX),
      top: Math.min(start.y, clientY),
      right: Math.max(start.x, clientX),
      bottom: Math.max(start.y, clientY),
    };
    this.selectionRect.set({
      left: rect.left,
      top: rect.top,
      width: rect.right - rect.left,
      height: rect.bottom - rect.top,
    });

    const nextSelection = new Set(this.marqueeBaseSelection);
    for (const item of surface.querySelectorAll<HTMLElement>('[data-file-id]')) {
      const itemRect = item.getBoundingClientRect();
      if (
        itemRect.left < rect.right &&
        itemRect.right > rect.left &&
        itemRect.top < rect.bottom &&
        itemRect.bottom > rect.top
      ) {
        const id = item.dataset['fileId'];
        if (id) {
          nextSelection.add(id);
        }
      }
    }
    this.setSelection(nextSelection, [...nextSelection].at(-1) ?? null);
  }

  openShare(): void {
    const file = this.selectedFile();
    if (!file) {
      return;
    }
    this.shareOpen.set(true);
  }

  closeShare(): void {
    this.shareOpen.set(false);
  }

  readonly accountOpen = signal<boolean>(false);
  readonly adminOpen = signal<boolean>(false);
  readonly isAdmin = this.auth.isAdmin;
}
