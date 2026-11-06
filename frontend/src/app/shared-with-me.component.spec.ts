import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../environments/environment';
import { SharedWithMeComponent } from './shared-with-me.component';

describe('SharedWithMeComponent', () => {
  const api = environment.apiBaseUrl;
  const folderShare = { id: 's1', kind: 'folder', itemId: 'f1', name: 'Photos', sizeBytes: null, contentType: null, ownerEmail: 'owner@example.com', createdAtUtc: '' };
  const fileShare = { id: 's2', kind: 'file', itemId: 'x1', name: 'a.txt', sizeBytes: 5, contentType: 'text/plain', ownerEmail: 'owner@example.com', createdAtUtc: '' };

  const setup = () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(SharedWithMeComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(`${api}/Shared`).flush([folderShare, fileShare]);
    fixture.detectChanges();
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
    return { fixture, cmp: fixture.componentInstance, http, text };
  };

  it('shows Loading instead of the empty message until the first load completes', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(SharedWithMeComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text()).toContain('Loading...');
    expect(text()).not.toContain('Nothing has been shared');

    http.expectOne(`${api}/Shared`).flush([]);
    fixture.detectChanges();
    expect(text()).not.toContain('Loading...');
    expect(text()).toContain('Nothing has been shared');
  });

  it('lists incoming shares with owner email and only read-only actions', () => {
    const { text } = setup();

    expect(text()).toContain('owner@example.com');
    for (const word of ['Rename', 'Delete', 'Move', 'Upload', 'Star', 'trash']) {
      expect(text()).not.toContain(word);
    }
    expect(text()).toContain('Download');
    expect(text()).toContain('Remove');
  });

  it('browses into a shared folder and back through the breadcrumb', () => {
    const { fixture, cmp, http, text } = setup();

    cmp.openShare(folderShare as never);
    const root = http.expectOne(r => r.url === `${api}/Shared/s1/children`);
    expect(root.request.params.has('folderId')).toBe(false);
    root.flush({ folders: [{ id: 'f2', name: 'Trips' }], files: [{ id: 'x2', fileName: 'b.png', sizeBytes: 10, contentType: 'image/png', createdAtUtc: '' }] });
    fixture.detectChanges();
    expect(text()).toContain('Trips');
    expect(text()).toContain('b.png');

    cmp.openFolder({ id: 'f2', name: 'Trips' });
    const sub = http.expectOne(r => r.url === `${api}/Shared/s1/children`);
    expect(sub.request.params.get('folderId')).toBe('f2');
    sub.flush({ folders: [], files: [] });
    fixture.detectChanges();
    expect(text()).toContain('Shared with me/Photos/Trips');

    cmp.backToList();
    http.expectOne(`${api}/Shared`).flush([folderShare]);
    expect(cmp.share()).toBeNull();
    expect(cmp.shares().length).toBe(1);
  });

  it('hides Load more after going back from a full page', () => {
    const { fixture, cmp, http, text } = setup();
    const files = Array.from({ length: 100 }, (_, i) => ({ id: `x${i}`, fileName: `f${i}`, sizeBytes: 1, contentType: 'text/plain', createdAtUtc: '' }));

    cmp.openShare(folderShare as never);
    http.expectOne(r => r.url === `${api}/Shared/s1/children`).flush({ folders: [], files });
    fixture.detectChanges();
    expect(text()).toContain('Load more');

    cmp.backToList();
    http.expectOne(`${api}/Shared`).flush([folderShare]);
    fixture.detectChanges();
    expect(text()).not.toContain('Load more');
  });

  it('removes a share only after confirming', () => {
    const { fixture, cmp, http } = setup();

    cmp.pendingRemove.set(fileShare as never);
    fixture.detectChanges();
    http.expectNone(`${api}/UserShares/s2`);

    cmp.confirmRemove();
    const req = http.expectOne(`${api}/UserShares/s2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(cmp.shares().map(s => s.id)).toEqual(['s1']);
  });

  it('previews a shared file from the shared download URL', () => {
    const { fixture, cmp, http } = setup();

    cmp.preview('s2', 'x1', 'a.txt', 'text/plain', 5);
    fixture.detectChanges();

    const req = http.expectOne(`${api}/Shared/s2/files/x1/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['hello']));
  });

  it('explains a shared folder with too many files, read from the Blob error body', async () => {
    const { cmp, http } = setup();
    const fail = (body: string) => {
      cmp.downloadFolder('s1', 'f1', 'Photos');
      http.expectOne(`${api}/Shared/s1/folders/f1/download`).flush(new Blob([body], { type: 'application/json' }), { status: 400, statusText: 'Bad Request' });
    };

    fail(JSON.stringify({ code: 'Folder.TooLarge', message: 'Folder holds more than 5000 files.' }));
    await vi.waitFor(() => expect(cmp.error()).toBe('This folder has too many files to download as a zip.'));

    for (const body of [JSON.stringify({ code: 'Folder.NotFound', message: 'Not found.' }), '<html>Bad Gateway</html>', 'null']) {
      fail(body);
      await vi.waitFor(() => expect(cmp.error()).toBe('Unable to download Photos.zip. Please try again.'));
    }
  });
});
