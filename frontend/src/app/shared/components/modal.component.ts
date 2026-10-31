import { Component, HostListener, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div
      *ngIf="open()"
      class="fixed inset-0 z-50 flex items-center justify-center p-4"
    >
      <!-- Backdrop -->
      <div
        class="absolute inset-0 bg-black/40 backdrop-blur-xs transition-opacity"
        (click)="closeOnBackdrop() && close.emit()"
      ></div>

      <!-- Modal Card -->
      <div
        class="relative bg-white rounded-2xl shadow-2xl w-full border border-gray-100 flex flex-col max-h-[90vh] overflow-hidden transform transition-all"
        [ngClass]="maxWidthClasses[maxWidth()]"
      >
        <!-- Header -->
        <div *ngIf="title() || showHeader()" class="flex items-center justify-between px-6 pt-5 pb-4 border-b border-gray-100 flex-shrink-0">
          <div class="flex items-center gap-2.5">
            <app-icon *ngIf="icon()" [name]="icon()!" class="text-blue-600" [size]="22" />
            <div>
              <h2 class="text-base font-semibold text-gray-900 leading-tight">{{ title() }}</h2>
              <p *ngIf="subtitle()" class="text-xs text-gray-400 mt-0.5">{{ subtitle() }}</p>
            </div>
          </div>
          <button
            type="button"
            (click)="close.emit()"
            class="p-1.5 rounded-lg hover:bg-gray-100 text-gray-400 hover:text-gray-600 transition-colors cursor-pointer"
            title="Close"
          >
            <app-icon name="close" [size]="18" />
          </button>
        </div>

        <!-- Body -->
        <div class="p-6 overflow-y-auto flex-1">
          <ng-content></ng-content>
        </div>

        <!-- Footer -->
        <div *ngIf="hasFooter()" class="flex items-center justify-end gap-2 px-6 py-4 bg-gray-50 border-t border-gray-100 flex-shrink-0">
          <ng-content select="[footer]"></ng-content>
        </div>
      </div>
    </div>
  `,
})
export class ModalComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string>('');
  readonly subtitle = input<string>('');
  readonly icon = input<string | null>(null);
  readonly showHeader = input<boolean>(true);
  readonly hasFooter = input<boolean>(false);
  readonly closeOnBackdrop = input<boolean>(true);
  readonly maxWidth = input<'sm' | 'md' | 'lg' | 'xl' | '2xl'>('md');

  readonly close = output<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) {
      this.close.emit();
    }
  }

  readonly maxWidthClasses = {
    sm: 'max-w-sm',
    md: 'max-w-md',
    lg: 'max-w-lg',
    xl: 'max-w-xl',
    '2xl': 'max-w-2xl',
  };
}
