import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from './icon.component';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon';

@Component({
  selector: 'app-button',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <button
      [type]="type()"
      [disabled]="disabled() || loading()"
      (click)="onClick.emit($event)"
      [ngClass]="[baseClasses, variantClasses[variant()], sizeClasses[size()], customClass()]"
      [title]="title()"
    >
      <app-icon
        *ngIf="loading()"
        name="progress_activity"
        [size]="iconSize"
        class="animate-spin"
      />
      <app-icon
        *ngIf="!loading() && icon()"
        [name]="icon()!"
        [size]="iconSize"
        [filled]="iconFilled()"
      />
      <span *ngIf="label() || hasContent"><ng-content></ng-content>{{ label() }}</span>
      <app-icon
        *ngIf="!loading() && iconRight()"
        [name]="iconRight()!"
        [size]="iconSize"
      />
    </button>
  `,
})
export class ButtonComponent {
  readonly variant = input<ButtonVariant>('secondary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input<boolean>(false);
  readonly loading = input<boolean>(false);
  readonly icon = input<string | null>(null);
  readonly iconRight = input<string | null>(null);
  readonly iconFilled = input<boolean>(false);
  readonly label = input<string>('');
  readonly title = input<string>('');
  readonly customClass = input<string>('');

  readonly onClick = output<MouseEvent>();

  get hasContent(): boolean {
    return !!this.label();
  }

  get iconSize(): number {
    switch (this.size()) {
      case 'sm': return 16;
      case 'lg': return 22;
      case 'icon': return 18;
      default: return 18;
    }
  }

  readonly baseClasses = 'inline-flex items-center justify-center font-medium transition-all duration-150 rounded-xl cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 gap-1.5 select-none';

  readonly variantClasses: Record<ButtonVariant, string> = {
    primary: 'bg-blue-600 hover:bg-blue-700 active:bg-blue-800 text-white shadow-xs',
    secondary: 'bg-gray-100 hover:bg-gray-200 active:bg-gray-300 text-gray-700',
    outline: 'border border-gray-200 hover:bg-gray-50 active:bg-gray-100 text-gray-700 bg-white',
    ghost: 'text-gray-600 hover:bg-gray-100 active:bg-gray-200 hover:text-gray-900',
    danger: 'bg-red-50 hover:bg-red-100 text-red-600 active:bg-red-200 border border-red-200',
  };

  readonly sizeClasses: Record<ButtonSize, string> = {
    sm: 'px-2.5 py-1.5 text-xs',
    md: 'px-3.5 py-2 text-sm',
    lg: 'px-5 py-2.5 text-base',
    icon: 'p-2 aspect-square',
  };
}
