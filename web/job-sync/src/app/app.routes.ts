import { Routes } from '@angular/router';
import { authGuard } from './services/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'otp',
    loadComponent: () => import('./pages/auth/otp/otp').then((m) => m.Otp),
  },
  {
    path: 'applications/:id/edit',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/applications/application-edit/application-edit').then(
        (m) => m.ApplicationEdit,
      ),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/applications/applications').then((m) => m.Applications),
  },
  {
    path: '**',
    canActivate: [authGuard],
    redirectTo: '',
  },
];
