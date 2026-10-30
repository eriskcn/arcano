import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

export const authenticatedGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(AuthService).validateSession().pipe(
    map(isAuthenticated =>
      isAuthenticated
        ? true
        : router.createUrlTree(['/signin'], { queryParams: { returnUrl: state.url } }),
    ),
  );
};

export const anonymousOnlyGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService).validateSession().pipe(
    map(isAuthenticated => isAuthenticated ? router.createUrlTree(['/app']) : true),
  );
};
