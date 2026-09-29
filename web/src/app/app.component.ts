import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Actions } from './core/actions';
import { Api } from './core/api';
import { SearchHit } from './core/models';
import { Session } from './core/session';
import { FormDrawer } from './shared/form-drawer';
import { Icon } from './shared/icon';

interface NavItem { label: string; path: string; icon: string; }
interface NavGroup { label: string; items: NavItem[]; }

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, FormDrawer],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  readonly session = inject(Session);
  readonly actions = inject(Actions);
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  readonly groups: NavGroup[] = [
    { label: 'Home', items: [
      { label: 'Dashboard', path: '/dashboard', icon: 'dashboard' },
      { label: 'My Day', path: '/my-day', icon: 'day' }
    ] },
    { label: 'Sales', items: [
      { label: 'Leads', path: '/leads', icon: 'lead' },
      { label: 'Accounts', path: '/accounts', icon: 'account' },
      { label: 'Contacts', path: '/contacts', icon: 'contact' },
      { label: 'Pipeline', path: '/pipeline', icon: 'pipeline' },
      { label: 'Quotes', path: '/quotes', icon: 'quote' }
    ] },
    { label: 'Operations', items: [
      { label: 'Lanes', path: '/lanes', icon: 'lane' },
      { label: 'Loads', path: '/loads', icon: 'load' },
      { label: 'Carriers', path: '/carriers', icon: 'carrier' }
    ] },
    { label: 'Insights', items: [
      { label: 'Reports', path: '/reports', icon: 'report' },
      { label: 'Activity Log', path: '/activity', icon: 'activity' }
    ] }
  ];

  readonly collapsed = signal(readFlag('freight-ops.nav-collapsed'));
  readonly mobileOpen = signal(false);
  readonly newOpen = signal(false);
  readonly query = signal('');
  readonly hits = signal<SearchHit[]>([]);
  readonly searchOpen = signal(false);
  readonly activeHit = signal(-1);
  readonly initials = computed(() => this.session.rep()?.name.split(' ').map(p => p[0]).join('') ?? 'All');
  private searchTimer?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    void this.session.loadMeta();
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      this.mobileOpen.set(false);
      this.newOpen.set(false);
      this.searchOpen.set(false);
      document.getElementById('main')?.focus({ preventScroll: true });
    });
  }

  toggleCollapsed(): void {
    this.collapsed.update(v => !v);
    try { localStorage.setItem('freight-ops.nav-collapsed', String(this.collapsed())); } catch { /* ignore */ }
  }

  create(kind: string): void {
    this.newOpen.set(false);
    if (!this.session.meta()) return;
    switch (kind) {
      case 'lead': this.actions.newLead(); break;
      case 'account': this.actions.newAccount(); break;
      case 'contact': this.actions.newContact(); break;
      case 'quote': this.actions.newQuote(); break;
      case 'follow-up': this.actions.newFollowUp(); break;
      case 'activity': this.actions.logActivity(); break;
      case 'lane': this.actions.newLane(); break;
      case 'carrier': this.actions.newCarrier(); break;
    }
  }

  onSearch(value: string): void {
    this.query.set(value);
    this.activeHit.set(-1);
    clearTimeout(this.searchTimer);
    if (value.trim().length < 2) { this.hits.set([]); return; }
    this.searchTimer = setTimeout(async () => {
      try {
        this.hits.set(await this.api.search(value.trim()));
        this.searchOpen.set(true);
      } catch { this.hits.set([]); }
    }, 200);
  }

  onSearchKey(event: KeyboardEvent): void {
    const count = this.hits().length;
    if (event.key === 'ArrowDown' && count) { event.preventDefault(); this.searchOpen.set(true); this.activeHit.update(i => (i + 1) % count); }
    else if (event.key === 'ArrowUp' && count) { event.preventDefault(); this.activeHit.update(i => (i <= 0 ? count - 1 : i - 1)); }
    else if (event.key === 'Enter') {
      const hit = this.hits()[Math.max(this.activeHit(), 0)];
      if (hit) { event.preventDefault(); this.go(hit); }
    } else if (event.key === 'Escape') { this.searchOpen.set(false); }
  }

  go(hit: SearchHit): void {
    this.query.set('');
    this.hits.set([]);
    this.searchOpen.set(false);
    void this.router.navigateByUrl(hit.url);
  }

  setRep(value: string): void {
    this.session.setRep(value || null);
    this.session.changed();
  }

  /** "/" focuses search, as in most web apps; ignored while typing in a field. */
  @HostListener('document:keydown', ['$event'])
  shortcut(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (event.key === '/' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) && !target.isContentEditable) {
      event.preventDefault();
      document.getElementById('global-search')?.focus();
    }
    if (event.key === 'Escape') { this.newOpen.set(false); this.mobileOpen.set(false); }
  }

  @HostListener('document:click', ['$event'])
  outside(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('.new-menu')) this.newOpen.set(false);
    if (!target.closest('.search')) this.searchOpen.set(false);
  }
}

function readFlag(key: string): boolean {
  try { return localStorage.getItem(key) === 'true'; } catch { return false; }
}
