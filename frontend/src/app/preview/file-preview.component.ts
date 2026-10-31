import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnDestroy, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subscription } from 'rxjs';
import { ButtonComponent } from '../shared/components/button.component';
import { FilesService } from '../files.service';
import { PreviewKind, previewKind } from './preview-kind';

const TEXT_PREVIEW_BYTES = 100 * 1024;

type PreviewState = 'loading' | 'error' | 'ready';

@Component({
  selector: 'app-file-preview',
  standalone: true,
  imports: [CommonModule, ButtonComponent],
  template: `
    <div class="w-full">
      <p *ngIf="state() === 'loading'" class="text-sm text-gray-500">Loading preview…</p>

      <div *ngIf="state() === 'error'" class="flex flex-col items-start gap-3">
        <p class="text-sm text-gray-500">Preview unavailable.</p>
        <app-button variant="outline" size="sm" icon="download" label="Download" (onClick)="download.emit()" />
      </div>

      <ng-container *ngIf="state() === 'ready'">
        <ng-container *ngIf="objectUrl() as url">
          <img *ngIf="kind() === 'image'" [src]="url" [alt]="fileName()" class="max-w-full h-auto" />
          <audio *ngIf="kind() === 'media' && isAudio()" [src]="url" controls class="w-full"></audio>
          <video *ngIf="kind() === 'media' && !isAudio()" [src]="url" controls class="max-w-full"></video>
        </ng-container>

        <ng-container *ngIf="kind() === 'text'">
          <pre class="overflow-x-auto text-xs text-gray-800 bg-gray-50 border border-gray-200 rounded-xl p-3">{{ text() }}</pre>
          <p *ngIf="truncated()" class="mt-2 text-xs text-gray-500">
            Showing the first 100 KB. Download to see the rest.
          </p>
        </ng-container>
      </ng-container>
    </div>
  `,
})
export class FilePreviewComponent implements OnDestroy {
  private readonly files = inject(FilesService);
  private readonly destroyRef = inject(DestroyRef);

  readonly fileId = input<string>('');
  // Optional override for sources other than an owned file (e.g. a public share).
  readonly loader = input<(() => Observable<Blob>) | null>(null);
  readonly contentType = input.required<string>();
  readonly fileSize = input.required<number>();
  readonly fileName = input.required<string>();
  readonly download = output<void>();

  readonly kind = computed<PreviewKind>(() => previewKind(this.contentType(), this.fileSize()));
  readonly isAudio = computed(() => this.contentType().toLowerCase().startsWith('audio/'));

  readonly state = signal<PreviewState>('loading');
  readonly objectUrl = signal<string | null>(null);
  readonly text = signal<string>('');
  readonly truncated = signal<boolean>(false);

  private subscription?: Subscription;
  private loadToken = 0;

  constructor() {
    effect(() => {
      const id = this.fileId();
      const kind = this.kind();
      const loader = this.loader();
      untracked(() => this.load(id, kind, loader));
    });
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.releaseObjectUrl();
  }

  private load(id: string, kind: PreviewKind, loader: (() => Observable<Blob>) | null): void {
    this.subscription?.unsubscribe();
    const token = ++this.loadToken;
    this.releaseObjectUrl();
    this.text.set('');
    this.truncated.set(false);

    if (kind === 'none') {
      this.state.set('error');
      return;
    }

    this.state.set('loading');
    this.subscription = (loader ? loader() : this.files.download(id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => void this.show(blob, kind, token),
        error: () => {
          if (token === this.loadToken) this.state.set('error');
        },
      });
  }

  private async show(blob: Blob, kind: PreviewKind, token: number): Promise<void> {
    try {
      if (kind === 'text') {
        const text = await blob.slice(0, TEXT_PREVIEW_BYTES).text();
        if (token !== this.loadToken) return;
        this.text.set(text);
        this.truncated.set(blob.size > TEXT_PREVIEW_BYTES);
      } else {
        this.objectUrl.set(URL.createObjectURL(blob));
      }
      this.state.set('ready');
    } catch {
      if (token === this.loadToken) this.state.set('error');
    }
  }

  private releaseObjectUrl(): void {
    const url = this.objectUrl();
    if (url) {
      URL.revokeObjectURL(url);
      this.objectUrl.set(null);
    }
  }
}
