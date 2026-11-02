import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { By } from '@angular/platform-browser';
import { App, FileItem } from './app';
import { routes } from './app.routes';
import { AuthService } from './auth.service';
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

  it('should have no Shared nav item or filter chip, and keep Trash in the bottom nav', () => {
    const app = TestBed.createComponent(App).componentInstance;
    expect(app.navItems.some(i => i.label === 'Shared')).toBe(false);
    expect(app.filterChips).not.toContain('Shared');
    expect(app.navItems.slice(0, 5).some(i => i.key === 'trash')).toBe(true);
  });

  it('should derive the profile initial from the signed-in email', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    expect(app.userInitial()).toBe('?');
    app.userEmail.set('person@arcano.test');
    expect(app.userInitial()).toBe('P');
  });

  describe('context menu', () => {
    const api = environment.apiBaseUrl;
    const open = (target: FileItem | null, nav: 'my-files' | 'trash' = 'my-files') => {
      const fixture = TestBed.createComponent(App);
      const app = fixture.componentInstance;
      const http = TestBed.inject(HttpTestingController);
      fixture.detectChanges(); // ngOnInit resets the page, so set it afterwards
      app.page.set('app');
      app.activeNav.set(nav);
      app.files.set(sampleFiles);
      app.folders.set([folderItem('d1', 'Docs')]);
      const event = new MouseEvent('contextmenu', { clientX: 10, clientY: 10, cancelable: true });
      if (target) {
        app.showContextMenu(target, event);
      } else {
        app.showBackgroundMenu(event);
      }
      fixture.detectChanges();
      const labels = () => Array.from(fixture.nativeElement.querySelectorAll('[role="menuitem"]') as NodeListOf<HTMLElement>).map(el => el.children[1].textContent!.trim());
      return { fixture, app, http, labels, event };
    };

    it('shows Open for folders and file actions for files', () => {
      const folder = open(folderItem('d1', 'Docs')).labels();
      expect(folder.some(l => l.startsWith('Open'))).toBe(true);
      expect(folder.some(l => l === 'Download' || l.startsWith('Copy'))).toBe(false);
      expect(folder.some(l => l.startsWith('Download as zip'))).toBe(true);
      expect(folder.some(l => l === 'Share')).toBe(true);

      const file = open(sampleFiles[0]).labels();
      expect(file.some(l => l.startsWith('Open'))).toBe(false);
      for (const label of ['Preview', 'Download', 'Share', 'Rename', 'Make a copy', 'Copy', 'Cut', 'Move to', 'Move to trash']) {
        expect(file.some(l => l.startsWith(label))).toBe(true);
      }
    });

    it('Download as zip explains a folder with too many files, read from the Blob error body', async () => {
      const { app, http } = open(folderItem('d1', 'Docs'));
      const fail = (body: string) => {
        app.downloadFolder(folderItem('d1', 'Docs'));
        http.expectOne(`${api}/Folders/d1/download`).flush(new Blob([body], { type: 'application/json' }), { status: 400, statusText: 'Bad Request' });
      };

      fail(JSON.stringify({ code: 'Folder.TooLarge', message: 'Folder holds more than 5000 files.' }));
      await vi.waitFor(() => expect(app.actionBusy()).toBe(false));
      expect(app.actionError()).toBe('This folder has too many files to download as a zip.');

      for (const body of [JSON.stringify({ code: 'Folder.NotFound', message: 'Not found.' }), '<html>Bad Gateway</html>', 'null']) {
        fail(body);
        await vi.waitFor(() => expect(app.actionBusy()).toBe(false));
        expect(app.actionError()).toBe('Unable to download Docs. Please try again.');
      }
    });

    it('Download as zip fetches the folder archive', () => {
      const { app, http } = open(folderItem('d1', 'Docs'));
      URL.createObjectURL = () => 'blob:zip';
      URL.revokeObjectURL = () => {};

      app.downloadFolder(folderItem('d1', 'Docs'));

      const req = http.expectOne(`${api}/Folders/d1/download`);
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['zip']));
      expect(app.actionBusy()).toBe(false);
      expect(app.actionError()).toBe('');
    });

    it('details panel offers Download as zip for a selected folder', () => {
      const { fixture, app, http } = open(null);
      URL.createObjectURL = () => 'blob:zip';
      URL.revokeObjectURL = () => {};
      app.selectedId.set('d1');
      app.selectedIds.set(new Set(['d1']));
      fixture.detectChanges();

      const button = Array.from(fixture.nativeElement.querySelectorAll('aside button') as NodeListOf<HTMLButtonElement>)
        .find(b => b.textContent!.includes('Download as zip'))!;
      expect(button).toBeTruthy();
      button.click();

      expect(http.expectOne(`${api}/Folders/d1/download`).request.method).toBe('GET');
    });

    it('Download as zip reports an error', async () => {
      const { app, http } = open(folderItem('d1', 'Docs'));

      app.downloadFolder(folderItem('d1', 'Docs'));
      http.expectOne(`${api}/Folders/d1/download`).flush(new Blob(['x']), { status: 500, statusText: 'Server Error' });

      await vi.waitFor(() => expect(app.actionError()).toBe('Unable to download Docs. Please try again.'));
    });

    it('deleting a folder asks first and only deletes after confirming', () => {
      const { app, http } = open(folderItem('d1', 'Docs'));

      app.deleteFolder(folderItem('d1', 'Docs'));

      http.expectNone(`${api}/Folders/d1`);
      expect(app.pendingTrash()?.message).toBe('Delete Docs and everything in it? Files go to the Trash.');
      app.confirmPendingTrash();
      expect(http.expectOne(`${api}/Folders/d1`).request.method).toBe('DELETE');
    });

    it('selects an unselected item on right-click', () => {
      const { app } = open(sampleFiles[2]);
      expect([...app.selectedIds()]).toEqual(['3']);
    });

    it('the phone upload button opens the file picker', () => {
      const { fixture } = open(null);
      const picker = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
      const clicked = vi.spyOn(picker, 'click');

      (fixture.nativeElement.querySelector('button[aria-label="Upload files"]') as HTMLButtonElement).click();

      expect(clicked).toHaveBeenCalledTimes(1);
    });

    it('Make a copy posts to the copy endpoint', () => {
      const { fixture, http } = open(sampleFiles[2]);
      const item = Array.from(fixture.nativeElement.querySelectorAll('[role="menuitem"]') as NodeListOf<HTMLElement>).find(el => el.textContent!.includes('Make a copy'))!;

      item.click();

      const req = http.expectOne(`${api}/Files/3/copy`);
      expect(req.request.body).toEqual({ folderId: null });
      req.flush({ id: 'new' }, { status: 201, statusText: 'Created' });
      http.match(r => r.method === 'GET').forEach(r => r.flush([]));
    });

    it('background menu pastes the clipboard and prevents the browser menu', () => {
      const { fixture, app, http, event } = open(null);
      expect(event.defaultPrevented).toBe(true);
      const paste = () => Array.from(fixture.nativeElement.querySelectorAll('[role="menuitem"]') as NodeListOf<HTMLElement>).find(el => el.textContent!.includes('Paste'))!;
      expect(paste().getAttribute('aria-disabled')).toBe('true');

      app.clipboard.set({ mode: 'copy', items: [sampleFiles[0]], sourceFolderId: null });
      app.showBackgroundMenu(new MouseEvent('contextmenu', { cancelable: true }));
      fixture.detectChanges();
      paste().click();

      http.expectOne(`${api}/Files/1/copy`).flush({ id: 'new' }, { status: 201, statusText: 'Created' });
      http.match(r => r.method === 'GET').forEach(r => r.flush([]));
    });

    it('ignores the background menu in Trash', () => {
      const { app } = open(null, 'trash');
      expect(app.contextMenu()).toBeNull();
    });

    it('closes on Escape without clearing the selection', () => {
      const { app } = open(sampleFiles[0]);
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      expect(app.contextMenu()).toBeNull();
      expect(app.selectedIds().size).toBe(1);
    });
  });

  it('should star a folder through the API and revert when it fails', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folders.set([{ id: 'f1', name: 'Images', type: 'folder', size: null, modified: '', starred: false, shared: false }]);

    app.toggleFileStar('f1');
    expect(app.folders()[0].starred).toBe(true);
    const star = http.expectOne(`${environment.apiBaseUrl}/Folders/f1/star`);
    expect([star.request.method, star.request.body]).toEqual(['PUT', { isStarred: true }]);
    star.flush(null, { status: 204, statusText: 'No Content' });
    expect(app.folders()[0].starred).toBe(true);

    app.toggleFileStar('f1');
    http.expectOne(`${environment.apiBaseUrl}/Folders/f1/star`).flush(null, { status: 500, statusText: 'Server Error' });
    expect(app.folders()[0].starred).toBe(true);
    expect(app.actionError()).toContain('Unable to update favorites for Images');
    http.verify();
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

  it('should return to the root when the active section is selected again inside a folder', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.folderPath.set([{ id: 'f1', name: 'Projects' }, { id: 'f2', name: 'Reports' }]);

    app.selectNav(app.activeNav());

    expect(app.folderPath()).toEqual([]);
    http.expectOne(r => r.url.endsWith('/Files')).flush([]);
    http.expectOne(r => r.url.endsWith('/Folders')).flush([]);
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

  it('should load the first page and append the next one near the bottom', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const page = (start: number, count: number) =>
      Array.from({ length: count }, (_, i) => ({ id: `f${start + i}`, fileName: `f${start + i}.txt`, contentType: 'text/plain', fileSize: 1, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false, lastModifiedAtUtc: null }));
    const near = { target: { scrollHeight: 1000, scrollTop: 500, clientHeight: 400 } } as unknown as Event;

    app.loadFiles();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush(page(0, 100));
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([]);
    expect(app.hasMoreFiles()).toBe(true);

    app.onFilesScroll({ target: { scrollHeight: 5000, scrollTop: 0, clientHeight: 400 } } as unknown as Event);
    http.verify(); // far from the bottom: nothing requested

    app.onFilesScroll(near);
    app.onFilesScroll(near); // in-flight guard: still one request
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=100&take=100`).flush(page(100, 3));
    expect(app.files().length).toBe(103);
    expect(app.hasMoreFiles()).toBe(false);
    app.onFilesScroll(near);
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
    http.verify(); // nothing is sent before the user confirms
    expect(app.pendingTrash()?.message).toBe('Delete forever? This file cannot be recovered.');
    app.pendingTrash.set(null); // cancel
    app.deleteForever(app.files()[0]);
    app.confirmPendingTrash();
    const forever = http.expectOne(`${environment.apiBaseUrl}/Files/t/permanent`);
    expect(forever.request.method).toBe('DELETE');
    forever.flush(null, noContent);
    reloadTrash();

    app.emptyTrash();
    http.verify();
    expect(app.pendingTrash()?.message).toBe('Empty trash? All 1 items will be permanently deleted.');
    app.confirmPendingTrash();
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

  it('gives the password toggles, the bell and nothing else icon-only an accessible name', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const toggle = () => el.querySelector('button[aria-label$="password"]') as HTMLButtonElement;
    expect(toggle().getAttribute('aria-label')).toBe('Show password');
    expect(toggle().getAttribute('aria-pressed')).toBe('false');
    toggle().click();
    fixture.detectChanges();
    expect(toggle().getAttribute('aria-label')).toBe('Hide password');
    expect(toggle().getAttribute('aria-pressed')).toBe('true');

    app.page.set('signup');
    fixture.detectChanges();
    expect(toggle().getAttribute('aria-label')).toBe('Show password');

    app.page.set('app');
    fixture.detectChanges();
    expect(el.querySelector('button[aria-label="Notifications"]')).not.toBeNull();
  });

  describe('details below xl', () => {
    const openSelected = () => {
      const fixture = TestBed.createComponent(App);
      const app = fixture.componentInstance;
      fixture.detectChanges();
      app.page.set('app');
      app.activeNav.set('my-files');
      app.files.set(sampleFiles);
      app.selectedId.set(sampleFiles[0].id);
      app.selectedIds.set(new Set([sampleFiles[0].id]));
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      const toggle = () => el.querySelector('button.xl\\:hidden[aria-label="Toggle details"]') as HTMLButtonElement;
      const sheet = () => el.querySelector('div.fixed.right-0.xl\\:hidden');
      return { fixture, app, toggle, sheet };
    };

    it('opens the existing panel in a sheet from the toggle and closes it on the button, Escape and a view change', () => {
      const { fixture, app, toggle, sheet } = openSelected();
      expect(toggle().getAttribute('aria-expanded')).toBe('false');
      expect(sheet()).toBeNull();

      toggle().click();
      fixture.detectChanges();
      expect(toggle().getAttribute('aria-expanded')).toBe('true');
      expect(sheet()?.textContent).toContain('File Info');

      (sheet()!.querySelector('[aria-label="Close details"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(sheet()).toBeNull();

      app.detailsSheetOpen.set(true);
      app.onEscape();
      expect(app.detailsSheetOpen()).toBe(false);
      expect(app.selectedIds().size).toBe(1); // first Escape only closes the sheet

      app.detailsSheetOpen.set(true);
      app.selectNav('trash');
      expect(app.detailsSheetOpen()).toBe(false);
    });

    it('does not open the sheet when a file is selected', () => {
      const { fixture, app, sheet } = openSelected();
      app.selectFile(sampleFiles[0].id);
      fixture.detectChanges();
      expect(app.previewOpen()).toBe(true); // xl panel behaviour unchanged
      expect(sheet()).toBeNull();
    });
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
    const folder = { id: 'f1', name: 'Projects', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false };

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
    create.flush({ id: 'f2', name: 'Reports', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false });

    expect(app.folderEditor()).toBeNull();
    http.expectOne(`${environment.apiBaseUrl}/Files?skip=0&take=100`).flush([]);
    http.expectOne(`${environment.apiBaseUrl}/Folders`).flush([
      { id: 'f2', name: 'Reports', parentId: null, createdAtUtc: '2026-07-22T10:00:00Z', isStarred: false },
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

  it('should show a clear message for an invalid invite key and send the key', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    app.signUpEmail.set('a@b.co');
    app.signUpPassword.set('secret123');
    app.signUpKey.set(' key-1 ');

    app.onSignUpSubmit(new Event('submit'));
    const request = http.expectOne(`${environment.apiBaseUrl}/Auth/register`);
    expect(request.request.body).toEqual({ email: 'a@b.co', password: 'secret123', inviteKey: 'key-1' });
    request.flush(
      { code: 'Auth.InviteInvalid', message: 'Invite key is invalid.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(app.signUpError()).toBe('This invite key is invalid, expired or already used.');
  });

  it('should show the invite key field only when the server requires one', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    app.page.set('signup');
    fixture.detectChanges();
    expect(el.querySelector('input[name="inviteKey"]')).toBeNull(); // setup not resolved yet
    expect(el.textContent).not.toContain('first account becomes the admin');

    app.requiresKey.set(true);
    fixture.detectChanges();
    expect(el.querySelector('input[name="inviteKey"]')).not.toBeNull();

    app.requiresKey.set(false);
    fixture.detectChanges();
    expect(el.querySelector('input[name="inviteKey"]')).toBeNull();
    expect(el.textContent).toContain('first account becomes the admin');

    app.signUpEmail.set('a@b.co');
    app.signUpPassword.set('secret123');
    app.signUpKey.set('ignored');
    app.onSignUpSubmit(new Event('submit'));
    expect(http.expectOne(`${environment.apiBaseUrl}/Auth/register`).request.body).toEqual({ email: 'a@b.co', password: 'secret123' });
  });

  it('should reset the password with a key and return to sign-in with a note', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    app.resetKey.set('rk');
    app.resetPassword.set('short');
    app.resetConfirm.set('short');
    app.onResetSubmit(new Event('submit'));
    expect(app.resetError()).toContain('at least 8');
    app.resetPassword.set('long-enough');
    app.onResetSubmit(new Event('submit'));
    expect(app.resetError()).toContain('do not match');
    http.expectNone(`${environment.apiBaseUrl}/Auth/reset`);

    app.resetConfirm.set('long-enough');
    app.onResetSubmit(new Event('submit'));
    const request = http.expectOne(`${environment.apiBaseUrl}/Auth/reset`);
    expect(request.request.body).toEqual({ key: 'rk', newPassword: 'long-enough' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(TestBed.inject(AuthService).takeSignInNotice()).toContain('Password reset');
    expect(navigate).toHaveBeenCalledWith('/signin');

    app.resetKey.set('bad');
    app.resetPassword.set('long-enough');
    app.resetConfirm.set('long-enough');
    app.onResetSubmit(new Event('submit'));
    http.expectOne(`${environment.apiBaseUrl}/Auth/reset`).flush(
      { code: 'Auth.InviteInvalid', message: 'x' },
      { status: 403, statusText: 'Forbidden' },
    );
    expect(app.resetError()).toContain('invalid');
  });

  it('should explain HTTP 429 on sign-up, reset and sign-in', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const msg = 'Too many attempts. Wait a minute and try again.';
    app.onSignUpSubmit(new Event('submit'));
    http.expectOne(`${environment.apiBaseUrl}/Auth/register`).flush({}, { status: 429, statusText: 'x' });
    expect(app.signUpError()).toBe(msg);

    app.resetKey.set('rk');
    app.resetPassword.set('long-enough');
    app.resetConfirm.set('long-enough');
    app.onResetSubmit(new Event('submit'));
    http.expectOne(`${environment.apiBaseUrl}/Auth/reset`).flush({}, { status: 429, statusText: 'x' });
    expect(app.resetError()).toBe(msg);
  });

  it('should show the backend message for a rejected sign-up and the generic text otherwise', () => {
    const app = TestBed.createComponent(App).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const submit = (status: number, body: object) => {
      app.onSignUpSubmit(new Event('submit'));
      http.expectOne(`${environment.apiBaseUrl}/Auth/register`).flush(body, { status, statusText: 'x' });
    };

    submit(400, { code: 'Auth.RegisterFailed', message: 'Passwords must be at least 8 characters.' });
    expect(app.signUpError()).toBe('Passwords must be at least 8 characters.');

    submit(500, { code: 'X', message: 'boom' });
    expect(app.signUpError()).toBe('Unable to create your account. Please try again.');
  });

  it('should classify audio files and open previewable files on double-click', () => {
    const app = TestBed.createComponent(App).componentInstance;
    expect(app.fileTypeName('audio')).toBe('Audio');
    const song = { ...sampleFiles[0], contentType: 'audio/wav', fileSize: 10 };
    const bin = { ...sampleFiles[1], contentType: 'application/octet-stream', fileSize: 10 };
    app.files.set([song, bin]);
    app.openItem(bin);
    expect(app.previewModalOpen()).toBe(false);
    app.openItem(song);
    expect(app.previewModalOpen()).toBe(true);
    expect(app.selectedFile()?.id).toBe(song.id);
  });

  it('should show the current folder name as the page title', () => {
    const app = TestBed.createComponent(App).componentInstance;
    expect(app.pageTitle()).toBe('My Files');
    app.activeNav.set('my-files');
    app.folderPath.set([{ id: 'f', name: 'Docs' }]);
    expect(app.pageTitle()).toBe('Docs');
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

  describe.each(['list', 'grid'] as const)('clicking real rows in %s view', view => {
    const setup = (files: FileItem[] = sampleFiles) => {
      const fixture = TestBed.createComponent(App);
      const app = fixture.componentInstance;
      fixture.detectChanges(); // ngOnInit resets the page, so set it afterwards
      app.page.set('app');
      app.viewMode.set(view);
      app.files.set(files);
      fixture.detectChanges();
      const row = (id: string) => fixture.nativeElement.querySelector(`[data-file-id="${id}"]`) as HTMLElement;
      // Dispatches on an inner element, where a real click lands: the name in a list row, the button in a grid card.
      const fire = (id: string, type: string, init: MouseEventInit = {}) => {
        const target = row(id).querySelector(view === 'list' ? 'td span' : 'button') as HTMLElement;
        target.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, ...init }));
        fixture.detectChanges();
      };
      const selected = () => Array.from(fixture.nativeElement.querySelectorAll('[data-file-id][aria-selected="true"]') as NodeListOf<HTMLElement>).map(el => el.dataset['fileId']);
      return { app, row, fire, selected };
    };

    it('Ctrl-click and Cmd-click add and remove items', () => {
      const { fire, selected } = setup();
      fire('1', 'click');
      fire('3', 'click', { ctrlKey: true });
      expect(selected()).toEqual(['1', '3']);
      fire('5', 'click', { metaKey: true });
      expect(selected()).toEqual(['1', '3', '5']);
      fire('3', 'click', { ctrlKey: true });
      expect(selected()).toEqual(['1', '5']);
    });

    it('Shift-click selects the range from the anchor, in either direction', () => {
      const { fire, selected } = setup();
      fire('2', 'click');
      fire('4', 'click', { shiftKey: true });
      expect(selected()).toEqual(['2', '3', '4']);
      fire('1', 'click', { shiftKey: true });
      expect(selected()).toEqual(['1', '2']);
    });

    it('Ctrl+Shift-click adds the range to the selection, and a plain click resets it', () => {
      const { fire, selected } = setup();
      fire('1', 'click');
      fire('3', 'click', { ctrlKey: true });
      fire('5', 'click', { ctrlKey: true, shiftKey: true });
      expect(selected()).toEqual(['1', '3', '4', '5']);
      fire('2', 'click');
      expect(selected()).toEqual(['2']);
    });

    it('double-click with a modifier does not open the item or reset the selection', () => {
      const song = { ...sampleFiles[0], contentType: 'audio/wav', fileSize: 10 };
      const { app, fire, selected } = setup([song, sampleFiles[1], folderItem('d1', 'Docs')]);
      fire('2', 'click');
      fire(song.id, 'click', { ctrlKey: true });
      fire(song.id, 'dblclick', { ctrlKey: true });
      fire('d1', 'dblclick', { shiftKey: true });
      expect(app.previewModalOpen()).toBe(false);
      expect(app.folderPath()).toEqual([]);
      expect(selected()).toEqual(['1', '2']);
    });

    it('double-click without a modifier still opens the item', () => {
      const song = { ...sampleFiles[0], contentType: 'audio/wav', fileSize: 10 };
      const { app, fire } = setup([song]);
      fire(song.id, 'dblclick');
      expect(app.previewModalOpen()).toBe(true);
    });

    it('pressing, right-clicking and dragging a selected row keep the selection', () => {
      const { app, row, fire, selected } = setup();
      fire('1', 'click');
      fire('2', 'click', { ctrlKey: true });

      fire('2', 'pointerdown', { button: 0 });
      expect(selected()).toEqual(['1', '2']);

      fire('2', 'contextmenu');
      expect(selected()).toEqual(['1', '2']);
      expect(app.contextMenu()?.items.map(item => item.id)).toEqual(['1', '2']);
      app.contextMenu.set(null);

      const setData = vi.fn();
      row('2').dispatchEvent(Object.assign(new Event('dragstart', { bubbles: true }), { dataTransfer: { setData } }));
      expect(setData).toHaveBeenCalledWith('text/plain', '1,2');
      expect(selected()).toEqual(['1', '2']);
    });
  });

  describe('keyboard shortcuts', () => {
    const api = environment.apiBaseUrl;
    const setup = () => {
      const app = TestBed.createComponent(App).componentInstance;
      const http = TestBed.inject(HttpTestingController);
      app.page.set('app');
      app.files.set(sampleFiles);
      return { app, http };
    };
    const press = (key: string, init: KeyboardEventInit = {}, target: EventTarget = document): KeyboardEvent => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(event);
      return event;
    };
    // Answers the reload and storage requests that follow a completed bulk action.
    const settle = (http: HttpTestingController) => {
      http.match(r => r.method === 'GET').forEach(r => r.flush([]));
    };

    it('Delete moves the selection to trash', () => {
      const { app, http } = setup();
      app.selectFile('1');
      app.selectFile('2', new MouseEvent('click', { ctrlKey: true }));

      expect(press('Delete').defaultPrevented).toBe(true);
      http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(`${api}/Files/2`).flush(null, { status: 204, statusText: 'No Content' });
      settle(http);
    });

    it('ignores shortcuts while typing in an input', () => {
      const { app, http } = setup();
      app.selectFile('1');
      const input = document.createElement('input');
      document.body.appendChild(input);

      expect(press('Delete', {}, input).defaultPrevented).toBe(false);
      input.remove();
      http.verify();
    });

    it('Ctrl+A selects every item in view', () => {
      const { app } = setup();

      press('a', { ctrlKey: true });

      expect(app.selectedIds().size).toBe(sampleFiles.length);
    });

    it('Ctrl+C then Ctrl+V posts a copy into the current folder', () => {
      const { app, http } = setup();
      app.activeNav.set('my-files');
      app.folderPath.set([{ id: 'f9', name: 'Docs' }]);
      app.selectFile('3');

      press('c', { ctrlKey: true });
      press('v', { ctrlKey: true });

      const req = http.expectOne(`${api}/Files/3/copy`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ folderId: 'f9' });
      req.flush({ id: 'new' }, { status: 201, statusText: 'Created' });
      settle(http);
      expect(app.clipboard()).not.toBeNull();
    });

    it('Ctrl+X then Ctrl+V moves the file and clears the clipboard', () => {
      const { app, http } = setup();
      app.activeNav.set('my-files');
      app.selectFile('3');
      press('x', { ctrlKey: true });
      app.folderPath.set([{ id: 'f9', name: 'Docs' }]);

      press('v', { ctrlKey: true });

      const req = http.expectOne(`${api}/Files/3/folder`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ folderId: 'f9' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      settle(http);
      expect(app.clipboard()).toBeNull();

      press('z', { ctrlKey: true });
      expect(http.expectOne(`${api}/Files/3/folder`).request.body).toEqual({ folderId: null });
    });

    it('Ctrl+Z restores a deleted file', () => {
      const { app, http } = setup();
      app.selectFile('1');
      press('Delete');
      http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
      settle(http);

      expect(press('z', { ctrlKey: true }).defaultPrevented).toBe(true);
      expect(http.expectOne(`${api}/Files/1/restore`).request.method).toBe('POST');
    });

    describe('with folders selected', () => {
      const skipped = 'Folders were skipped. Act on them one at a time.';
      const copyCut = 'Folders were skipped. They cannot be copied or cut.';
      const folder = (id: string): FileItem => ({ ...sampleFiles[0], id, name: `Folder ${id}`, type: 'folder' });
      const setupFolders = (...ids: string[]) => {
        const { app, http } = setup();
        app.folders.set([folder('f1'), folder('f2')]);
        ids.forEach((id, index) => app.selectFile(id, index ? new MouseEvent('click', { ctrlKey: true }) : undefined));
        return { app, http };
      };
      const menuEntry = (app: App, item: FileItem, label: string) => {
        app.showContextMenu(item, new MouseEvent('contextmenu'));
        return app.contextMenu()!.entries.find(entry => entry.label === label);
      };

      it('Delete on a mix trashes the file only and the message survives the reload', () => {
        const { app, http } = setupFolders('f1', '1');

        expect(press('Delete').defaultPrevented).toBe(true);
        http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
        http.expectNone(`${api}/Files/f1`);
        settle(http);

        expect(app.actionError()).toBe(skipped);
      });

      it('Ctrl+C and Ctrl+X on a mix keep the file only and show the message', () => {
        const { app } = setupFolders('f1', '1');

        for (const key of ['c', 'x']) {
          app.actionError.set('');
          press(key, { ctrlKey: true });
          expect(app.clipboard()?.items.map(item => item.id)).toEqual(['1']);
          expect(app.actionError()).toBe(copyCut);
        }
      });

      it('Delete on one folder asks to confirm through deleteFile', () => {
        const { app, http } = setupFolders('f1');

        expect(press('Delete').defaultPrevented).toBe(true);

        expect(app.pendingTrash()?.title).toBe('Delete folder');
        expect(app.actionError()).toBe('');
        http.verify();
      });

      it('Delete on several folders shows the message and runs nothing', () => {
        const { app, http } = setupFolders('f1', 'f2');

        press('Delete');

        expect(app.pendingTrash()).toBeNull();
        expect(app.actionError()).toBe(skipped);
        expect(app.selectedIds().size).toBe(2);
        http.verify();
      });

      it('Ctrl+C on only folders shows the message and leaves the clipboard alone', () => {
        const { app } = setupFolders('f1');

        press('c', { ctrlKey: true });

        expect(app.clipboard()).toBeNull();
        expect(app.actionError()).toBe(copyCut);
      });

      it('the menu sends a lone folder to confirmation, and a mix to trash with the message', () => {
        const { app, http } = setupFolders('f1');
        menuEntry(app, folder('f1'), 'Move to trash')!.run();
        expect(app.pendingTrash()?.title).toBe('Delete folder');
        expect(app.actionError()).toBe('');
        app.pendingTrash.set(null);
        http.verify();

        app.selectFile('1', new MouseEvent('click', { ctrlKey: true }));
        menuEntry(app, folder('f1'), 'Move to trash')!.run();
        http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
        settle(http);
        expect(app.actionError()).toBe(skipped);
      });

      it('the menu on several folders offers no Copy or Cut, and Move to trash shows the message', () => {
        const { app, http } = setupFolders('f1', 'f2');

        expect(menuEntry(app, folder('f1'), 'Copy')).toBeUndefined();
        expect(menuEntry(app, folder('f1'), 'Cut')).toBeUndefined();
        menuEntry(app, folder('f1'), 'Move to trash')!.run();

        expect(app.actionError()).toBe(skipped);
        http.verify();
      });

      it('a files-only selection shows no message', () => {
        const { app, http } = setupFolders('1', '2');

        press('c', { ctrlKey: true });
        press('x', { ctrlKey: true });
        expect(app.actionError()).toBe('');

        press('Delete');
        http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
        http.expectOne(`${api}/Files/2`).flush(null, { status: 204, statusText: 'No Content' });
        settle(http);
        expect(app.actionError()).toBe('');
      });
    });

    it('Shift+Delete deletes forever only in Trash', () => {
      const { app, http } = setup();
      app.selectFile('1');

      expect(press('Delete', { shiftKey: true }).defaultPrevented).toBe(false);
      http.verify();

      app.activeNav.set('trash');
      expect(press('Delete', { shiftKey: true }).defaultPrevented).toBe(true);
      http.verify();
      app.confirmPendingTrash();
      http.expectOne(`${api}/Files/1/permanent`).flush(null, { status: 204, statusText: 'No Content' });
      settle(http);
    });
  });
});

describe('App sign-in notice through the real router', () => {
  const api = environment.apiBaseUrl;
  const setup = async (url: string) => {
    localStorage.removeItem('arcano.auth.token');
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create(url);
    const app = harness.routeDebugElement!.componentInstance as App;
    const form = harness.routeNativeElement!.querySelector('form') as HTMLFormElement;
    const submit = () => form.dispatchEvent(new Event('submit', { cancelable: true }));
    const settled = async () => {
      await harness.fixture.whenStable();
      harness.detectChanges();
      return harness.routeNativeElement!.querySelector('[role="status"]')?.textContent?.trim();
    };
    return { harness, http, app, submit, settled };
  };

  it('shows the sign-up notice once on /signin', async () => {
    const { harness, http, app, submit, settled } = await setup('/signup');
    http.expectOne(`${api}/Auth/setup`).flush({ requiresKey: false });
    app.signUpEmail.set('a@b.co');
    app.signUpPassword.set('secret123');
    submit();
    http.expectOne(`${api}/Auth/register`).flush({});

    expect(await settled()).toBe('Account created. Sign in to continue.');
    expect(TestBed.inject(Router).url).toBe('/signin');
    expect(harness.routeDebugElement!.componentInstance).not.toBe(app);

    await harness.navigateByUrl('/reset');
    await harness.navigateByUrl('/signin');
    expect(await settled()).toBeUndefined();
  });

  it('shows the reset notice once on /signin', async () => {
    const { harness, http, app, submit, settled } = await setup('/reset');
    app.resetKey.set('rk');
    app.resetPassword.set('long-enough');
    app.resetConfirm.set('long-enough');
    submit();
    http.expectOne(`${api}/Auth/reset`).flush(null, { status: 204, statusText: 'No Content' });

    expect(await settled()).toBe('Password reset. Sign in with your new password.');

    await harness.navigateByUrl('/reset');
    await harness.navigateByUrl('/signin');
    expect(await settled()).toBeUndefined();
  });
});
