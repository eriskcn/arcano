import { CommonModule } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';
import { errorBody } from './http-errors';
import { ModalComponent } from './shared/components/modal.component';

const MIN_PASSWORD_LENGTH = 8;

@Component({
  selector: 'app-account-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent],
  template: `
    <app-modal [open]="open()" title="Settings" icon="settings" (close)="close.emit()">
      <div class="space-y-4">
        <div *ngIf="error()" class="rounded-xl border border-red-100 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-700" role="alert">{{ error() }}</div>
        <div *ngIf="notice()" class="rounded-xl border border-green-100 bg-green-50 px-3 py-2.5 text-xs font-medium text-green-700" role="status">{{ notice() }}</div>

        <form class="rounded-xl border border-gray-200 p-4 space-y-3" (ngSubmit)="changePassword()">
          <div>
            <h3 class="text-sm font-semibold text-gray-900">Change password</h3>
            <p class="text-xs text-gray-500 mt-0.5">Changing your password signs out your other sessions.</p>
          </div>
          <label class="block space-y-1">
            <span class="text-xs font-medium text-gray-600">Current password</span>
            <input type="password" name="current" autocomplete="current-password" aria-label="Current password" [class]="inputClass" [ngModel]="currentPassword()" (ngModelChange)="currentPassword.set($event)" />
          </label>
          <label class="block space-y-1">
            <span class="text-xs font-medium text-gray-600">New password</span>
            <input type="password" name="new" autocomplete="new-password" placeholder="8+ characters" aria-label="New password (8+ characters)" [class]="inputClass" [ngModel]="newPassword()" (ngModelChange)="newPassword.set($event)" />
          </label>
          <label class="block space-y-1">
            <span class="text-xs font-medium text-gray-600">Confirm new password</span>
            <input type="password" name="confirm" autocomplete="new-password" aria-label="Confirm new password" [class]="inputClass" [ngModel]="confirmPassword()" (ngModelChange)="confirmPassword.set($event)" />
          </label>
          <div class="flex justify-end">
            <button type="submit" [disabled]="busy()" class="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-semibold cursor-pointer disabled:opacity-60 disabled:cursor-default">Change password</button>
          </div>
        </form>

        <div class="rounded-xl border border-gray-200 p-4 flex items-center justify-between gap-4">
          <div class="min-w-0">
            <h3 class="text-sm font-semibold text-gray-900">Sessions</h3>
            <p class="text-xs text-gray-500 mt-0.5">Sign out of this device and every other one.</p>
          </div>
          <button type="button" (click)="signOutEverywhere()" [disabled]="busy()" class="flex-shrink-0 px-4 py-2 rounded-xl border border-gray-200 text-sm font-semibold text-gray-700 hover:bg-gray-50 cursor-pointer disabled:opacity-60 disabled:cursor-default">Sign out everywhere</button>
        </div>

        <div class="rounded-xl border border-red-200 bg-red-50/50 p-4 space-y-3">
          <div>
            <h3 class="text-sm font-semibold text-red-700">Delete account</h3>
            <p class="text-xs text-red-700/80 mt-0.5">Permanently deletes your account and all your files, folders and shared links, including the Trash. It cannot be undone.</p>
          </div>
          <button *ngIf="!confirmingDelete()" type="button" (click)="confirmingDelete.set(true)" class="px-4 py-2 rounded-xl border border-red-200 bg-white text-sm font-semibold text-red-600 hover:bg-red-50 cursor-pointer">Delete account</button>
          <form *ngIf="confirmingDelete()" class="space-y-3" (ngSubmit)="deleteAccount()">
            <input type="password" name="delete" autocomplete="current-password" placeholder="Enter your password" aria-label="Enter your password" [class]="inputClass" [ngModel]="deletePassword()" (ngModelChange)="deletePassword.set($event)" />
            <button type="submit" [disabled]="busy()" class="px-4 py-2 rounded-xl bg-red-600 hover:bg-red-700 text-white text-sm font-semibold cursor-pointer disabled:opacity-60 disabled:cursor-default">Delete everything permanently</button>
          </form>
        </div>
      </div>
    </app-modal>
  `,
})
export class AccountDialogComponent {
  private readonly auth = inject(AuthService);

  protected readonly inputClass = 'w-full rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-100 transition-all placeholder:text-gray-400';

  readonly open = input.required<boolean>();
  readonly close = output<void>();
  readonly signedOut = output<void>();

  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly confirmPassword = signal('');
  readonly deletePassword = signal('');
  readonly confirmingDelete = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');

  changePassword(): void {
    this.error.set('');
    this.notice.set('');
    if (!this.currentPassword()) {
      this.error.set('Enter your current password.');
    } else if (this.newPassword().length < MIN_PASSWORD_LENGTH) {
      this.error.set(`The new password must be at least ${MIN_PASSWORD_LENGTH} characters.`);
    } else if (this.newPassword() !== this.confirmPassword()) {
      this.error.set('The new passwords do not match.');
    } else {
      this.run(this.auth.changePassword(this.currentPassword(), this.newPassword()), () => {
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.notice.set('Password changed. Other sessions were signed out.');
      }, 'Unable to change the password. Please try again.');
    }
  }

  signOutEverywhere(): void {
    this.run(this.auth.logoutEverywhere(), () => this.signedOut.emit(), 'Unable to sign out. Please try again.');
  }

  deleteAccount(): void {
    this.error.set('');
    if (!this.deletePassword()) {
      this.error.set('Enter your password to delete the account.');
      return;
    }
    this.run(this.auth.deleteAccount(this.deletePassword()), () => this.signedOut.emit(), 'Unable to delete the account. Please try again.');
  }

  private run(request: Observable<void>, onDone: () => void, fallback: string): void {
    this.busy.set(true);
    this.error.set('');
    request.subscribe({
      next: () => {
        this.busy.set(false);
        onDone();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const message = errorBody(error)?.message;
        this.error.set(typeof message === 'string' && message ? message : fallback);
      },
    });
  }
}
