import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

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

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  // Navigation & View State
  readonly page = signal<Page>('signin');
  readonly activeNav = signal<NavItem>('my-files');
  readonly sidebarCollapsed = signal<boolean>(false);
  readonly mobileSidebarOpen = signal<boolean>(false);
  readonly viewMode = signal<ViewMode>('list');
  readonly selectedId = signal<string | null>('1');
  readonly searchQuery = signal<string>('');
  readonly activeFilter = signal<string>('All');
  readonly previewOpen = signal<boolean>(true);

  // Auth Form State
  readonly signInEmail = signal<string>('');
  readonly signInPassword = signal<string>('');
  readonly signInError = signal<string>('');
  readonly showSignInPassword = signal<boolean>(false);

  readonly signUpFullName = signal<string>('');
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
    { key: 'org', label: 'People in ArcanoDrive Org', sub: 'Share within your organization, account required.', icon: 'corporate_fare', color: 'text-blue-600' },
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

  readonly files = signal<FileItem[]>([
    { id: '1', name: 'Design Assets', type: 'folder', size: null, modified: 'Jul 22, 2026', starred: true, shared: true },
    { id: '2', name: 'Q3 Marketing Campaign', type: 'folder', size: null, modified: 'Jul 20, 2026', starred: false, shared: true },
    { id: '3', name: 'Engineering Docs', type: 'folder', size: null, modified: 'Jul 18, 2026', starred: true, shared: false },
    { id: '4', name: 'Brand Guidelines v4.pdf', type: 'pdf', size: '8.4 MB', modified: 'Jul 21, 2026', starred: true, shared: false },
    { id: '5', name: 'Product Roadmap 2026.xlsx', type: 'spreadsheet', size: '1.2 MB', modified: 'Jul 19, 2026', starred: false, shared: true },
    { id: '6', name: 'Homepage Hero.png', type: 'image', size: '3.7 MB', modified: 'Jul 17, 2026', starred: false, shared: false },
    { id: '7', name: 'Demo Recording.mp4', type: 'video', size: '124 MB', modified: 'Jul 15, 2026', starred: false, shared: true },
    { id: '8', name: 'API Reference.docx', type: 'document', size: '512 KB', modified: 'Jul 14, 2026', starred: false, shared: false },
    { id: '9', name: 'Project Archive.zip', type: 'archive', size: '56 MB', modified: 'Jul 12, 2026', starred: false, shared: false },
    { id: '10', name: 'Meeting Notes July.docx', type: 'document', size: '84 KB', modified: 'Jul 10, 2026', starred: false, shared: true },
    { id: '11', name: 'User Research Photos', type: 'folder', size: null, modified: 'Jul 8, 2026', starred: false, shared: false },
    { id: '12', name: 'Sprint Retrospective.pdf', type: 'pdf', size: '2.1 MB', modified: 'Jul 5, 2026', starred: false, shared: false },
  ]);

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
    return nav === 'my-files' ? ['ArcanoDrive', 'My Files'] : ['ArcanoDrive', this.navLabels[nav]];
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
  onSignInSubmit(e: Event): void {
    e.preventDefault();
    if (this.signInEmail() === 'admin' && this.signInPassword() === 'password') {
      this.signInError.set('');
      this.page.set('app');
    } else {
      this.signInError.set('Invalid email or password.');
    }
  }

  onSignUpSubmit(e: Event): void {
    e.preventDefault();
    this.page.set('app');
  }

  selectFile(id: string): void {
    this.selectedId.set(id);
    this.previewOpen.set(true);
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
