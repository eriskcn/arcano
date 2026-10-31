import { Routes } from '@angular/router';
import { App } from './app';
import { SharePageComponent } from './share-page.component';
import { anonymousOnlyGuard, authenticatedGuard } from './auth.guards';

export const routes: Routes = [
  { path: 'signin', component: App, canActivate: [anonymousOnlyGuard] },
  { path: 'signup', component: App, canActivate: [anonymousOnlyGuard] },
  { path: 'app', component: App, canActivate: [authenticatedGuard] },
  { path: 'share/:token', component: SharePageComponent },
  { path: '', pathMatch: 'full', redirectTo: 'app' },
  { path: '**', redirectTo: 'app' },
];
