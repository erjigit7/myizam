import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/chat/chat').then(m => m.ChatPage) },
  { path: 'laws', loadComponent: () => import('./pages/laws/laws').then(m => m.LawsPage) },
  { path: 'about', loadComponent: () => import('./pages/about/about').then(m => m.AboutPage) },
  { path: '**', redirectTo: '' },
];
