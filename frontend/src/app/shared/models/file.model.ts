export type FileType = 'folder' | 'document' | 'image' | 'video' | 'archive' | 'spreadsheet' | 'pdf' | 'audio' | 'code' | 'text';

export interface FileItem {
  id: string;
  name: string;
  type: FileType;
  size: string | null;
  sizeBytes?: number;
  modified: string;
  starred: boolean;
  shared: boolean;
  parentId?: string | null;
}

export type UserRole = 'admin' | 'user';

export interface UserProfile {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  usedBytes: number;
  quotaBytes: number;
  avatarUrl?: string;
}

export type ViewMode = 'list' | 'grid';
export type NavItem = 'home' | 'my-files' | 'recent' | 'favorites' | 'trash';
