import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'send',
    pathMatch: 'full'
  },
  {
    path: 'send',
    loadComponent: () =>
      import('./features/send/pages/send/send')
        .then(m => m.Send)
  }
];