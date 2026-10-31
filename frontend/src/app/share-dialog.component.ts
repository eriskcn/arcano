import { CommonModule } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { errorBody } from './http-errors';
import { ModalComponent } from './shared/components/modal.component';
import { ShareLinkDto, ShareService } from './share.service';

@Component({
  selector: 'app-share-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent],
  template: `
    <app-modal [open]="open()" title="Share file" [subtitle]="fileName()" icon="share" (close)="close.emit()">
      <div class="space-y-4">
        <div *ngIf="shareError()" class="rounded-xl border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-700" role="alert">{{ shareError() }}</div>
        <div class="flex items-end gap-2">
          <label class="flex-1 text-xs font-medium text-gray-600">
            Link expires
            <select
              class="mt-1 w-full rounded-xl border border-gray-200 px-3 py-2 text-sm text-gray-800"
              [ngModel]="shareExpiryDays()"
              (ngModelChange)="shareExpiryDays.set($event)"
            >
              <option *ngFor="let o of shareExpiryOptions" [ngValue]="o.days">{{ o.label }}</option>
            </select>
          </label>
          <button
            type="button"
            (click)="createShareLink()"
            [disabled]="shareBusy()"
            class="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold cursor-pointer disabled:opacity-60 disabled:cursor-default"
          >
            Create link
          </button>
        </div>
        <p *ngIf="shareLinks().length === 0" class="text-xs text-gray-500">No active links.</p>
        <ul class="space-y-2">
          <li *ngFor="let link of shareLinks()" class="flex items-center gap-2 rounded-xl border border-gray-200 px-3 py-2">
            <div class="min-w-0 flex-1">
              <p class="text-xs text-gray-800 truncate">{{ shareLinkUrl(link.token) }}</p>
              <p class="text-[11px] text-gray-400">{{ link.expiresAtUtc ? 'Expires ' + (link.expiresAtUtc | date: 'medium') : 'Never expires' }}</p>
            </div>
            <button type="button" (click)="copyShareLink(link)" class="text-xs font-semibold text-blue-600 hover:text-blue-700 cursor-pointer">Copy</button>
            <button type="button" (click)="revokeShareLink(link)" class="text-xs font-semibold text-red-600 hover:text-red-700 cursor-pointer">Revoke</button>
          </li>
        </ul>
      </div>
    </app-modal>
  `,
})
export class ShareDialogComponent {
  private readonly shareApi = inject(ShareService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly fileId = input<string | null>(null);
  readonly fileName = input<string>('');
  readonly close = output<void>();

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

  constructor() {
    // Each time the dialog opens, load the links of the selected file.
    effect(() => {
      const id = this.fileId();
      if (this.open() && id) {
        untracked(() => this.load(id));
      }
    });
  }

  private load(id: string): void {
    this.shareLinks.set([]);
    this.shareError.set('');
    this.shareApi.list(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: links => this.shareLinks.set(links),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to load share links.')),
    });
  }

  createShareLink(): void {
    const id = this.fileId();
    if (!id || this.shareBusy()) {
      return;
    }
    const days = this.shareExpiryDays();
    const expiresAtUtc = days === null ? null : new Date(Date.now() + days * 86_400_000).toISOString();
    this.shareBusy.set(true);
    this.shareError.set('');
    this.shareApi.create(id, expiresAtUtc).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
    const body = errorBody(err);
    const text = [body?.description, body?.detail, body?.title, body?.message].find(v => typeof v === 'string' && v);
    return (typeof raw === 'string' && raw) || (text as string | undefined) || fallback;
  }
}
