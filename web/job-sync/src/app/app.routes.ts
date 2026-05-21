import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/applications/applications').then((m) => m.Applications),
  },
];
