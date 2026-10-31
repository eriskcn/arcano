import { TestBed } from '@angular/core/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FileItem } from './app';
import { FileActionsHost, FileActionsService } from './file-actions.service';
import { environment } from '../environments/environment';

const api = environment.apiBaseUrl;
const doc = (id: string): FileItem => ({ id, name: `File ${id}.txt`, type: 'document', size: '1 KB', modified: '', starred: false, shared: false });

describe('FileActionsService', () => {
  let service: FileActionsService;
  let http: HttpTestingController;
  let host: FileActionsHost & { folderId: string | null; selected: Set<string> };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), FileActionsService],
    });
    service = TestBed.inject(FileActionsService);
    http = TestBed.inject(HttpTestingController);
    host = {
      folderId: null,
      selected: new Set(['1']),
      files: () => [doc('1'), doc('2')],
      selectedIds() { return this.selected; },
      currentFolderId() { return this.folderId; },
      reload: vi.fn(),
      refreshStorage: vi.fn(),
      move: vi.fn(),
    };
    service.bind(host);
  });

  const upload = (...files: File[]) => service.uploadFiles(files);

  it('uploads files in order then reloads', () => {
    upload(new File(['a'], 'a.txt'), new File(['b'], 'b.txt'));
    http.expectOne(`${api}/Files/upload`).flush({});
    http.expectOne(`${api}/Files/upload`).flush({});

    expect(service.actionBusy()).toBe(false);
    expect(service.actionError()).toBe('');
    expect(host.reload).toHaveBeenCalledTimes(1);
    expect(host.refreshStorage).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('reports a failed upload and continues', () => {
    upload(new File(['a'], 'a.txt'));
    http.expectOne(`${api}/Files/upload`).flush('no', { status: 400, statusText: 'Bad Request' });

    expect(service.actionError()).toContain('Unable to upload a.txt');
    expect(host.reload).toHaveBeenCalled();
  });

  it('shows upload progress, then clears it when done', () => {
    upload(new File(['a'], 'a.txt'), new File(['b'], 'b.txt'));
    const first = http.expectOne(`${api}/Files/upload`);
    first.event({ type: HttpEventType.UploadProgress, loaded: 1, total: 4 });
    expect(service.uploadState()).toEqual({ name: 'a.txt', index: 1, total: 2, percent: 25 });
    first.flush({});
    http.expectOne(`${api}/Files/upload`).flush({});
    expect(service.uploadState()).toBeNull();
  });

  it('switches to the chunked upload above 8 MB', () => {
    const big = new File([new Uint8Array(8 * 1024 * 1024 + 1)], 'big.bin');
    upload(big);

    http.expectNone(`${api}/Files/upload`);
    expect(http.match(() => true).length).toBeGreaterThan(0);
  });

  it('shows a too-large message when the server answers 413', () => {
    upload(new File(['a'], 'a.txt'));
    http.expectOne(`${api}/Files/upload`).flush('', { status: 413, statusText: 'Payload Too Large' });

    expect(service.actionError()).toBe('File is too large.');
  });

  it('shows the quota message when an upload exceeds the storage limit', () => {
    upload(new File(['a'], 'a.txt'));
    http.expectOne(`${api}/Files/upload`).flush(
      { code: 'Storage.QuotaExceeded', message: 'Not enough storage space.' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(service.actionError()).toBe('Not enough storage space.');
  });

  it('ignores an upload while another action is running', () => {
    service.actionBusy.set(true);
    upload(new File(['a'], 'a.txt'));
    http.verify();
  });

  it('copies to the clipboard and pastes a copy into the current folder, then undoes it', () => {
    service.toClipboard('copy');
    host.folderId = 'f9';
    expect(service.paste()).toBe(true);
    const req = http.expectOne(`${api}/Files/1/copy`);
    expect(req.request.body).toEqual({ folderId: 'f9' });
    req.flush({ id: 'new' });

    expect(service.canUndo).toBe(true);
    service.undo();
    expect(http.expectOne(`${api}/Files/new`).request.method).toBe('DELETE');
  });

  it('cut-paste into the same folder is refused, into another folder moves and clears the clipboard', () => {
    service.toClipboard('cut');
    expect(service.paste()).toBe(false);

    host.folderId = 'f9';
    expect(service.paste()).toBe(true);
    expect(host.move).toHaveBeenCalledWith([doc('1')], 'f9', null);
    expect(service.clipboard()).toBeNull();
  });

  it('trashes the selection and undoes it with a restore', () => {
    service.trashSelected();
    http.expectOne(`${api}/Files/1`).flush(null, { status: 204, statusText: 'No Content' });
    service.undo();
    expect(http.expectOne(`${api}/Files/1/restore`).request.method).toBe('POST');
  });

  it('keeps only the last 20 undo steps', () => {
    for (let i = 0; i < 25; i++) service.pushUndo(() => {});
    let count = 0;
    while (service.canUndo) { service.undo(); count++; }
    expect(count).toBe(20);
  });
});
