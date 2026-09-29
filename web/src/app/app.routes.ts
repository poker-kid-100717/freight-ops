import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'dashboard', title: 'Dashboard', loadComponent: () => import('./pages/dashboard').then(m => m.DashboardPage) },
  { path: 'my-day', title: 'My Day', loadComponent: () => import('./pages/my-day').then(m => m.MyDayPage) },
  { path: 'leads', title: 'Leads', loadComponent: () => import('./pages/leads').then(m => m.LeadsPage) },
  { path: 'accounts', title: 'Accounts', loadComponent: () => import('./pages/accounts').then(m => m.AccountsPage) },
  { path: 'accounts/:id', title: 'Account', loadComponent: () => import('./pages/account-detail').then(m => m.AccountDetailPage) },
  { path: 'contacts', title: 'Contacts', loadComponent: () => import('./pages/contacts').then(m => m.ContactsPage) },
  { path: 'pipeline', title: 'Pipeline', loadComponent: () => import('./pages/pipeline').then(m => m.PipelinePage) },
  { path: 'quotes', title: 'Quotes', loadComponent: () => import('./pages/quotes').then(m => m.QuotesPage) },
  { path: 'lanes', title: 'Lanes', loadComponent: () => import('./pages/lanes').then(m => m.LanesPage) },
  { path: 'loads', title: 'Loads', loadComponent: () => import('./pages/loads').then(m => m.LoadsPage) },
  { path: 'carriers', title: 'Carriers', loadComponent: () => import('./pages/carriers').then(m => m.CarriersPage) },
  { path: 'reports', title: 'Reports', loadComponent: () => import('./pages/reports').then(m => m.ReportsPage) },
  { path: 'activity', title: 'Activity Log', loadComponent: () => import('./pages/activity').then(m => m.ActivityPage) },
  { path: 'settings', title: 'Settings', loadComponent: () => import('./pages/settings').then(m => m.SettingsPage) },
  { path: '**', title: 'Not found', loadComponent: () => import('./pages/not-found').then(m => m.NotFoundPage) }
];
