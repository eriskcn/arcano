import { Component, input, output } from '@angular/core';
import { ModalComponent } from './modal.component';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [ModalComponent],
  template: `
    <app-modal [open]="open()" [title]="title()" maxWidth="sm" [hasFooter]="true" (close)="cancelled.emit()">
      <p class="text-sm text-gray-600">{{ message() }}</p>
      <ng-container footer>
        <button
          type="button"
          (click)="cancelled.emit()"
          class="px-4 py-2 rounded-xl text-sm font-medium text-gray-700 border border-gray-200 hover:bg-gray-100 transition-colors cursor-pointer"
        >
          Cancel
        </button>
        <button
          type="button"
          (click)="confirmed.emit()"
          class="px-4 py-2 rounded-xl text-sm font-semibold text-white transition-colors cursor-pointer"
          [class]="danger() ? 'bg-red-600 hover:bg-red-700' : 'bg-blue-600 hover:bg-blue-700'"
        >
          {{ confirmLabel() }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class ConfirmDialogComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string>('');
  readonly message = input<string>('');
  readonly confirmLabel = input<string>('Confirm');
  readonly danger = input<boolean>(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
}
