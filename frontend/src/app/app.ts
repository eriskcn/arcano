import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router } from '@angular/router';
import { EMPTY, Observable, Subject, Subscription, catchError, concatMap, debounceTime, distinctUntilChanged, filter, forkJoin, from, of, tap } from 'rxjs';
import { environment } from '../environments/environment';
import { AccountService } from './account.service';
import { AuthService } from './auth.service';
import { FileDto, FilesService, FilesView } from './files.service';
import { FolderDto, FoldersService } from './folders.service';
import { ShareLinkDto, ShareService } from './share.service';
import { FilePreviewComponent } from './preview/file-preview.component';
import { ModalComponent } from './shared/components/modal.component';
import { QuotaMeterComponent } from './shared/components/quota-meter.component';

export type Page = 'signin' | 'signup' | 'app';
export type ViewMode = 'list' | 'grid';
export type NavItem = 'home' | 'my-files' | 'recent' | 'favorites' | 'shared' | 'trash';

export interface FileItem {
  id: string;
  name: string;
  type: 'folder' | 'document' | 'image' | 'video' | 'archive' | 'spreadsheet' | 'pdf';
  size: string | null;
  modified: string;
  starred: boolean;
  shared: boolean;
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
  shared: false,
  contentType: file.contentType,
  fileSize: file.fileSize,
});

