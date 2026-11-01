import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminDialogComponent } from './admin-dialog.component';
import { environment } from '../environments/environment';

describe('AdminDialogComponent', () => {
  const base = `${environment.apiBaseUrl}/Admin`;
  const GB = 1024 ** 3;
  const alice = { id: 'u1', email: 'alice@x.test', isAdmin: true, isDisabled: false, usedBytes: GB, quotaBytes: null, effectiveQuotaBytes: 10 * GB, createdAtUtc: '2026-01-01T00:00:00Z' };
  const bob = { ...alice, id: 'u2', email: 'bob@x.test', isAdmin: false, isDisabled: true, quotaBytes: 2 * GB, effectiveQuotaBytes: 2 * GB };
  const key = { id: 'k1', type: 'Join', quotaBytes: 5 * GB, userEmail: null, expiresAtUtc: null, createdAtUtc: '2026-01-01T00:00:00Z' };

  const setup = () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(AdminDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${base}/users`).flush([alice, bob]);
    http.expectOne(`${base}/keys`).flush([key]);
    fixture.detectChanges();
    const reload = () => {
      http.expectOne(`${base}/users`).flush([alice, bob]);
      http.expectOne(`${base}/keys`).flush([key]);
    };
    return { fixture, dialog: fixture.componentInstance, http, el: fixture.nativeElement as HTMLElement, reload };
  };

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders members with sizes, the disabled badge and open keys', () => {
    const { el } = setup();
    const rows = el.querySelectorAll('[data-member]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('alice@x.test');
    expect(rows[0].textContent).toContain('1 GB of 10 GB');
    expect(rows[0].textContent).toContain('server default');
    expect(rows[0].querySelector('[data-disabled]')).toBeNull();
    expect(rows[1].querySelector('[data-disabled]')).not.toBeNull();
    expect(el.querySelectorAll('[data-key]').length).toBe(1);
  });

  it('saves a quota in GB, and empty means the server default', () => {
    const { dialog, http, reload } = setup();
    dialog.editQuota(bob);
    expect(dialog.quotaInput()).toBe(2);
    dialog.quotaInput.set(3);
    dialog.saveQuota(bob);
    const put = http.expectOne(`${base}/users/u2/quota`);
    expect(put.request.body).toEqual({ quotaBytes: 3 * GB });
    put.flush(null, { status: 204, statusText: 'No Content' });
    reload();

    dialog.quotaInput.set('');
    dialog.saveQuota(bob);
    const reset = http.expectOne(`${base}/users/u2/quota`);
    expect(reset.request.body).toEqual({ quotaBytes: null });
    reset.flush(null, { status: 204, statusText: 'No Content' });
    reload();
  });

  it('enables a disabled member and deletes only after confirming', () => {
    const { dialog, http, reload } = setup();
    dialog.toggleDisabled(bob);
    http.expectOne(`${base}/users/u2/enable`).flush(null, { status: 204, statusText: 'No Content' });
    reload();

    dialog.pendingDelete.set(bob);
    http.expectNone(`${base}/users/u2`);
    dialog.confirmDelete();
    const del = http.expectOne(`${base}/users/u2`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    reload();
  });

  it('shows a new key once with a copy button', async () => {
    const { fixture, dialog, http, el, reload } = setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    dialog.joinQuota.set(5);
    dialog.joinDays.set(7);

    dialog.createJoinKey();
    const post = http.expectOne(`${base}/keys`);
    expect(post.request.body).toEqual({ type: 'Join', quotaBytes: 5 * GB, expiresInDays: 7 });
    post.flush({ id: 'k2', key: 'plain-secret', type: 'Join', expiresAtUtc: null }, { status: 201, statusText: 'Created' });
    http.expectOne(`${base}/keys`).flush([key]);
    fixture.detectChanges();

    expect((el.querySelector('[data-new-key] input') as HTMLInputElement).value).toBe('plain-secret');
    expect(el.querySelector('[data-new-key]')?.textContent).toContain('cannot be shown again');
    await dialog.copyKey('plain-secret');
    expect(writeText).toHaveBeenCalledWith('plain-secret');
    expect(dialog.copied()).toBe(true);

    // A reopen reloads the lists and drops the plain key.
    fixture.componentRef.setInput('open', false);
    fixture.detectChanges();
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    reload();
    expect(dialog.newKey()).toBeNull();
  });

  it('reports a failed copy and creates a reset key from a member', async () => {
    const { dialog, http } = setup();
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('no')) }, configurable: true });
    await dialog.copyKey('x');
    expect(dialog.copyFailed()).toBe(true);

    dialog.createKey({ type: 'Reset', userId: 'u2' });
    const post = http.expectOne(`${base}/keys`);
    expect(post.request.body).toEqual({ type: 'Reset', userId: 'u2' });
    post.flush({ id: 'k3', key: 'r', type: 'Reset', expiresAtUtc: null }, { status: 201, statusText: 'Created' });
    http.expectOne(`${base}/keys`).flush([]);
    expect(dialog.newKey()?.key).toBe('r');
  });

  it('revokes a key', () => {
    const { dialog, http, reload } = setup();
    dialog.revoke({ ...key, type: 'Join' });
    const del = http.expectOne(`${base}/keys/k1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    reload();
  });

  it('explains the last-admin error', () => {
    const { dialog, http } = setup();
    dialog.toggleDisabled(alice);
    http.expectOne(`${base}/users/u1/disable`).flush(
      { code: 'Admin.LastAdmin', message: 'x' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(dialog.error()).toBe('The last admin cannot be deleted or disabled.');
    expect(dialog.busy()).toBe(false);
  });
});
