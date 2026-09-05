import { Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-quota-meter',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="rounded-xl bg-gray-50 border border-gray-100 p-3">
      <div class="flex items-center justify-between mb-2">
        <div class="flex items-center gap-2">
          <app-icon name="cloud" class="text-blue-600" [size]="16" />
          <span class="text-xs font-medium text-gray-700">Storage</span>
        </div>
        <span
          *ngIf="isWarning()"
          class="text-[10px] font-semibold px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-800"
        >
          {{ percentage() }}%
        </span>
      </div>

      <!-- Progress bar -->
      <div class="w-full h-1.5 bg-gray-200 rounded-full overflow-hidden mb-2">
        <div
          class="h-full rounded-full transition-all duration-300"
          [ngClass]="barColor()"
          [style.width.%]="percentage()"
        ></div>
      </div>

      <p class="text-xs text-gray-500">
        <span class="font-semibold text-gray-800">{{ formattedUsed() }}</span> of {{ formattedTotal() }} used
      </p>
    </div>
  `,
})
export class QuotaMeterComponent {
  readonly usedBytes = input<number>(0);
  readonly totalBytes = input<number>(5 * 1024 * 1024 * 1024); // 5GB default

  readonly percentage = computed(() => {
    if (this.totalBytes() <= 0) return 0;
    const p = Math.min(100, Math.round((this.usedBytes() / this.totalBytes()) * 100));
    return p;
  });

  readonly isWarning = computed(() => this.percentage() >= 80);

  readonly barColor = computed(() => {
    const p = this.percentage();
    if (p >= 95) return 'bg-red-600';
    if (p >= 80) return 'bg-amber-500';
    return 'bg-blue-600';
  });

  readonly formattedUsed = computed(() => this.formatBytes(this.usedBytes()));
  readonly formattedTotal = computed(() => this.formatBytes(this.totalBytes()));

  private formatBytes(bytes: number): string {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
  }
}
