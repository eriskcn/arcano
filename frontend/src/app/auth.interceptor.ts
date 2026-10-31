import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, of, switchMap } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../environments/environment';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const apiUrl = new URL(environment.apiBaseUrl, 'http://localhost');
  const requestUrl = new URL(request.url, apiUrl);
  const apiPath = apiUrl.pathname.replace(/\/$/, '');
  const isApiRequest =
    requestUrl.origin === apiUrl.origin &&
    (requestUrl.pathname === apiPath || requestUrl.pathname.startsWith(`${apiPath}/`));
  const send = () => {
    const token = auth.tokenForRequest();
    return next(token && isApiRequest ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request);
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
