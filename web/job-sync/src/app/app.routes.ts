import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'applications/:id/edit',
    loadComponent: () =>
      import('./pages/applications/application-edit/application-edit').then(
        (m) => m.ApplicationEdit,
      ),
  },
  {
    path: '',
    loadComponent: () => import('./pages/applications/applications').then((m) => m.Applications),
  },
];