const toFolderItem = (folder: FolderDto): FileItem => ({
  id: folder.id,
  name: folder.name,
  type: 'folder',
  size: null,
  modified: formatDate(folder.createdAtUtc),
  starred: false,
  shared: false,
});

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, FilePreviewComponent, ModalComponent, QuotaMeterComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  readonly appName = environment.appName;
  private readonly auth = inject(AuthService);
  private readonly filesApi = inject(FilesService);
  private readonly foldersApi = inject(FoldersService);
  private readonly shareApi = inject(ShareService);
  private readonly accountApi = inject(AccountService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly userEmail = this.auth.email;
  readonly userInitial = computed(() => this.userEmail()?.trim().charAt(0).toUpperCase() || '?');

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
  readonly previewModalOpen = signal<boolean>(false);
  readonly contextMenu = signal<{ file: FileItem; left: number; top: number } | null>(null);
  readonly selectionRect = signal<{ left: number; top: number; width: number; height: number } | null>(null);
  private marqueeStart: { x: number; y: number } | null = null;
  private marqueeSurface: HTMLElement | null = null;
  private marqueeBaseSelection = new Set<string>();
  // In-memory clipboard for files (folders are never copied) and a capped undo stack.
  readonly clipboard = signal<{ mode: 'copy' | 'cut'; items: FileItem[]; sourceFolderId: string | null } | null>(null);
  private undoStack: (() => void)[] = [];

  // Auth Form State
  readonly signInEmail = signal<string>('');
  readonly signInPassword = signal<string>('');
  readonly signInError = signal<string>('');
  readonly signUpError = signal<string>('');
  readonly signUpSuccess = signal<string>('');
  readonly authBusy = signal<boolean>(false);
  readonly showSignInPassword = signal<boolean>(false);

  readonly signUpEmail = signal<string>('');
  readonly signUpPassword = signal<string>('');
  readonly showSignUpPassword = signal<boolean>(false);

  // Share Dialog State
  readonly shareOpen = signal<boolean>(false);
  readonly shareLinks = signal<ShareLinkDto[]>([]);
  readonly shareExpiryDays = signal<number | null>(null);
  readonly shareBusy = signal<boolean>(false);
  readonly shareError = signal<string>('');
  readonly shareExpiryOptions: { label: string; days: number | null }[] = [
    { label: 'Never', days: null },
    { label: '1 day', days: 1 },
    { label: '7 days', days: 7 },
    { label: '30 days', days: 30 },
  ];

  ngOnInit(): void {
    this.syncPageWithRoute();
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncPageWithRoute());
    // loadFiles cancels the previous request, so a stale search never overwrites a newer one.
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
    { key: 'shared', label: 'Shared', icon: 'group' },
    { key: 'trash', label: 'Trash', icon: 'delete' },
  ];

  readonly filterChips = ['All', 'Folders', 'Documents', 'Images', 'Videos', 'Shared'];

  readonly navLabels: Record<NavItem, string> = {
    home: 'Home',
    'my-files': 'My Files',
    recent: 'Recent',
    favorites: 'Favorites',
    shared: 'Shared',
    trash: 'Trash',
  };

  readonly emptyMessages: Record<NavItem, { icon: string; title: string; sub: string }> = {
    home: { icon: 'home', title: 'Welcome home', sub: 'Your recent and starred files will appear here.' },
    'my-files': { icon: 'folder', title: 'No files yet', sub: 'Upload or create new files to get started.' },
    recent: { icon: 'schedule', title: 'No recent activity', sub: 'Files you open will appear here.' },
    favorites: { icon: 'star', title: 'No favorites yet', sub: 'Star files to find them quickly.' },
    shared: { icon: 'group', title: 'Nothing shared', sub: 'Files shared with you will appear here.' },
    trash: { icon: 'delete', title: 'Trash is empty', sub: 'Deleted files will appear here for 30 days.' },
  };

  readonly files = signal<FileItem[]>([]);
  readonly filesLoading = signal<boolean>(false);
  readonly filesError = signal<string>('');
  readonly actionError = signal<string>('');
  readonly uploadState = signal<{ name: string; index: number; total: number; percent: number } | null>(null);
  readonly actionBusy = signal<boolean>(false);

  // Folder navigation: folders of the current folder, the path to it, and the create/rename editor.
  readonly folders = signal<FileItem[]>([]);
  readonly folderPath = signal<{ id: string; name: string }[]>([]);
  readonly folderEditor = signal<{ mode: 'create' } | { mode: 'rename'; folder: FileItem } | null>(null);
  readonly folderName = signal<string>('');
  readonly contextMenuView = signal<'actions' | 'move'>('actions');
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
    return nav === 'my-files' || nav === 'home' || nav === 'recent' || nav === 'favorites' || nav === 'shared' || nav === 'trash';
  });

  readonly filteredFiles = computed(() => {
    const nav = this.activeNav();
    const filter = this.activeFilter();

    return this.items().filter(f => {
      if (nav === 'favorites') return f.starred;
      if (nav === 'shared') return f.shared;
      if (filter === 'Folders') return f.type === 'folder';
      if (filter === 'Documents') return f.type === 'document' || f.type === 'pdf' || f.type === 'spreadsheet';
      if (filter === 'Images') return f.type === 'image';
      if (filter === 'Videos') return f.type === 'video';
      if (filter === 'Shared') return f.shared;
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

  fileIcon(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'folder',
      document: 'description',
      image: 'image',
      video: 'movie',
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
      archive: 'bg-orange-50',
      spreadsheet: 'bg-emerald-50',
      pdf: 'bg-red-50',
    };
    return map[type];
  }

  fileTypeName(type: FileItem['type']): string {
    const map: Record<FileItem['type'], string> = {
      folder: 'Folder',
      document: 'Word Document',
      image: 'PNG Image',
      video: 'MP4 Video',
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
    if (changed) {
      this.folderPath.set([]);
      this.loadFiles();
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
        this.actionError.set(this.fileErrorMessage(error, this.folderErrorMessage(error, editor.mode === 'rename'
          ? `Unable to rename ${editor.folder.name}. Please try again.`
          : 'Unable to create the folder. Please try again.')));
      },
    });
  }

  deleteFolder(folder: FileItem): void {
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

  moveFile(file: FileItem, folderId: string | null): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    this.contextMenu.set(null);

    this.filesApi.moveFile(file.id, folderId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.loadFiles();
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(`Unable to move ${file.name}. Please try again.`);
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

    this.auth.register(this.signUpEmail().trim(), this.signUpPassword()).subscribe({
      next: () => {
        this.authBusy.set(false);
        this.signUpPassword.set('');
        this.signUpSuccess.set('Account created. Sign in to continue.');
        void this.router.navigateByUrl('/signin');
      },
      error: error => {
        this.authBusy.set(false);
        this.signUpError.set(this.authErrorMessage(error, 'Unable to create your account. Please try again.'));
      },
    });
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
    this.clearSelection();
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
      this.page() !== 'app' || this.shareOpen() || this.previewModalOpen() || this.folderEditor() ||
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
        this.bulk(selected, file => this.filesApi.deletePermanent(file.id), name => `Unable to delete ${name}. Please try again.`);
      }
    } else if (key === 'delete') {
      handled = !trash && selected.length > 0;
      if (handled) {
        this.bulk(selected, file => this.filesApi.delete(file.id), name => `Unable to delete ${name}. Please try again.`, done => {
          const deleted = done.map(([file]) => file);
          this.pushUndo(() => this.bulk(deleted, file => this.filesApi.restore(file.id), name => `Unable to restore ${name}. Please try again.`));
        });
      }
    } else if (key === 'f2' && !trash && this.selectedIds().size === 1 && this.selectedFile()) {
      this.startRenameFolder(this.selectedFile()!);
      handled = true;
    } else if (mod && (key === 'c' || key === 'x') && !trash && selected.length > 0) {
      this.clipboard.set({ mode: key === 'c' ? 'copy' : 'cut', items: selected, sourceFolderId: this.currentFolderId() });
      handled = true;
    } else if (mod && key === 'v' && this.activeNav() === 'my-files' && this.clipboard()) {
      handled = this.paste();
    } else if (mod && key === 'z' && !event.shiftKey && this.undoStack.length > 0) {
      this.undoStack.pop()!();
      handled = true;
    }
    if (handled) {
      event.preventDefault();
    }
  }

  private paste(): boolean {
    const clip = this.clipboard()!;
    const folderId = this.currentFolderId();
    if (clip.mode === 'cut') {
      if (clip.sourceFolderId === folderId) {
        return false;
      }
      this.clipboard.set(null);
      this.moveItems(clip.items, folderId, clip.sourceFolderId);
      return true;
    }
    this.bulk(clip.items, file => this.filesApi.copyFile(file.id, folderId), name => `Unable to copy ${name}. Please try again.`, done => {
      const copies = done.map(([file, copy]) => ({ id: copy.id, name: file.name }));
      this.pushUndo(() => this.bulk(copies, file => this.filesApi.delete(file.id), name => `Unable to undo the copy of ${name}. Please try again.`));
    });
    return true;
  }

  private pushUndo(undo: () => void): void {
    this.undoStack = [...this.undoStack, undo].slice(-20);
  }

  // Runs op for each item in order, reports failures through actionError, then reloads like upload does.
  private bulk<R>(
    items: { id: string; name: string }[],
    op: (item: { id: string; name: string }) => Observable<R>,
    failure: (name: string) => string,
    done?: (succeeded: [{ id: string; name: string }, R][]) => void,
  ): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    const succeeded: [{ id: string; name: string }, R][] = [];

    from(items)
      .pipe(
        concatMap(item => op(item).pipe(
          tap(result => succeeded.push([item, result])),
          catchError(error => {
            this.actionError.set(this.fileErrorMessage(error, failure(item.name)));
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
          this.refreshStorage();
          if (succeeded.length > 0) {
            done?.(succeeded);
          }
        },
      });
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

    const menuWidth = 192;
    const menuHeight = 80;
    this.contextMenuView.set('actions');
    this.contextMenu.set({
      file,
      left: Math.max(8, Math.min(event.clientX, window.innerWidth - menuWidth - 8)),
      top: Math.max(8, Math.min(event.clientY, window.innerHeight - menuHeight - 8)),
    });
  }

  toggleFileStar(id: string): void {
    const setStar = (starred: boolean) => (items: FileItem[]): FileItem[] =>
      items.map(file => file.id === id ? { ...file, starred } : file);
    const file = this.files().find(item => item.id === id);
    if (!file) {
      // Folders have no backend star yet, so this stays local.
      this.folders.update(items => items.map(item => item.id === id ? { ...item, starred: !item.starred } : item));
      return;
    }
    this.actionError.set('');
    this.files.update(setStar(!file.starred));
    this.filesApi.star(id, !file.starred).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {
        this.files.update(setStar(file.starred));
        this.actionError.set(`Unable to update favorites for ${file.name}. Please try again.`);
      },
    });
  }

  onUploadSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const selected = Array.from(input.files ?? []);
    input.value = '';
    this.uploadFiles(selected);
  }

  onDropFiles(event: DragEvent): void {
    event.preventDefault();
    this.dropKey.set(null);
    this.uploadFiles(Array.from(event.dataTransfer?.files ?? []));
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
      this.uploadFiles(osFiles, folder.id);
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
            this.pushUndo(() => this.moveItems(moved, origin, targetId, false));
          }
        },
      });
  }

  private uploadFiles(selected: File[], folderId: string | null = this.currentFolderId()): void {
    if (selected.length === 0 || this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');

    from(selected)
      .pipe(
        concatMap((file, index) => (file.size > 8 * 1024 * 1024 ? this.filesApi.uploadChunked(file, folderId) : this.filesApi.upload(file, folderId)).pipe(
          tap(percent => this.uploadState.set({ name: file.name, index: index + 1, total: selected.length, percent })),
          catchError(error => {
            this.actionError.set(this.fileErrorMessage(error, `Unable to upload ${file.name}. Please try again.`));
            return EMPTY;
          }),
        )),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => {
          this.actionBusy.set(false);
          this.uploadState.set(null);
          const error = this.actionError();
          this.loadFiles();
          this.actionError.set(error);
          this.refreshStorage();
        },
      });
  }

  loadFiles(): void {
    this.files.set([]);
    this.folders.set([]);
    this.clearSelection();
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
      files: nav === 'trash' ? this.filesApi.trash() : this.filesApi.list(parentId, view, q),
      folders: withFolders ? this.foldersApi.list(parentId) : of([]),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ files, folders }) => {
        this.folders.set(folders.map(toFolderItem));
        this.files.set(files.map(toFileItem));
        this.filesLoading.set(false);
      },
      error: () => {
        this.filesLoading.set(false);
        this.filesError.set('Unable to load your files. Please try again.');
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
        if (this.contextMenu()?.file.id === file.id) {
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
    this.trashAction(this.filesApi.deletePermanent(file.id), `Unable to delete ${file.name}. Please try again.`);
  }

  emptyTrash(): void {
    this.trashAction(this.filesApi.emptyTrash(), 'Unable to empty the trash. Please try again.');
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
  }

  private syncPageWithRoute(): void {
    const path = this.router.url.split('?')[0];
    const previous = this.page();
    const next = path === '/signup' ? 'signup' : path === '/app' ? 'app' : 'signin';
    this.page.set(next);
    if (next === 'app' && previous !== 'app') {
      this.loadFiles();
      this.refreshStorage();
    }
  }

  private errorBody(error: unknown): { code?: unknown; description?: unknown; detail?: unknown; title?: unknown; message?: unknown } | null {
    const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
    return typeof body === 'object' && body !== null ? (body as NonNullable<ReturnType<App['errorBody']>>) : null;
  }

  private fileErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.status === 413) {
      return 'File is too large.';
    }
    switch (this.errorBody(error)?.code) {
      case 'Storage.QuotaExceeded':
        return 'Not enough storage space.';
      case 'File.InvalidName':
        return 'Enter a valid file name.';
      default:
        return fallback;
    }
  }

  private folderErrorMessage(error: unknown, fallback: string): string {
    switch (this.errorBody(error)?.code) {
      case 'Folder.NameTaken':
        return 'A folder with this name already exists here.';
      case 'Folder.InvalidName':
        return 'Enter a valid folder name.';
      case 'Folder.Cycle':
        return 'A folder cannot be moved into itself or one of its subfolders.';
      case 'Folder.NotEmpty':
        return 'Only empty folders can be deleted. Move or delete their contents first.';
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
    this.shareLinks.set([]);
    this.shareError.set('');
    this.shareApi.list(file.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: links => this.shareLinks.set(links),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to load share links.')),
    });
  }

  closeShare(): void {
    this.shareOpen.set(false);
  }

  createShareLink(): void {
    const file = this.selectedFile();
    if (!file || this.shareBusy()) {
      return;
    }
    const days = this.shareExpiryDays();
    const expiresAtUtc = days === null ? null : new Date(Date.now() + days * 86_400_000).toISOString();
    this.shareBusy.set(true);
    this.shareError.set('');
    this.shareApi.create(file.id, expiresAtUtc).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: link => {
        this.shareLinks.update(links => [link, ...links]);
        this.shareBusy.set(false);
      },
      error: err => {
        this.shareBusy.set(false);
        this.shareError.set(this.shareErrorMessage(err, 'Unable to create share link.'));
      },
    });
  }

  revokeShareLink(link: ShareLinkDto): void {
    this.shareError.set('');
    this.shareApi.revoke(link.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.shareLinks.update(links => links.filter(l => l.id !== link.id)),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to revoke share link.')),
    });
  }

  copyShareLink(link: ShareLinkDto): void {
    navigator.clipboard.writeText(this.shareApi.linkFor(link.token)).catch(() => {});
  }

  shareLinkUrl(token: string): string {
    return this.shareApi.linkFor(token);
  }

  private shareErrorMessage(err: unknown, fallback: string): string {
    const raw = err instanceof HttpErrorResponse ? err.error : null;
    const body = this.errorBody(err);
    const text = [body?.description, body?.detail, body?.title, body?.message].find(v => typeof v === 'string' && v);
    return (typeof raw === 'string' && raw) || (text as string | undefined) || fallback;
  }
}
