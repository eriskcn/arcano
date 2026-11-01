import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { AdminKey, AdminService, AdminUser, CreatedKey, NewKey } from './admin.service';
import { AuthService } from './auth.service';
import { errorBody } from './http-errors';
import { ConfirmDialogComponent } from './shared/components/confirm-dialog.component';
import { ModalComponent } from './shared/components/modal.component';
import { FileSizePipe } from './shared/pipes/file-size.pipe';

const GB = 1024 ** 3;

@Component({
  selector: 'app-admin-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent, ConfirmDialogComponent, FileSizePipe],
  template: `
    <app-modal [open]="open()" title="Admin" icon="admin_panel_settings" maxWidth="2xl" (close)="close.emit()">
      <div class="space-y-6">
        <div *ngIf="error()" class="rounded-xl border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-700" role="alert">{{ error() }}</div>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-gray-900">Members</h3>
          <ul class="divide-y divide-gray-100 rounded-xl border border-gray-100">
            <li *ngFor="let user of users()" class="p-3 space-y-2" data-member>
              <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
                <span class="text-sm font-medium text-gray-900 break-all">{{ user.email }}</span>
                <span *ngIf="user.isAdmin" class="text-[10px] font-semibold uppercase rounded-full bg-blue-50 text-blue-700 px-2 py-0.5">Admin</span>
                <span *ngIf="user.isDisabled" class="text-[10px] font-semibold uppercase rounded-full bg-red-50 text-red-700 px-2 py-0.5" data-disabled>Disabled</span>
              </div>
              <p class="text-xs text-gray-500">
                {{ user.usedBytes | fileSize }} of {{ user.effectiveQuotaBytes | fileSize }}
                <span *ngIf="user.quotaBytes === null">(server default)</span>
              </p>
              <form *ngIf="editingId() === user.id" class="flex flex-wrap items-center gap-2" (ngSubmit)="saveQuota(user)">
                <input type="number" min="0" step="any" name="quota" placeholder="GB (empty = server default)" aria-label="Quota in GB" class="flex-1 min-w-0 rounded-xl border border-gray-200 px-3 py-1.5 text-sm" [ngModel]="quotaInput()" (ngModelChange)="quotaInput.set($event)" (input)="quotaBad.set($any($event.target).validity.badInput)" />
                <button type="submit" [disabled]="busy() || quotaInvalid()" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Save</button>
                <button type="button" (click)="editingId.set(null)" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Cancel</button>
                <p *ngIf="quotaInvalid()" class="w-full text-xs text-red-600" data-quota-error>Enter a quota of 0 or more GB, or leave it empty.</p>
              </form>
              <p *ngIf="isSelf(user)" class="text-xs font-medium text-amber-700" data-self-warning>This is your account. Disabling or deleting it will lock you out.</p>
              <div class="flex flex-wrap gap-2">
                <button type="button" (click)="editQuota(user)" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Edit quota</button>
                <button type="button" (click)="toggleDisabled(user)" [disabled]="busy()" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">{{ user.isDisabled ? 'Enable' : 'Disable' }}</button>
                <button type="button" (click)="createKey({ type: 'Reset', userId: user.id })" [disabled]="busy()" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Reset key</button>
                <button type="button" (click)="pendingDelete.set(user)" class="px-3 py-1.5 rounded-lg border border-red-200 text-xs font-semibold text-red-600 hover:bg-red-50 cursor-pointer">Delete</button>
              </div>
            </li>
          </ul>
        </section>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-gray-900">Invite keys</h3>
          <form class="grid grid-cols-2 gap-2 sm:grid-cols-3" (ngSubmit)="createJoinKey()">
            <input type="number" min="0" step="any" name="joinQuota" placeholder="Quota (GB)" aria-label="Quota in GB" class="w-full rounded-xl border border-gray-200 px-3 py-2 text-sm" [ngModel]="joinQuota()" (ngModelChange)="joinQuota.set($event)" (input)="joinQuotaBad.set($any($event.target).validity.badInput)" />
            <input type="number" min="1" max="30" step="1" name="joinDays" placeholder="Expires in (days)" aria-label="Expiry in days" class="w-full rounded-xl border border-gray-200 px-3 py-2 text-sm" [ngModel]="joinDays()" (ngModelChange)="joinDays.set($event)" (input)="joinDaysBad.set($any($event.target).validity.badInput)" />
            <button type="submit" [disabled]="busy() || joinQuotaInvalid() || joinDaysInvalid()" class="col-span-2 sm:col-span-1 px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold cursor-pointer disabled:opacity-60 disabled:cursor-default">Generate join key</button>
          </form>
          <p class="text-xs text-gray-500">Empty quota = server default. Empty expiry = 7 days (1 to 30 allowed).</p>
          <p *ngIf="joinQuotaInvalid()" class="text-xs text-red-600" data-join-quota-error>Enter a quota of 0 or more GB, or leave it empty.</p>
          <p *ngIf="joinDaysInvalid()" class="text-xs text-red-600" data-join-days-error>Expiry must be a whole number of days from 1 to 30.</p>

          <div *ngIf="newKey() as created" class="rounded-xl border border-amber-200 bg-amber-50 p-3 space-y-2" data-new-key>
            <p class="text-xs font-medium text-amber-800">New {{ created.type.toLowerCase() }} key. Copy it now: it cannot be shown again.</p>
            <div class="flex gap-2">
              <input readonly aria-label="New key" class="flex-1 min-w-0 rounded-xl border border-amber-200 bg-white px-3 py-2 text-xs font-mono" [value]="created.key" (focus)="$any($event.target).select()" />
              <button type="button" (click)="copyKey(created.key)" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">{{ copied() ? 'Copied' : 'Copy' }}</button>
            </div>
            <p *ngIf="copyFailed()" class="text-xs text-amber-800">Copy failed. Select the key and copy it manually.</p>
          </div>

          <ul class="divide-y divide-gray-100 rounded-xl border border-gray-100" *ngIf="keys().length">
            <li *ngFor="let key of keys()" class="p-3 flex flex-wrap items-center justify-between gap-2" data-key>
              <div class="text-xs text-gray-600 min-w-0">
                <span class="font-semibold text-gray-900">{{ key.type }}</span>
                <span *ngIf="key.userEmail"> for <span class="break-all">{{ key.userEmail }}</span></span>
                <span *ngIf="key.quotaBytes !== null"> · {{ key.quotaBytes | fileSize }}</span>
                <span *ngIf="key.expiresAtUtc"> · expires {{ key.expiresAtUtc | date: 'mediumDate' }}</span>
              </div>
              <button type="button" (click)="revoke(key)" [disabled]="busy()" class="px-3 py-1.5 rounded-lg border border-gray-200 text-xs font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Revoke</button>
            </li>
          </ul>
          <p *ngIf="!keys().length" class="text-xs text-gray-500">No open keys.</p>
        </section>
      </div>
    </app-modal>

    <app-confirm-dialog
      [open]="!!pendingDelete()"
      title="Delete member"
      [message]="'Delete ' + (pendingDelete()?.email ?? '') + ' and all their files? This cannot be undone.' + (pendingDelete() && isSelf(pendingDelete()!) ? ' This is your own account: you will be signed out.' : '')"
      confirmLabel="Delete"
      [danger]="true"
      (confirmed)="confirmDelete()"
      (cancelled)="pendingDelete.set(null)"
    />
  `,
})
export class AdminDialogComponent {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private usersSeq = 0;
  private keysSeq = 0;

