import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, concatMap, from, takeUntil, tap } from 'rxjs';
import type { FileItem } from './app';
import { FilesService } from './files.service';
import { fileErrorMessage } from './http-errors';

// What the service needs from the explorer that owns it.
export interface FileActionsHost {
  files(): FileItem[];
  selectedIds(): ReadonlySet<string>;
  currentFolderId(): string | null;
  reload(): void;
  refreshStorage(): void;
  move(items: FileItem[], targetId: string | null, origin: string | null): void;
}

// Busy/error state, clipboard, undo stack, bulk file actions and uploads. Provided per AppShell, which calls bind().
@Injectable()
export class FileActionsService {
  private readonly filesApi = inject(FilesService);
  private readonly destroyRef = inject(DestroyRef);
  private host!: FileActionsHost;

  readonly actionBusy = signal<boolean>(false);
  readonly actionError = signal<string>('');
  readonly uploadState = signal<{ name: string; index: number; total: number; percent: number } | null>(null);
  readonly clipboard = signal<{ mode: 'copy' | 'cut'; items: FileItem[]; sourceFolderId: string | null } | null>(null);
  private undoStack: (() => void)[] = [];
  private readonly cancelUpload$ = new Subject<void>();

  bind(host: FileActionsHost): void {
    this.host = host;
  }

  get canUndo(): boolean {
    return this.undoStack.length > 0;
  }

  selectedFiles(): FileItem[] {
    return this.host.files().filter(file => this.host.selectedIds().has(file.id));
  }

  // host.files() holds no folders, so a selected id missing from it is a folder.
  private foldersSkippedNotice(hint = 'Act on them one at a time.'): string {
    return this.host.selectedIds().size > this.selectedFiles().length ? `Folders were skipped. ${hint}` : '';
  }

  toClipboard(mode: 'copy' | 'cut'): void {
    const items = this.selectedFiles();
    this.actionError.set(this.foldersSkippedNotice('They cannot be copied or cut.'));
    if (items.length > 0) {
      this.clipboard.set({ mode, items, sourceFolderId: this.host.currentFolderId() });
    }
  }

  trashSelected(): void {
    const files = this.selectedFiles();
    if (files.length === 0) {
      this.actionError.set(this.foldersSkippedNotice());
      return;
    }
    this.bulk(files, file => this.filesApi.delete(file.id), name => `Unable to delete ${name}. Please try again.`, done => {
      const deleted = done.map(([file]) => file);
      this.pushUndo(() => this.bulk(deleted, file => this.filesApi.restore(file.id), name => `Unable to restore ${name}. Please try again.`));
    }, this.foldersSkippedNotice());
  }

  undo(): void {
    this.undoStack.pop()!();
  }

  copyItems(items: FileItem[], folderId: string | null): void {
    this.bulk(items, file => this.filesApi.copyFile(file.id, folderId), name => `Unable to copy ${name}. Please try again.`, done => {
      const copies = done.map(([file, copy]) => ({ id: copy.id, name: file.name }));
      this.pushUndo(() => this.bulk(copies, file => this.filesApi.delete(file.id), name => `Unable to undo the copy of ${name}. Please try again.`));
    });
  }

  paste(): boolean {
    const clip = this.clipboard()!;
    const folderId = this.host.currentFolderId();
    if (clip.mode === 'cut') {
      if (clip.sourceFolderId === folderId) {
        return false;
      }
      this.clipboard.set(null);
      this.host.move(clip.items, folderId, clip.sourceFolderId);
      return true;
    }
    this.copyItems(clip.items, folderId);
    return true;
  }

  pushUndo(undo: () => void): void {
    this.undoStack = [...this.undoStack, undo].slice(-20);
  }

  // Runs op for each item in order, reports failures through actionError, then reloads like upload does.
  bulk<R>(
    items: { id: string; name: string }[],
    op: (item: { id: string; name: string }) => Observable<R>,
    failure: (name: string) => string,
    done?: (succeeded: [{ id: string; name: string }, R][]) => void,
    notice = '',
  ): void {
    if (this.actionBusy()) {
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set(notice);
    const succeeded: [{ id: string; name: string }, R][] = [];

    from(items)
      .pipe(
        concatMap(item => op(item).pipe(
          tap(result => succeeded.push([item, result])),
          catchError(error => {
            this.actionError.set(fileErrorMessage(error, failure(item.name)));
            return EMPTY;
          }),
        )),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => {
          this.actionBusy.set(false);
          const error = this.actionError();
          this.host.reload();
          this.actionError.set(error);
          this.host.refreshStorage();
          if (succeeded.length > 0) {
            done?.(succeeded);
          }
        },
      });
  }

  // Stops the running upload; an open chunked session is aborted by FilesService.
  cancelUpload(): void {
    this.cancelUpload$.next();
  }

  uploadFiles(selected: File[], folderId: string | null = this.host.currentFolderId()): void {
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
            this.actionError.set(fileErrorMessage(error, `Unable to upload ${file.name}. Please try again.`));
            return EMPTY;
          }),
        )),
        takeUntil(this.cancelUpload$),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => {
          this.actionBusy.set(false);
          this.uploadState.set(null);
          const error = this.actionError();
          this.host.reload();
          this.actionError.set(error);
          this.host.refreshStorage();
        },
      });
  }
}
