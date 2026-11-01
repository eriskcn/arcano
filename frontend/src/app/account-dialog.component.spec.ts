import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountDialogComponent } from './account-dialog.component';
import { environment } from '../environments/environment';

describe('AccountDialogComponent', () => {
  const api = environment.apiBaseUrl;
  const setup = () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const fixture = TestBed.createComponent(AccountDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    const dialog = fixture.componentInstance;
    const signedOut = vi.fn();
    dialog.signedOut.subscribe(signedOut);
    return { dialog, http: TestBed.inject(HttpTestingController), signedOut };
  };

  it('validates the new password on the client', () => {
    const { dialog, http } = setup();
    dialog.currentPassword.set('old-password');

    dialog.newPassword.set('short');
    dialog.confirmPassword.set('short');
    dialog.changePassword();
    expect(dialog.error()).toContain('at least 8');

    dialog.newPassword.set('long-enough');
    dialog.confirmPassword.set('different1');
    dialog.changePassword();
    expect(dialog.error()).toContain('do not match');
    http.verify();
  });

  it('shows the server message when the current password is wrong', () => {
    const { dialog, http } = setup();
    dialog.currentPassword.set('wrong');
    dialog.newPassword.set('long-enough');
    dialog.confirmPassword.set('long-enough');

    dialog.changePassword();
    http.expectOne(`${api}/Account/password`).flush(
      { code: 'Account.PasswordIncorrect', message: 'Current password is incorrect.' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(dialog.error()).toBe('Current password is incorrect.');
    expect(dialog.busy()).toBe(false);
  });

  it('signs out everywhere', () => {
    const { dialog, http, signedOut } = setup();

    dialog.signOutEverywhere();
    http.expectOne(`${api}/Auth/logout`).flush(null, { status: 204, statusText: 'No Content' });

    expect(signedOut).toHaveBeenCalled();
  });

  it('deletes the account with the password and signs out', () => {
    const { dialog, http, signedOut } = setup();
    dialog.confirmingDelete.set(true);
    dialog.deletePassword.set('secret-pass');

    dialog.deleteAccount();
    const request = http.expectOne(`${api}/Account`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toEqual({ password: 'secret-pass' });
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(signedOut).toHaveBeenCalled();
  });
});