  readonly open = input.required<boolean>();
  readonly close = output<void>();

  readonly users = signal<AdminUser[]>([]);
  readonly keys = signal<AdminKey[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly quotaInput = signal<number | string | null>(null);
  readonly pendingDelete = signal<AdminUser | null>(null);
  readonly joinQuota = signal<number | string | null>(null);
  readonly joinDays = signal<number | string | null>(null);
  readonly newKey = signal<CreatedKey | null>(null);
  readonly copied = signal(false);
  readonly copyFailed = signal(false);
  // The number input yields null for unparsable text, so the browser's badInput flag is tracked separately.
  readonly quotaBad = signal(false);
  readonly joinQuotaBad = signal(false);
  readonly joinDaysBad = signal(false);
  readonly quotaInvalid = computed(() => this.quotaBad() || this.gbToBytes(this.quotaInput()) === undefined);
  readonly joinQuotaInvalid = computed(() => this.joinQuotaBad() || this.gbToBytes(this.joinQuota()) === undefined);
  readonly joinDaysInvalid = computed(() => {
    const value = this.joinDays();
    if (this.joinDaysBad()) {
      return true;
    }
    if (value === null || value === '') {
      return false;
    }
    const days = Number(value);
    return !(Number.isInteger(days) && days >= 1 && days <= 30);
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        this.newKey.set(null);
        this.error.set('');
        this.load();
      } else {
        this.newKey.set(null);
      }
    });
  }

  editQuota(user: AdminUser): void {
    this.quotaInput.set(user.quotaBytes === null ? null : user.quotaBytes / GB);
    this.quotaBad.set(false);
    this.editingId.set(user.id);
  }

  saveQuota(user: AdminUser): void {
    const gb = this.gbToBytes(this.quotaInput());
    if (gb === undefined || this.quotaBad()) {
      return;
    }
    this.run(this.admin.setQuota(user.id, gb), () => this.editingId.set(null));
  }

  toggleDisabled(user: AdminUser): void {
    this.run(user.isDisabled ? this.admin.enable(user.id) : this.admin.disable(user.id));
  }

  confirmDelete(): void {
    const user = this.pendingDelete();
    this.pendingDelete.set(null);
    if (user) {
      this.run(this.admin.deleteUser(user.id));
    }
  }

  createJoinKey(): void {
    const quotaBytes = this.gbToBytes(this.joinQuota());
    const days = this.joinDays() === null || this.joinDays() === '' ? null : Number(this.joinDays());
    if (this.joinQuotaInvalid() || this.joinDaysInvalid()) {
      return;
    }
    this.createKey({ type: 'Join', quotaBytes, expiresInDays: days });
  }

  createKey(request: NewKey): void {
    this.error.set('');
    this.busy.set(true);
    this.admin.createKey(request).subscribe({
      next: created => {
        this.busy.set(false);
        this.copied.set(false);
        this.copyFailed.set(false);
        this.newKey.set(created);
        this.loadKeys();
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  revoke(key: AdminKey): void {
    this.run(this.admin.revokeKey(key.id));
  }

  isSelf(user: AdminUser): boolean {
    return user.email.toLowerCase() === this.auth.email()?.toLowerCase();
  }

  async copyKey(key: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(key);
      this.copied.set(true);
      this.copyFailed.set(false);
    } catch {
      this.copyFailed.set(true);
    }
  }

  // undefined = invalid, null = empty (server default), otherwise bytes.
  private gbToBytes(value: number | string | null): number | null | undefined {
    if (value === null || value === '') {
      return null;
    }
    const gb = Number(value);
    return Number.isFinite(gb) && gb >= 0 ? Math.round(gb * GB) : undefined;
  }

  private load(): void {
    const seq = ++this.usersSeq;
    this.admin.users().subscribe({
      next: users => seq === this.usersSeq && this.users.set(users),
      error: (error: unknown) => seq === this.usersSeq && this.fail(error),
    });
    this.loadKeys();
  }

  private loadKeys(): void {
    const seq = ++this.keysSeq;
    this.admin.keys().subscribe({
      next: keys => seq === this.keysSeq && this.keys.set(keys),
      error: (error: unknown) => seq === this.keysSeq && this.fail(error),
    });
  }

  private run(request: Observable<void>, onDone: () => void = () => {}): void {
    this.error.set('');
    this.busy.set(true);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        onDone();
        this.load();
      },
      error: (error: unknown) => {
        this.fail(error);
        // The row may be stale (e.g. already deleted), so refresh and keep the error visible.
        this.load();
      },
    });
  }

  private fail(error: unknown): void {
    this.busy.set(false);
    const body = errorBody(error);
    this.error.set(
      body?.code === 'Admin.LastAdmin'
        ? 'The last admin cannot be deleted or disabled.'
        : typeof body?.message === 'string' && body.message
          ? body.message
          : 'Something went wrong. Please try again.',
    );
  }
}
