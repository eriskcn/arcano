import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, of, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../environments/environment';

// Calls whose 401 means bad credentials or a failed refresh, not an ended session, the guard-handled session check, and anonymous share links.
const isCredentialCall = (path: string, apiPath: string): boolean =>
  /^\/(Auth\/(login|register|reset|refresh|setup|session)|Public\/)/i.test(path.slice(apiPath.length));

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const apiUrl = new URL(environment.apiBaseUrl, location.origin);
  const requestUrl = new URL(request.url, apiUrl);
  const apiPath = apiUrl.pathname.replace(/\/$/, '');
  const isApiRequest =
    requestUrl.origin === apiUrl.origin &&
    (requestUrl.pathname === apiPath || requestUrl.pathname.startsWith(`${apiPath}/`));
  const send = () => {
    const token = auth.tokenForRequest();
    const sent = !!token && isApiRequest;
    return next(sent ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request).pipe(
      catchError(error => {
        if (sent && error instanceof HttpErrorResponse && error.status === 401 && !isCredentialCall(requestUrl.pathname, apiPath)) {
          auth.sessionExpired(token);
        }
        return throwError(() => error);
      }),
    );
  };

  // The refresh call itself never triggers a refresh. On failure the old token is sent and normal 401 handling applies.
  if (isApiRequest && requestUrl.pathname !== `${apiPath}/Auth/refresh` && auth.needsRefresh()) {
    return auth.refresh().pipe(
      catchError(() => of(undefined)),
      switchMap(send),
    );
  }
  return send();
};
