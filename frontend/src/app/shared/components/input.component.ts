import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-input',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="w-full">
      <label *ngIf="label()" class="block text-sm font-medium text-gray-700 mb-1.5">
        {{ label() }}
      </label>
      <div class="relative flex items-center">
        <span *ngIf="icon()" class="absolute left-3 text-gray-400 pointer-events-none flex items-center">
          <app-icon [name]="icon()!" [size]="18" />
        </span>
        <input
          [type]="actualType"
          [placeholder]="placeholder()"
          [value]="value()"
          [disabled]="disabled()"
          (input)="onInput($event)"
          (keydown)="onKeyDown($event)"
          [ngClass]="[
            'w-full py-2.5 text-sm bg-white border rounded-xl outline-none transition-all placeholder:text-gray-400 text-gray-900',
            icon() ? 'pl-9.5' : 'pl-3.5',
            isPassword() || clearable() ? 'pr-10' : 'pr-3.5',
            error() ? 'border-red-300 focus:border-red-500 focus:ring-2 focus:ring-red-100' : 'border-gray-200 focus:border-blue-400 focus:ring-2 focus:ring-blue-100',
            disabled() ? 'bg-gray-50 text-gray-400 cursor-not-allowed' : ''
          ]"
        />
        <button
          *ngIf="isPassword()"
          type="button"
          (click)="showPassword = !showPassword"
          class="absolute right-3 text-gray-400 hover:text-gray-600 transition-colors cursor-pointer"
        >
          <app-icon [name]="showPassword ? 'visibility_off' : 'visibility'" [size]="18" />
        </button>
        <button
          *ngIf="clearable() && value() && !isPassword()"
          type="button"
          (click)="valueChange.emit('')"
          class="absolute right-3 text-gray-400 hover:text-gray-600 transition-colors cursor-pointer"
        >
          <app-icon name="close" [size]="16" />
        </button>
      </div>
      <p *ngIf="error()" class="mt-1.5 text-xs text-red-600 font-medium flex items-center gap-1">
        <app-icon name="error" [size]="14" />
        {{ error() }}
      </p>
      <p *ngIf="!error() && hint()" class="mt-1 text-xs text-gray-400">
        {{ hint() }}
      </p>
    </div>
  `,
})
export class InputComponent {
  readonly label = input<string>('');
  readonly placeholder = input<string>('');
  readonly type = input<string>('text');
  readonly value = input<string>('');
  readonly icon = input<string | null>(null);
  readonly error = input<string>('');
  readonly hint = input<string>('');
  readonly disabled = input<boolean>(false);
  readonly clearable = input<boolean>(false);

  readonly valueChange = output<string>();
  readonly enter = output<string>();

  showPassword = false;

  isPassword(): boolean {
    return this.type() === 'password';
  }

  get actualType(): string {
    if (this.isPassword()) {
      return this.showPassword ? 'text' : 'password';
    }
    return this.type();
  }

  onInput(e: Event): void {
    const target = e.target as HTMLInputElement;
    this.valueChange.emit(target.value);
  }

  onKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Enter') {
      this.enter.emit(this.value());
    }
  }
}
