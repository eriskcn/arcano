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
    http.expectOne(r => r.url === `${api}/UserShares` && r.params.get('fileId') === '1').flush([]);
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

  it('shares with a person by email and removes access', () => {
    const { dialog, http } = setup();

    dialog.personEmail.set(' bob@example.com ');
    dialog.sharePerson();
    const create = http.expectOne(`${api}/UserShares`);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ fileId: '1', email: 'bob@example.com' });
    const share = { id: 's1', kind: 'file', itemId: '1', name: 'a.txt', recipientEmail: 'bob@example.com', createdAtUtc: '2026-07-22T10:00:00Z' };
    create.flush(share);
    expect(dialog.people().map(p => p.id)).toEqual(['s1']);

    dialog.personEmail.set('bob@example.com');
    dialog.sharePerson();
    http.expectOne(`${api}/UserShares`).flush(share); // idempotent: same share again
    expect(dialog.people().length).toBe(1);

    dialog.removePerson(share);
    const del = http.expectOne(`${api}/UserShares/s1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    expect(dialog.people()).toEqual([]);
  });

  it('submits the person form (what Enter does) only when the Share button is enabled', () => {
    const { fixture, dialog, http } = setup();
    const el = fixture.nativeElement as HTMLElement;
    const button = el.querySelector('form button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    dialog.personEmail.set('bob@example.com');
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne(`${api}/UserShares`).flush({ id: 's1', recipientEmail: 'bob@example.com' });
  });

  it('shows the backend message for a self-share and a fallback for an unknown email', () => {
    const { dialog, http } = setup();

    dialog.personEmail.set('me@example.com');
    dialog.sharePerson();
    http.expectOne(`${api}/UserShares`).flush({ code: 'UserShare.Self', message: 'You cannot share with yourself.' }, { status: 400, statusText: 'Bad Request' });
    expect(dialog.shareError()).toBe('You cannot share with yourself.');

    dialog.personEmail.set('nobody@example.com');
    dialog.sharePerson();
    http.expectOne(`${api}/UserShares`).flush(null, { status: 404, statusText: 'Not Found' });
    expect(dialog.shareError()).toBe('No account found with that email.');
  });

  it('works for folders: lists people by folderId and hides the public link section', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(ShareDialogComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('folderId', 'f1');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${api}/UserShares` && r.params.get('folderId') === 'f1').flush([{ id: 's1', recipientEmail: 'bob@example.com', createdAtUtc: '' }]);
    fixture.detectChanges();

    expect(document.body.textContent).toContain('bob@example.com');
    expect(document.body.textContent).not.toContain('Create link');
    http.verify();
  });
});
