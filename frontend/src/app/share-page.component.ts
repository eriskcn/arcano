import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { FilePreviewComponent } from './preview/file-preview.component';
import { ButtonComponent } from './shared/components/button.component';
import { FileSizePipe } from './shared/pipes/file-size.pipe';
import { PublicShareInfo, ShareService } from './share.service';

@Component({
  selector: 'app-share-page',
  standalone: true,
  imports: [CommonModule, ButtonComponent, FilePreviewComponent, FileSizePipe],
  template: `
    <main class="max-w-2xl mx-auto p-6 flex flex-col gap-4">
      <p *ngIf="state() === 'loading'" class="text-sm text-gray-500">Loading…</p>
      <p *ngIf="state() === 'notFound'" class="text-sm text-gray-700">This link is invalid or has expired.</p>

      <ng-container *ngIf="info() as file">
        <div class="flex items-center justify-between gap-4">
          <div class="min-w-0">
            <h1 class="text-lg font-semibold truncate">{{ file.fileName }}</h1>
            <p class="text-sm text-gray-500">{{ file.fileSize | fileSize }}</p>
          </div>
          <app-button variant="primary" icon="download" label="Download" (onClick)="download()" />
        </div>
        <app-file-preview
          [loader]="loader"
          [contentType]="file.contentType"
          [fileSize]="file.fileSize"
          [fileName]="file.fileName"
          (download)="download()"
        />
      </ng-container>
    </main>
  `,
})
export class SharePageComponent {
  private readonly shares = inject(ShareService);
  private readonly token = inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '';

  readonly state = signal<'loading' | 'notFound' | 'ready'>('loading');
  readonly info = signal<PublicShareInfo | null>(null);
  readonly loader = () => this.shares.publicDownload(this.token);

  constructor() {
    this.shares
      .publicInfo(this.token)
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe({
        next: info => {
          this.info.set(info);
          this.state.set('ready');
        },
        error: () => this.state.set('notFound'),
      });
  }

  download(): void {
    const name = this.info()?.fileName ?? 'download';
    this.loader().subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = name;
      a.click();
      URL.revokeObjectURL(url);
    });
  }
}
