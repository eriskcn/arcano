import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';
import { FileDto, FilesService } from './files.service';

export type Page = 'signin' | 'signup' | 'app';
export type ViewMode = 'list' | 'grid';
export type NavItem = 'home' | 'my-files' | 'recent' | 'favorites' | 'shared' | 'trash';
export type ShareView = 'share' | 'manage' | 'link-settings';
export type LinkScope = 'anyone' | 'org' | 'existing' | 'chosen';

export interface FileItem {
  id: string;
  name: string;
  type: 'folder' | 'document' | 'image' | 'video' | 'archive' | 'spreadsheet' | 'pdf';
  size: string | null;
  modified: string;
  starred: boolean;
  shared: boolean;
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

const toFileItem = (file: FileDto): FileItem => ({
  id: file.id,
  name: file.fileName,
  type: fileTypeFromContentType(file.contentType),
  size: formatFileSize(file.fileSize),
  modified: new Date(file.createdAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
  starred: false,
  shared: false,
});

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  readonly appName = environment.appName;
  private readonly auth = inject(AuthService);
  private readonly filesApi = inject(FilesService);
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
  readonly contextMenu = signal<{ file: FileItem; left: number; top: number } | null>(null);
  readonly selectionRect = signal<{ left: number; top: number; width: number; height: number } | null>(null);
  private marqueeStart: { x: number; y: number } | null = null;
  private marqueeSurface: HTMLElement | null = null;
  private marqueeBaseSelection = new Set<string>();

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
  readonly shareView = signal<ShareView>('share');
  readonly shareRecipient = signal<string>('');
  readonly shareMessage = signal<string>('');
  readonly shareCopied = signal<boolean>(false);
  readonly shareSent = signal<boolean>(false);
  readonly manageTab = signal<'people' | 'groups' | 'links'>('people');
  readonly linkScope = signal<LinkScope>('org');
  readonly sharePermission = signal<string>('Can view');
  readonly shareExpiration = signal<string>('');

  ngOnInit(): void {
    this.syncPageWithRoute();
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncPageWithRoute());
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

  readonly linkScopeOptions: { key: LinkScope; label: string; sub?: string; icon: string; color: string }[] = [
    { key: 'anyone', label: 'Anyone', icon: 'public', color: 'text-green-600' },
    { key: 'org', label: `People in ${this.appName} Org`, sub: 'Share within your organization, account required.', icon: 'corporate_fare', color: 'text-blue-600' },
    { key: 'existing', label: 'Only people with existing access', icon: 'person', color: 'text-orange-500' },
    { key: 'chosen', label: 'People you choose', icon: 'group', color: 'text-purple-600' },
  ];

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
  readonly actionBusy = signal<boolean>(false);

  readonly showFiles = computed(() => {
    const nav = this.activeNav();
    return nav === 'my-files' || nav === 'home' || nav === 'recent' || nav === 'favorites' || nav === 'shared';
  });

  readonly filteredFiles = computed(() => {
    const q = this.searchQuery().toLowerCase();
    const nav = this.activeNav();
    const filter = this.activeFilter();

    return this.files().filter(f => {
      const matchSearch = f.name.toLowerCase().includes(q);
      if (!matchSearch) return false;
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
    return this.files().find(f => f.id === id) ?? null;
  });

  readonly breadcrumb = computed(() => {
    const nav = this.activeNav();
    return nav === 'my-files' ? [this.appName, 'My Files'] : [this.appName, this.navLabels[nav]];
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

  signOut(): void {
    this.auth.logout();
    this.files.set([]);
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
    this.contextMenu.set({
      file,
      left: Math.max(8, Math.min(event.clientX, window.innerWidth - menuWidth - 8)),
      top: Math.max(8, Math.min(event.clientY, window.innerHeight - menuHeight - 8)),
    });
  }

  toggleFileStar(id: string): void {
    this.files.update(files =>
      files.map(file => file.id === id ? { ...file, starred: !file.starred } : file),
    );
  }

  loadFiles(): void {
    this.files.set([]);
    this.clearSelection();
    this.filesLoading.set(true);
    this.filesError.set('');
    this.actionError.set('');

    this.filesApi.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: files => {
        this.files.set(files.map(toFileItem));
        this.filesLoading.set(false);
      },
      error: () => {
        this.filesLoading.set(false);
        this.filesError.set('Unable to load your files. Please try again.');
      },
    });
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
        URL.revokeObjectURL(url);
        this.actionBusy.set(false);
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(`Unable to download ${file.name}. Please try again.`);
      },
    });
  }

  deleteFile(file: FileItem): void {
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
      },
      error: () => {
        this.actionBusy.set(false);
        this.actionError.set(`Unable to delete ${file.name}. Please try again.`);
      },
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

  handleCopyLink(): void {
    navigator.clipboard.writeText('https://arcanodrive.app/share/df92bc7a').catch(() => {});
    this.shareCopied.set(true);
    setTimeout(() => this.shareCopied.set(false), 2000);
  }

  handleSendShare(): void {
    if (!this.shareRecipient().trim()) return;
    this.shareSent.set(true);
    setTimeout(() => {
      this.shareSent.set(false);
      this.shareRecipient.set('');
      this.shareMessage.set('');
    }, 1800);
  }
}
