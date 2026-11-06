import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { errorBody } from './http-errors';
import { startDownload } from './download';
import { FilePreviewComponent } from './preview/file-preview.component';
import { previewKind } from './preview/preview-kind';
import { ConfirmDialogComponent } from './shared/components/confirm-dialog.component';
import { ModalComponent } from './shared/components/modal.component';
import { FileSizePipe } from './shared/pipes/file-size.pipe';
import { SHARED_PAGE_SIZE, SharedFileDto, SharedItemDto, UserSharesService } from './user-shares.service';

interface Crumb { id: string | null; name: string }

// Read-only view of items other users shared with me. Only actions: open, preview, download, remove my share.
@Component({
  selector: 'app-shared-with-me',
  standalone: true,
  imports: [CommonModule, FileSizePipe, FilePreviewComponent, ModalComponent, ConfirmDialogComponent],
  template: `
    <div class="flex-1 overflow-y-auto">
      <nav *ngIf="share()" class="flex flex-wrap items-center gap-1 px-4 py-2 text-xs text-gray-500" aria-label="Breadcrumb">
        <button type="button" (click)="backToList()" class="hover:text-blue-600 cursor-pointer">Shared with me</button>
        <ng-container *ngFor="let crumb of path(); let i = index">
          <span>/</span>
          <button type="button" (click)="goTo(i + 1)" class="hover:text-blue-600 cursor-pointer">{{ crumb.name }}</button>
        </ng-container>
      </nav>
      <div *ngIf="error()" class="mx-4 my-2 rounded-xl border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-700" role="alert">{{ error() }}</div>
      <p *ngIf="loading()" class="px-4 py-3 text-xs font-medium text-gray-500" role="status">Loading...</p>
      <p *ngIf="!loading() && !error() && isEmpty()" class="px-4 py-8 text-center text-sm text-gray-500">
        {{ share() ? 'This folder is empty.' : 'Nothing has been shared with you yet.' }}
      </p>

      <ul *ngIf="!share()" class="divide-y divide-gray-100">
        <li *ngFor="let item of shares()" class="flex items-center gap-3 px-4 py-2.5">
          <span class="material-symbols-rounded" [ngClass]="item.kind === 'folder' ? 'text-amber-500' : 'text-blue-500'" style="font-size: 22px">{{ item.kind === 'folder' ? 'folder' : 'description' }}</span>
          <div class="min-w-0 flex-1">
            <p class="text-sm font-medium text-gray-800 truncate">{{ item.name }}</p>
            <p class="text-xs text-gray-500 truncate">{{ item.ownerEmail }}{{ item.sizeBytes !== null ? ' · ' + (item.sizeBytes | fileSize) : '' }}</p>
          </div>
          <button *ngIf="item.kind === 'folder'" type="button" (click)="openShare(item)" class="text-xs font-semibold text-blue-600 cursor-pointer">Open</button>
          <ng-container *ngIf="item.kind === 'file'">
            <button *ngIf="canPreview(item.contentType, item.sizeBytes)" type="button" (click)="preview(item.id, item.itemId, item.name, item.contentType, item.sizeBytes)" class="text-xs font-semibold text-blue-600 cursor-pointer">Preview</button>
            <button type="button" (click)="downloadFile(item.id, item.itemId, item.name)" class="text-xs font-semibold text-blue-600 cursor-pointer">Download</button>
          </ng-container>
          <button *ngIf="item.kind === 'folder'" type="button" (click)="downloadFolder(item.id, item.itemId, item.name)" class="text-xs font-semibold text-blue-600 cursor-pointer">Download zip</button>
          <button type="button" (click)="pendingRemove.set(item)" class="text-xs font-semibold text-red-600 cursor-pointer">Remove</button>
        </li>
      </ul>

      <ul *ngIf="share()" class="divide-y divide-gray-100">
        <li *ngFor="let folder of folders()" class="flex items-center gap-3 px-4 py-2.5">
          <span class="material-symbols-rounded text-amber-500" style="font-size: 22px">folder</span>
          <button type="button" (click)="openFolder(folder)" class="min-w-0 flex-1 truncate text-left text-sm font-medium text-gray-800 hover:text-blue-600 cursor-pointer">{{ folder.name }}</button>
          <button type="button" (click)="downloadFolder(share()!.id, folder.id, folder.name)" class="text-xs font-semibold text-blue-600 cursor-pointer">Download zip</button>
        </li>
        <li *ngFor="let file of files()" class="flex items-center gap-3 px-4 py-2.5">
          <span class="material-symbols-rounded text-blue-500" style="font-size: 22px">description</span>
          <div class="min-w-0 flex-1">
            <p class="text-sm font-medium text-gray-800 truncate">{{ file.fileName }}</p>
            <p class="text-xs text-gray-500">{{ file.sizeBytes | fileSize }}</p>
          </div>
          <button *ngIf="canPreview(file.contentType, file.sizeBytes)" type="button" (click)="preview(share()!.id, file.id, file.fileName, file.contentType, file.sizeBytes)" class="text-xs font-semibold text-blue-600 cursor-pointer">Preview</button>
          <button type="button" (click)="downloadFile(share()!.id, file.id, file.fileName)" class="text-xs font-semibold text-blue-600 cursor-pointer">Download</button>
        </li>
      </ul>
      <div *ngIf="hasMore()" class="px-4 py-3">
        <button type="button" (click)="loadMore()" class="text-xs font-semibold text-blue-600 cursor-pointer">Load more</button>
      </div>
    </div>

    <app-modal [open]="!!previewing()" [title]="previewing()?.name ?? ''" maxWidth="2xl" (close)="previewing.set(null)">
      <app-file-preview
        *ngIf="previewing() as p"
        [loader]="p.loader"
        [contentType]="p.contentType"
        [fileSize]="p.size"
        [fileName]="p.name"
        (download)="p.save()"
      />
    </app-modal>

    <app-confirm-dialog
      [open]="!!pendingRemove()"
      title="Remove shared item"
      [message]="'Remove ' + (pendingRemove()?.name ?? '') + ' from Shared with me? The owner keeps the original.'"
      confirmLabel="Remove"
      [danger]="true"
      (confirmed)="confirmRemove()"
      (cancelled)="pendingRemove.set(null)"
    />
  `,
})
export class SharedWithMeComponent {
  private readonly api = inject(UserSharesService);
  private readonly destroyRef = inject(DestroyRef);

