import { CommonModule } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import type { FileItem, MenuEntry } from './app';

export interface ContextMenuModel {
  title: string;
  items: FileItem[];
  entries: MenuEntry[];
  x: number; // pointer position; the component clamps it into the viewport
  y: number;
}

@Component({
  selector: 'app-context-menu',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div
      class="fixed z-50 w-60 rounded-xl border border-gray-200 bg-white py-1 shadow-lg"
      [style.left.px]="position().left"
      [style.top.px]="position().top"
      role="menu"
      [attr.aria-label]="'Actions for ' + menu().title"
      (keydown)="onKey($event)"
      (click)="$event.stopPropagation()"
    >
      <ng-container *ngIf="view() === 'actions'">
        <ng-container *ngFor="let entry of menu().entries">
          <hr *ngIf="entry.divider" role="separator" class="my-1 border-gray-100" />
          <button
            type="button"
            role="menuitem"
            [attr.aria-disabled]="entry.disabled ? 'true' : null"
            (click)="activate(entry)"
            [ngClass]="[
              entry.danger ? 'text-red-600 hover:bg-red-50 focus:bg-red-50' : 'text-gray-700 hover:bg-gray-50 focus:bg-gray-50',
              entry.disabled ? 'opacity-40 cursor-default' : 'cursor-pointer'
            ]"
            class="flex h-9 w-full items-center gap-3 px-3 text-left text-sm outline-none"
          >
            <span class="material-symbols-rounded" style="font-size: 18px">{{ entry.icon }}</span>
            <span class="flex-1 truncate">{{ entry.label }}</span>
            <span *ngIf="entry.shortcut" class="text-xs text-gray-400">{{ entry.shortcut }}</span>
          </button>
        </ng-container>
      </ng-container>
      <ng-container *ngIf="view() === 'move'">
        <ng-container *ngFor="let targets of [visibleTargets()]">
          <p *ngIf="targets.length === 0" class="px-3 py-2 text-xs text-gray-400">No folders to move to</p>
          <button
            *ngFor="let target of targets"
            type="button"
            role="menuitem"
            (click)="move.emit({ items: menu().items, targetId: target.id })"
            class="flex h-9 w-full items-center gap-3 px-3 text-left text-sm text-gray-700 hover:bg-gray-50 focus:bg-gray-50 outline-none cursor-pointer"
          >
            <span class="material-symbols-rounded" style="font-size: 18px">folder</span>
            <span class="truncate">{{ target.name }}</span>
          </button>
        </ng-container>
      </ng-container>
    </div>
  `,
})
export class ContextMenuComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly menu = input.required<ContextMenuModel>();
  readonly moveTargets = input<{ id: string | null; name: string }[]>([]);
  // The parent closes the menu and runs the entry; submenu and disabled entries are handled here.
  readonly action = output<MenuEntry>();
  readonly move = output<{ items: FileItem[]; targetId: string | null }>();

  readonly view = signal<'actions' | 'move'>('actions');

  constructor() {
    // A new menu always starts on the actions list with focus inside.
    effect(() => {
      this.menu();
      untracked(() => {
        this.view.set('actions');
        this.focusFirst();
      });
    });
  }

  readonly position = computed(() => {
    const { entries, x, y } = this.menu();
    // Sizes mirror the Tailwind classes above: w-60, py-1, 36px rows, 9px separators.
    const menuWidth = 240;
    const menuHeight = 8 + entries.length * 36 + entries.filter(entry => entry.divider).length * 9;
    return {
      left: Math.max(8, Math.min(x, window.innerWidth - menuWidth - 8)),
      top: Math.max(8, Math.min(y, window.innerHeight - menuHeight - 8)),
    };
  });

  visibleTargets(): { id: string | null; name: string }[] {
    const items = this.menu().items;
    return this.moveTargets().filter(t => !items.some(i => i.id === t.id));
  }

  activate(entry: MenuEntry): void {
    if (entry.disabled) {
      return;
    }
    if (entry.submenu) {
      this.view.set('move');
      this.focusFirst();
      return;
    }
    this.action.emit(entry);
  }

  onKey(event: KeyboardEvent): void {
    const step = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
    if (!step || !(event.currentTarget instanceof HTMLElement)) {
      return;
    }
    event.preventDefault();
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('[role="menuitem"]:not([aria-disabled="true"])'));
    const at = buttons.indexOf(document.activeElement as HTMLElement);
    buttons.at(at < 0 ? (step > 0 ? 0 : -1) : (at + step + buttons.length) % buttons.length)?.focus();
  }

  private focusFirst(): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('[role="menu"] [role="menuitem"]:not([aria-disabled="true"])')?.focus());
  }
}
