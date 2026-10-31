import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { App, FileItem } from './app';
import { FilePreviewComponent } from './preview/file-preview.component';
import { environment } from '../environments/environment';

const sampleFiles: FileItem[] = ['1', '2', '3', '4', '5', '6'].map(id => ({
  id,
  name: `File ${id}.txt`,
  type: 'document',
  size: '1 KB',
  modified: 'Jul 22, 2026',
  starred: false,
  shared: false,
}));

const folderItem = (id: string, name: string): FileItem => ({
  id,
  name,
  type: 'folder',
  size: null,
  modified: 'Jul 22, 2026',
  starred: false,
  shared: false,
});

// jsdom has no DragEvent or DataTransfer, so drag events are plain objects with the fields the handlers read.
const dragEvent = (files: File[] = []): DragEvent =>
  ({
    preventDefault: () => {},
    stopPropagation: () => {},
    dataTransfer: { files, types: files.length ? ['Files'] : [], setData: () => {} },
  }) as unknown as DragEvent;

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render welcome back heading', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Welcome back');
  });

  it('should derive the profile initial from the signed-in email', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    expect(app.userInitial()).toBe('?');
    app.userEmail.set('person@arcano.test');
    expect(app.userInitial()).toBe('P');
  });

  it('should show a file context menu within the viewport', async () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    app.files.set(sampleFiles);
    const file = app.files()[0];
    fixture.detectChanges();
    const event = new MouseEvent('contextmenu', {
      clientX: window.innerWidth,
      clientY: window.innerHeight,
      cancelable: true,
    });

    app.page.set('app');
    app.showContextMenu(file, event);
    fixture.detectChanges();
    await fixture.whenStable();

    const menu = fixture.nativeElement.querySelector('[role="menu"]') as HTMLElement | null;
    expect(event.defaultPrevented).toBe(true);
    expect(menu?.textContent).toContain('Show details');
    expect(menu?.style.left).toBe(`${Math.max(8, window.innerWidth - 200)}px`);
    expect(menu?.style.top).toBe(`${Math.max(8, window.innerHeight - 88)}px`);
  });

  it('should star a file through the API and revert when it fails', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);

    app.toggleFileStar('1');
    expect(app.files()[0].starred).toBe(true);
    const star = http.expectOne(`${environment.apiBaseUrl}/Files/1/star`);
    expect([star.request.method, star.request.body]).toEqual(['PUT', { isStarred: true }]);
    star.flush(null, { status: 204, statusText: 'No Content' });
    expect(app.files()[0].starred).toBe(true);

    app.toggleFileStar('1');
    http.expectOne(`${environment.apiBaseUrl}/Files/1/star`).flush(null, { status: 500, statusText: 'Server Error' });
    expect(app.files()[0].starred).toBe(true);
    expect(app.actionError()).toContain('Unable to update favorites');
    http.verify();
  });

  it('should load the starred view for Favorites and the recent view for Recent', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);

    app.selectNav('favorites');
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&view=starred`).flush([
      { id: 's', fileName: 's.txt', contentType: 'text/plain', fileSize: 1, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: true, lastModifiedAtUtc: null },
    ]);
    expect(app.filteredFiles().map(file => [file.id, file.starred])).toEqual([['s', true]]);

    app.selectNav('recent');
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&view=recent`).flush([]);
    http.verify();
  });

  it('should search with q after a debounce and return to the folder view when cleared', () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    app.onSearchChange('rep');
    app.onSearchChange('report');
    vi.advanceTimersByTime(299);
    http.expectNone(r => r.url.endsWith('/Files'));
    vi.advanceTimersByTime(1);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&q=report`).flush([
      { id: 'r', fileName: 'quarterly.pdf', contentType: 'application/pdf', fileSize: 1, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false, lastModifiedAtUtc: null },
    ]);
    expect(app.filteredFiles().map(file => file.id)).toEqual(['r']);

    app.onSearchChange('');
    vi.advanceTimersByTime(300);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    vi.useRealTimers();
    http.verify();
  });

  it('should rename a file and show the invalid-name error', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);

    app.startRenameFolder(sampleFiles[0]);
    app.folderName.set('bad/name');
    app.submitFolderEditor();
    const bad = http.expectOne(`${environment.apiBaseUrl}/Files/1/name`);
    expect([bad.request.method, bad.request.body]).toEqual(['PUT', { name: 'bad/name' }]);
    bad.flush({ code: 'File.InvalidName' }, { status: 400, statusText: 'Bad Request' });
    expect(app.actionError()).toBe('Enter a valid file name.');

    app.folderName.set('good.txt');
    app.submitFolderEditor();
    http.expectOne(`${environment.apiBaseUrl}/Files/1/name`).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    expect(app.folderEditor()).toBeNull();
    http.verify();
  });

  it('should list the trash and restore, delete forever and empty it', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const noContent = { status: 204, statusText: 'No Content' };
    const reloadTrash = (withStorage = true): void => {
      http.expectOne(`${environment.apiBaseUrl}/Files/trash?skip=0&take=100`).flush([
        { id: 't', fileName: 't.txt', contentType: 'text/plain', fileSize: 1, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false, lastModifiedAtUtc: null },
      ]);
      if (withStorage) {
        http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 1, limitBytes: 10 });
      }
    };

    app.selectNav('trash');
    reloadTrash(false);
    http.verify();
    expect(app.files().map(file => file.id)).toEqual(['t']);

    app.restoreFile(app.files()[0]);
    const restore = http.expectOne(`${environment.apiBaseUrl}/Files/t/restore`);
    expect(restore.request.method).toBe('POST');
    restore.flush(null, noContent);
    reloadTrash();

    app.deleteForever(app.files()[0]);
    const forever = http.expectOne(`${environment.apiBaseUrl}/Files/t/permanent`);
    expect(forever.request.method).toBe('DELETE');
    forever.flush(null, noContent);
    reloadTrash();

    app.emptyTrash();
    const empty = http.expectOne(`${environment.apiBaseUrl}/Files/trash`);
    expect(empty.request.method).toBe('DELETE');
    empty.flush(null, noContent);
    reloadTrash();
    expect(app.storage()).toEqual({ usedBytes: 1, limitBytes: 10 });
    http.verify();
  });

  it('should support single, toggle, and range selection', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    app.files.set(sampleFiles);

    app.selectFile('2');
    expect([...app.selectedIds()]).toEqual(['2']);

    app.selectFile('6', new MouseEvent('click', { shiftKey: true }));
    expect([...app.selectedIds()]).toEqual(['2', '3', '4', '5', '6']);

    app.selectFile('4', new MouseEvent('click', { ctrlKey: true }));
    expect([...app.selectedIds()]).toEqual(['2', '3', '5', '6']);
  });

  it('should clear selection on Escape', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    app.selectFile('3');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(app.selectedIds().size).toBe(0);
    expect(app.selectedId()).toBeNull();
  });

  it('should select files intersected by a marquee', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const surface = document.createElement('div');
    surface.setPointerCapture = () => {};
    surface.hasPointerCapture = () => false;
    const first = document.createElement('div');
    first.dataset['fileId'] = '1';
    first.getBoundingClientRect = () => ({ left: 10, top: 10, right: 30, bottom: 30 } as DOMRect);
    const second = document.createElement('div');
    second.dataset['fileId'] = '2';
    second.getBoundingClientRect = () => ({ left: 40, top: 40, right: 60, bottom: 60 } as DOMRect);
    surface.append(first, second);

    const pointer = (type: string, x: number, y: number): PointerEvent => {
      const event = Object.assign(new MouseEvent(type, { clientX: x, clientY: y, bubbles: true }), { pointerId: 1 });
      Object.defineProperty(event, 'currentTarget', { value: surface });
      return event as PointerEvent;
    };

    app.beginMarquee(pointer('pointerdown', 0, 0));
    app.moveMarquee(pointer('pointermove', 35, 35));

    expect([...app.selectedIds()]).toEqual(['1']);
    expect(app.selectionRect()).toEqual({ left: 0, top: 0, width: 35, height: 35 });

    app.endMarquee(pointer('pointerup', 35, 35));
    expect(app.selectionRect()).toBeNull();
  });

  it('should load the signed-in user files and map them to file items', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);

    app.loadFiles();
    expect(app.filesLoading()).toBe(true);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([
      { id: 'a', fileName: 'photo.png', contentType: 'image/png', fileSize: 1258291, createdAtUtc: '2026-07-22T10:00:00Z' },
      { id: 'b', fileName: 'notes.bin', contentType: 'application/octet-stream', fileSize: 512, createdAtUtc: '2026-07-22T10:00:00Z' },
    ]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);

    expect(app.filesLoading()).toBe(false);
    expect(app.files().map(file => [file.id, file.name, file.type, file.size])).toEqual([
      ['a', 'photo.png', 'image', '1.2 MB'],
      ['b', 'notes.bin', 'document', '512 B'],
    ]);
    expect(app.selectedIds().size).toBe(0);
    http.verify();
  });

  it('should render the empty state when the user has no files', async () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    app.page.set('app');

    app.loadFiles();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No files yet');
    http.verify();
  });

  it('should render an error when the file list fails to load', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    app.page.set('app');

    app.loadFiles();
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unable to load your files');
    http.verify();
  });

  it('should remove a deleted file and clear its selection', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.selectFile('2');

    app.deleteFile(sampleFiles[1]);
    http.expectOne(`${environment.apiBaseUrl}/Files/2`).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });

    expect(app.files().some(file => file.id === '2')).toBe(false);
    expect(app.selectedIds().size).toBe(0);
    expect(app.selectedId()).toBeNull();
    http.verify();
  });

  it('should keep the file and show an error when delete fails', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);

    app.deleteFile(sampleFiles[1]);
    http.expectOne(`${environment.apiBaseUrl}/Files/2`).flush(null, { status: 404, statusText: 'Not Found' });

    expect(app.files().length).toBe(sampleFiles.length);
    expect(app.actionError()).toContain('Unable to delete');
    http.verify();
  });

  it('should download a file through an object URL and revoke it', () => {
    vi.useFakeTimers();
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    app.downloadFile(sampleFiles[0]);
    http.expectOne(`${environment.apiBaseUrl}/Files/1`).flush(new Blob(['x']));

    expect(create).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    expect(revoke).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1000);
    expect(revoke).toHaveBeenCalledWith('blob:test');
    vi.useRealTimers();
    http.verify();
  });

  it('should render the file preview for one selected file and download it on its output', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    fixture.detectChanges();
    app.page.set('app');
    app.files.set([{ ...sampleFiles[0], contentType: 'application/pdf', fileSize: 1024 }]);
    app.selectFile('1');
    app.openPreview();
    fixture.detectChanges();

    const preview = fixture.debugElement.query(By.directive(FilePreviewComponent));
    expect(preview.componentInstance.fileId()).toBe('1');
    preview.componentInstance.download.emit();
    http.expectOne(`${environment.apiBaseUrl}/Files/1`).flush(new Blob(['x']));

    expect(click).toHaveBeenCalled();
    http.verify();
  });

  it('should upload selected files then reload the list', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const input = document.createElement('input');
    input.type = 'file';
    Object.defineProperty(input, 'files', {
      value: [new File(['a'], 'a.txt'), new File(['b'], 'b.txt')],
    });

    app.onUploadSelected({ target: input } as unknown as Event);
    http.expectOne(`${environment.apiBaseUrl}/Files/upload`).flush({});
    http.expectOne(`${environment.apiBaseUrl}/Files/upload`).flush({});
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });

    expect(app.actionBusy()).toBe(false);
    expect(app.actionError()).toBe('');
    http.verify();
  });

  it('should report a failed upload and continue', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const input = document.createElement('input');
    input.type = 'file';
    Object.defineProperty(input, 'files', { value: [new File(['a'], 'a.txt')] });

    app.onUploadSelected({ target: input } as unknown as Event);
    http.expectOne(`${environment.apiBaseUrl}/Files/upload`).flush('no', { status: 400, statusText: 'Bad Request' });
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);

    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });

    expect(app.actionError()).toContain('Unable to upload a.txt');
    http.verify();
  });

  it('should show the quota message when an upload exceeds the storage limit', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const input = document.createElement('input');
    input.type = 'file';
    Object.defineProperty(input, 'files', { value: [new File(['a'], 'a.txt')] });

    app.onUploadSelected({ target: input } as unknown as Event);
    http.expectOne(`${environment.apiBaseUrl}/Files/upload`).flush(
      { code: 'Storage.QuotaExceeded', message: 'Not enough storage space.' },
      { status: 400, statusText: 'Bad Request' },
    );
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });

    expect(app.actionError()).toBe('Not enough storage space.');
    http.verify();
  });

  it('should show folders instead of the empty state when only folders exist', async () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    app.page.set('app');
    app.folders.set([{ id: 'f1', name: 'Images', type: 'folder', size: '—', modified: 'Oct 31, 2026', starred: false, shared: false }]);
    app.files.set([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('No files yet');
    expect(text).toContain('Images');
    http.verify();
  });

  it('should open a folder, load its contents, and return to root from the breadcrumb', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const folder = { id: 'f1', name: 'Projects', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z' };

    app.loadFiles();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([folder]);
    expect(app.items().map(item => item.name)).toEqual(['Projects']);

    app.openFolder(app.items()[0]);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&folderId=f1`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders?parentId=f1`).flush([]);
    expect(app.breadcrumb()).toEqual([environment.appName, 'My Files', 'Projects']);

    app.navigateCrumb(1);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([folder]);
    expect(app.breadcrumb()).toEqual([environment.appName, 'My Files']);
    http.verify();
  });

  it('should create a folder in the current folder and reload the listing', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);

    app.loadFiles();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);

    app.startCreateFolder();
    app.folderName.set('  Reports ');
    app.submitFolderEditor();
    const create = http.expectOne(`${environment.apiBaseUrl}/Folders`);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ name: 'Reports', parentId: null });
    create.flush({ id: 'f2', name: 'Reports', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z' });

    expect(app.folderEditor()).toBeNull();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([
      { id: 'f2', name: 'Reports', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z' },
    ]);
    expect(app.items().map(item => item.name)).toEqual(['Reports']);
    http.verify();
  });

  it('should upload OS files dropped on the explorer into the current folder', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folderPath.set([{ id: 'f1', name: 'Projects' }]);

    app.onDropFiles(dragEvent([new File(['a'], 'a.txt'), new File(['b'], 'b.txt')]));
    const first = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect(first.request.body.get('folderId')).toBe('f1');
    first.flush({});
    http.expectOne(`${environment.apiBaseUrl}/Files/upload`).flush({});
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&folderId=f1`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders?parentId=f1`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });

    expect(app.actionBusy()).toBe(false);
    http.verify();
  });

  it('should ignore a drop that carries no files', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);

    app.onDropFiles(dragEvent([]));

    http.verify();
  });

  it('should move a file dropped on a folder and reload the listing', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.folders.set([folderItem('f1', 'Projects')]);

    app.startDrag(dragEvent(), app.files()[0]);
    app.dropItems(dragEvent(), 'f1');

    const move = http.expectOne(`${environment.apiBaseUrl}/Files/1/folder`);
    expect(move.request.method).toBe('PUT');
    expect(move.request.body).toEqual({ folderId: 'f1' });
    move.flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.verify();
  });

  it('should move every selected file when one of them is dragged', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.folders.set([folderItem('f1', 'Projects')]);
    app.selectFile('1');
    app.selectFile('2', new MouseEvent('click', { ctrlKey: true }));

    app.startDrag(dragEvent(), app.files()[1]);
    app.dropItems(dragEvent(), 'f1');

    http.expectOne(`${environment.apiBaseUrl}/Files/1/folder`).flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files/2/folder`).flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.verify();
  });

  it('should move a folder dropped on another folder with its name and the new parentId', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folders.set([folderItem('f2', 'Reports'), folderItem('f1', 'Projects')]);

    app.startDrag(dragEvent(), app.folders()[0]);
    app.dropItems(dragEvent(), 'f1');

    const update = http.expectOne(`${environment.apiBaseUrl}/Folders/f2`);
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({ name: 'Reports', parentId: 'f1' });
    update.flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    http.verify();
  });

  it('should not highlight or move a folder dropped onto itself', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folders.set([folderItem('f1', 'Projects')]);

    app.startDrag(dragEvent(), app.folders()[0]);
    app.dragOverTarget(dragEvent(), 'f1', 'f1');
    expect(app.dropKey()).toBeNull();
    app.dropItems(dragEvent(), 'f1');

    http.verify();
  });

  it('should do nothing when an item is dropped on a file row', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    app.page.set('app');
    app.files.set(sampleFiles);
    fixture.detectChanges();
    app.startDrag(dragEvent(), app.files()[0]);

    const row = (fixture.nativeElement as HTMLElement).querySelector('[data-file-id="2"]') as HTMLElement;
    const dragOver = new Event('dragover', { bubbles: true, cancelable: true });
    row.dispatchEvent(dragOver);
    row.dispatchEvent(new Event('drop', { bubbles: true, cancelable: true }));

    expect(dragOver.defaultPrevented).toBe(false);
    expect(app.dropKey()).toBeNull();
    http.verify();
  });

  it('should show the backend cycle error when a folder move is rejected', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folders.set([folderItem('f2', 'Reports'), folderItem('f1', 'Projects')]);

    app.startDrag(dragEvent(), app.folders()[0]);
    app.dropItems(dragEvent(), 'f1');

    http.expectOne(`${environment.apiBaseUrl}/Folders/f2`).flush(
      { code: 'Folder.Cycle', message: 'Cycle' },
      { status: 400, statusText: 'Bad Request' },
    );
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);

    expect(app.actionError()).toBe('A folder cannot be moved into itself or one of its subfolders.');
    http.verify();
  });

  it('should upload OS files dropped on a folder row into that folder, and on the background into the current one', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const reload = (query = '', parent = '') => {
      http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100${query}`).flush([]);
      http.expectOne(`${environment.apiBaseUrl}/Folders${parent}`).flush([]);
      http.expectOne(`${environment.apiBaseUrl}/Account/storage`).flush({ usedBytes: 0, limitBytes: 10 });
    };

    app.dropOnFolder(dragEvent([new File(['a'], 'a.txt')]), folderItem('f9', 'Target'));
    const toFolder = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect(toFolder.request.body.get('folderId')).toBe('f9');
    toFolder.flush({});
    reload();

    app.onDropFiles(dragEvent([new File(['b'], 'b.txt')]));
    const toRoot = http.expectOne(`${environment.apiBaseUrl}/Files/upload`);
    expect(toRoot.request.body.has('folderId')).toBe(false);
    toRoot.flush({});
    reload();
    http.verify();
  });

  it('should move a file dropped on the root crumb to folderId null', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folderPath.set([{ id: 'f1', name: 'Projects' }]);
    app.files.set(sampleFiles);

    app.startDrag(dragEvent(), app.files()[0]);
    app.dropOnCrumb(dragEvent(), 1);

    const move = http.expectOne(`${environment.apiBaseUrl}/Files/1/folder`);
    expect(move.request.body).toEqual({ folderId: null });
    move.flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&folderId=f1`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders?parentId=f1`).flush([]);
    http.verify();
  });

  it('should move a file dropped on a folder crumb to that folder', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folderPath.set([{ id: 'f1', name: 'Projects' }, { id: 'f2', name: 'Reports' }]);
    app.files.set(sampleFiles);

    app.startDrag(dragEvent(), app.files()[0]);
    app.dropOnCrumb(dragEvent(), 2);

    const move = http.expectOne(`${environment.apiBaseUrl}/Files/1/folder`);
    expect(move.request.body).toEqual({ folderId: 'f1' });
    move.flush(null);
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100&folderId=f2`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders?parentId=f2`).flush([]);
    http.verify();
  });

  it('should not send a request when dropped on the crumb of the current folder', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folderPath.set([{ id: 'f1', name: 'Projects' }]);
    app.files.set(sampleFiles);

    app.startDrag(dragEvent(), app.files()[0]);
    app.dropOnCrumb(dragEvent(), 2);

    http.verify();
  });

  it('should open the share dialog, create and revoke a link', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.selectedId.set('1');

    app.openShare();
    http.expectOne(r => r.url === `${environment.apiBaseUrl}/Share` && r.params.get('fileId') === '1').flush([]);
    expect(app.shareOpen()).toBe(true);

    app.shareExpiryDays.set(7);
    app.createShareLink();
    const create = http.expectOne(`${environment.apiBaseUrl}/Share`);
    expect(create.request.body.fileId).toBe('1');
    expect(create.request.body.expiresAtUtc).not.toBeNull();
    const link = { id: 'l1', fileId: '1', token: 'tok', expiresAtUtc: null, createdAtUtc: '2026-07-22T10:00:00Z' };
    create.flush(link);
    expect(app.shareLinks()).toEqual([link]);

    app.revokeShareLink(link);
    http.expectOne(`${environment.apiBaseUrl}/Share/l1`).flush(null);
    expect(app.shareLinks()).toEqual([]);
    http.verify();
  });

  it('should show backend errors in the share dialog', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.selectedId.set('1');

    app.openShare();
    http.expectOne(r => r.url === `${environment.apiBaseUrl}/Share`).flush([]);
    app.createShareLink();
    http.expectOne(`${environment.apiBaseUrl}/Share`).flush({ title: 'Expiry must be in the future.' }, { status: 400, statusText: 'Bad Request' });
    expect(app.shareError()).toBe('Expiry must be in the future.');
  });

  it('should prefer the backend error description in the share dialog', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.files.set(sampleFiles);
    app.selectedId.set('1');

    app.openShare();
    http.expectOne(r => r.url === `${environment.apiBaseUrl}/Share`).flush([]);
    app.createShareLink();
    http.expectOne(`${environment.apiBaseUrl}/Share`).flush({ code: 'Share.Past', description: 'Expiry is in the past.', title: 'x' }, { status: 400, statusText: 'Bad Request' });
    expect(app.shareError()).toBe('Expiry is in the past.');
  });
});
