import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ShareDialogComponent } from './share-dialog.component';
import { environment } from '../environments/environment';

describe('ShareDialogComponent', () => {
  const api = environment.apiBaseUrl;
  const setup = () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(ShareDialogComponent);
    const dialog = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('fileId', '1');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${api}/Share` && r.params.get('fileId') === '1').flush([]);
    return { fixture, dialog, http };
  };

  it('loads links when opened, then creates and revokes a link', () => {
    const { dialog, http } = setup();

    dialog.shareExpiryDays.set(7);
    dialog.createShareLink();
    const create = http.expectOne(`${api}/Share`);
    expect(create.request.body.fileId).toBe('1');
    expect(create.request.body.expiresAtUtc).not.toBeNull();
    const link = { id: 'l1', fileId: '1', token: 'tok', expiresAtUtc: null, createdAtUtc: '2026-07-22T10:00:00Z' };
    create.flush(link);
    expect(dialog.shareLinks()).toEqual([link]);

    dialog.revokeShareLink(link);
    http.expectOne(`${api}/Share/l1`).flush(null);
    expect(dialog.shareLinks()).toEqual([]);
    http.verify();
  });

  it('shows the backend error title', () => {
    const { dialog, http } = setup();

    dialog.createShareLink();
    http.expectOne(`${api}/Share`).flush({ title: 'Expiry must be in the future.' }, { status: 400, statusText: 'Bad Request' });

    expect(dialog.shareError()).toBe('Expiry must be in the future.');
    expect(dialog.shareBusy()).toBe(false);
  });

  it('prefers the backend error description', () => {
    const { dialog, http } = setup();

    dialog.createShareLink();
    http.expectOne(`${api}/Share`).flush({ code: 'Share.Past', description: 'Expiry is in the past.', title: 'x' }, { status: 400, statusText: 'Bad Request' });

    expect(dialog.shareError()).toBe('Expiry is in the past.');
  });

  it('emits close from the modal', () => {
    const { fixture } = setup();
    const closed = vi.fn();
    fixture.componentInstance.close.subscribe(closed);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toHaveBeenCalledTimes(1);
  });
});
