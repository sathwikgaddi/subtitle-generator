import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth-guard';
import { Shell } from './core/layout/shell/shell';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./marketing/landing/landing').then((m) => m.Landing),
  },
  {
    path: 'login',
    loadComponent: () => import('./auth/login/login').then((m) => m.Login),
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/register/register').then((m) => m.Register),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      {
        path: 'videos',
        loadComponent: () => import('./videos/video-list/video-list').then((m) => m.VideoList),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