  readonly shares = signal<SharedItemDto[]>([]);
  readonly share = signal<SharedItemDto | null>(null); // the shared folder being browsed
  readonly path = signal<Crumb[]>([]); // first crumb is the shared folder root (id null)
  readonly folders = signal<{ id: string; name: string }[]>([]);
  readonly files = signal<SharedFileDto[]>([]);
  readonly hasMore = signal<boolean>(false);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly pendingRemove = signal<SharedItemDto | null>(null);
  readonly previewing = signal<{ name: string; contentType: string; size: number; loader: () => Observable<Blob>; save: () => void } | null>(null);

  constructor() {
    this.loadShares();
  }

  isEmpty(): boolean {
    return this.share() ? !this.folders().length && !this.files().length : !this.shares().length;
  }

  canPreview(contentType: string | null, size: number | null): boolean {
    return previewKind(contentType ?? '', size ?? 0) !== 'none';
  }

  private loadShares(): void {
    this.load(this.api.incoming(), shares => this.shares.set(shares), 'Unable to load shared items.');
  }

  backToList(): void {
    this.share.set(null);
    this.path.set([]);
    this.hasMore.set(false);
    this.loadShares();
  }

  openShare(item: SharedItemDto): void {
    this.share.set(item);
    this.path.set([{ id: null, name: item.name }]);
    this.loadChildren();
  }

  openFolder(folder: { id: string; name: string }): void {
    this.path.update(path => [...path, folder]);
    this.loadChildren();
  }

  // depth n keeps the first n crumbs of the path.
  goTo(depth: number): void {
    this.path.update(path => path.slice(0, depth));
    this.loadChildren();
  }

  private loadChildren(skip = 0): void {
    const shareId = this.share()!.id;
    this.load(this.api.children(shareId, this.path().at(-1)!.id, skip), children => {
      this.folders.set(skip ? this.folders() : children.folders);
      this.files.set(skip ? [...this.files(), ...children.files] : children.files);
      this.hasMore.set(children.files.length >= SHARED_PAGE_SIZE);
    }, 'Unable to open this folder.');
  }

  loadMore(): void {
    this.loadChildren(this.files().length);
  }

  private load<T>(request: Observable<T>, apply: (value: T) => void, failure: string): void {
    this.loading.set(true);
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => {
        apply(value);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(failure);
      },
    });
  }

  preview(shareId: string, fileId: string, name: string, contentType: string | null, size: number | null): void {
    this.previewing.set({
      name,
      contentType: contentType ?? '',
      size: size ?? 0,
      loader: () => this.api.downloadFile(shareId, fileId),
      save: () => this.downloadFile(shareId, fileId, name),
    });
  }

  downloadFile(shareId: string, fileId: string, name: string): void {
    this.save(this.api.fileDownloadUrl(shareId, fileId), name);
  }

  downloadFolder(shareId: string, folderId: string, name: string): void {
    this.save(this.api.folderDownloadUrl(shareId, folderId), `${name}.zip`);
  }

  private save(request: Observable<string>, fileName: string): void {
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: url => startDownload(url),
      error: (error: unknown) => {
        const tooLarge = errorBody(error)?.code === 'Folder.TooLarge';
        this.error.set(tooLarge ? 'This folder has too many files to download as a zip.' : `Unable to download ${fileName}. Please try again.`);
      },
    });
  }

  confirmRemove(): void {
    const item = this.pendingRemove();
    this.pendingRemove.set(null);
    if (!item) return;
    this.api.remove(item.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.shares.update(list => list.filter(s => s.id !== item.id)),
      error: () => this.error.set(`Unable to remove ${item.name}. Please try again.`),
    });
  }
}
