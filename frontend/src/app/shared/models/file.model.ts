export type FileType = 'folder' | 'document' | 'image' | 'video' | 'archive' | 'spreadsheet' | 'pdf' | 'audio' | 'code' | 'text';

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
export type NavItem = 'home' | 'my-files' | 'recent' | 'favorites' | 'trash' | 'shared';
