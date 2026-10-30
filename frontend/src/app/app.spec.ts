import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App, FileItem } from './app';
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

  it('should toggle a file favorite', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    app.files.set(sampleFiles);
    const file = app.files()[0];
    const initialStarredState = file.starred;

    app.toggleFileStar(file.id);

    expect(app.files()[0].starred).toBe(!initialStarredState);
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
    http.expectOne(`${environment.apiBaseUrl}/Files`).flush([
      { id: 'a', fileName: 'photo.png', contentType: 'image/png', fileSize: 1258291, createdAtUtc: '2026-07-22T10:00:00Z' },
      { id: 'b', fileName: 'notes.bin', contentType: 'application/octet-stream', fileSize: 512, createdAtUtc: '2026-07-22T10:00:00Z' },
    ]);

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
    http.expectOne(`${environment.apiBaseUrl}/Files`).flush([]);
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
    http.expectOne(`${environment.apiBaseUrl}/Files`).flush(null, { status: 500, statusText: 'Server Error' });
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
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    app.downloadFile(sampleFiles[0]);
    http.expectOne(`${environment.apiBaseUrl}/Files/1`).flush(new Blob(['x']));

    expect(create).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    expect(revoke).toHaveBeenCalledWith('blob:test');
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
    http.expectOne(`${environment.apiBaseUrl}/Files`).flush([]);

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
    http.expectOne(`${environment.apiBaseUrl}/Files`).flush([]);

    expect(app.actionError()).toContain('Unable to upload a.txt');
    http.verify();
  });
});
