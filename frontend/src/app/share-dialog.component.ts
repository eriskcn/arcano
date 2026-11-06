import { CommonModule } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { errorBody } from './http-errors';
import { ModalComponent } from './shared/components/modal.component';
import { ShareLinkDto, ShareService } from './share.service';
import { UserShareDto, UserSharesService } from './user-shares.service';

@Component({
  selector: 'app-share-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent],
  template: `
    <app-modal [open]="open()" title="Share" [subtitle]="fileName()" icon="share" (close)="close.emit()">
      <div class="space-y-4">
        <div *ngIf="shareError()" class="rounded-xl border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-700" role="alert">{{ shareError() }}</div>
        <section class="space-y-2">
          <h3 class="text-xs font-semibold text-gray-700">People</h3>
          <form class="flex items-center gap-2" (ngSubmit)="sharePerson()">
            <input
              type="email"
              name="email"
              aria-label="Email address"
              placeholder="Email address"
              [ngModel]="personEmail()"
              (ngModelChange)="personEmail.set($event)"
              class="min-w-0 flex-1 rounded-xl border border-gray-200 px-3 py-2 text-sm text-gray-800"
            />
            <button
              type="submit"
              [disabled]="personBusy() || !personEmail().trim()"
              class="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold cursor-pointer disabled:opacity-60 disabled:cursor-default"
            >
              Share
            </button>
          </form>
          <p *ngIf="people().length === 0" class="text-xs text-gray-500">Not shared with anyone.</p>
          <ul class="space-y-2">
            <li *ngFor="let person of people()" class="flex items-center gap-2 rounded-xl border border-gray-200 px-3 py-2">
              <p class="min-w-0 flex-1 text-xs text-gray-800 truncate">{{ person.recipientEmail }}</p>
              <button type="button" (click)="removePerson(person)" class="text-xs font-semibold text-red-600 hover:text-red-700 cursor-pointer">Remove</button>
            </li>
          </ul>
        </section>
        <ng-container *ngIf="fileId()">
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
            <button type="button" (click)="copyShareLink(link)" class="text-xs font-semibold text-blue-600 hover:text-blue-700 cursor-pointer">{{ copiedToken() === link.token ? 'Copied' : 'Copy' }}</button>
            <button type="button" (click)="revokeShareLink(link)" class="text-xs font-semibold text-red-600 hover:text-red-700 cursor-pointer">Revoke</button>
          </li>
        </ul>
        </ng-container>
      </div>
    </app-modal>
  `,
})
export class ShareDialogComponent {
  private readonly shareApi = inject(ShareService);
  private readonly userShares = inject(UserSharesService);
  private readonly destroyRef = inject(DestroyRef);

  readonly open = input.required<boolean>();
  readonly fileId = input<string | null>(null);
  readonly folderId = input<string | null>(null);
  readonly fileName = input<string>('');
  readonly close = output<void>();

  readonly people = signal<UserShareDto[]>([]);
  readonly personEmail = signal<string>('');
  readonly personBusy = signal<boolean>(false);
  readonly shareLinks = signal<ShareLinkDto[]>([]);
  readonly shareExpiryDays = signal<number | null>(null);
  readonly shareBusy = signal<boolean>(false);
  readonly shareError = signal<string>('');
  readonly copiedToken = signal<string | null>(null);
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
      const folderId = this.folderId();
      if (this.open() && (id || folderId)) {
        untracked(() => this.load(id, folderId));
      }
    });
  }

  private load(id: string | null, folderId: string | null): void {
    this.shareLinks.set([]);
    this.people.set([]);
    this.personEmail.set('');
    this.shareError.set('');
    this.copiedToken.set(null);
    this.userShares.list(this.target()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: people => this.people.set(people),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to load people.')),
    });
    if (!id) return;
    this.shareApi.list(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: links => this.shareLinks.set(links),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to load share links.')),
    });
  }

  private target(): { fileId?: string; folderId?: string } {
    return this.fileId() ? { fileId: this.fileId()! } : { folderId: this.folderId()! };
  }

  sharePerson(): void {
    const email = this.personEmail().trim();
    if (!email || this.personBusy()) {
      return;
    }
    this.personBusy.set(true);
    this.shareError.set('');
    this.userShares.create(this.target(), email).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: share => {
        // Sharing again returns the same share, so do not list it twice.
        this.people.update(people => people.some(p => p.id === share.id) ? people : [...people, share]);
        this.personEmail.set('');
        this.personBusy.set(false);
      },
      error: err => {
        this.personBusy.set(false);
        const unknown = err instanceof HttpErrorResponse && err.status === 404 ? 'No account found with that email.' : '';
        this.shareError.set(this.shareErrorMessage(err, unknown || 'Unable to share.'));
      },
    });
  }

  removePerson(person: UserShareDto): void {
    this.shareError.set('');
    this.userShares.remove(person.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.people.update(people => people.filter(p => p.id !== person.id)),
      error: err => this.shareError.set(this.shareErrorMessage(err, 'Unable to remove access.')),
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

  // navigator.clipboard is undefined over plain HTTP; the await throws and lands in catch.
  async copyShareLink(link: ShareLinkDto): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.shareApi.linkFor(link.token));
      this.copiedToken.set(link.token);
      this.shareError.set('');
    } catch {
      this.copiedToken.set(null);
      this.shareError.set('Copy failed. Select the link and copy it manually.');
    }
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
