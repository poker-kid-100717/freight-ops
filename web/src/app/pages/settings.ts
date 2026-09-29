import { Component, inject } from '@angular/core';
import { Session } from '../core/session';
import { tone } from '../core/format';

@Component({
  selector: 'app-settings',
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Admin</p><h1>Settings</h1><p class="muted">How this demo is configured. Reference lists are fixed in this build.</p></div>
    </header>

    @if (session.meta(); as meta) {
      <div class="settings-grid">
        <section class="card">
          <h2>Data storage</h2>
          <p><span class="badge" [attr.data-tone]="meta.storage.persistent ? 'good' : 'warn'">{{ meta.storage.mode }}</span></p>
          <p class="muted">{{ meta.storage.persistent
            ? 'Changes are saved to PostgreSQL.'
            : 'No database is configured, so the app uses a temporary demo database that resets when the server restarts.' }}</p>
          <p class="muted">Demo reset: {{ meta.demoReset.scheduled ? meta.demoReset.schedule : 'not scheduled' }}.</p>
        </section>

        <section class="card">
          <h2>Sales reps</h2>
          <ul class="compact-list">
            @for (r of meta.reps; track r.id) {
              <li><span class="avatar" aria-hidden="true">{{ initials(r.name) }}</span><div><strong>{{ r.name }}</strong><div class="muted small">{{ r.title }} · {{ r.email }}</div></div></li>
            }
          </ul>
        </section>

        <section class="card">
          <h2>Account stages</h2>
          <div class="chip-row">@for (s of meta.stages; track s) { <span class="badge" [attr.data-tone]="tone(s)">{{ s }}</span> }</div>
          <h2>Quote statuses</h2>
          <p class="muted">Draft → Sent → Won or Lost. A draft can also be marked lost. Winning a prospect's first quote makes it Active.</p>
          <h2>Lead statuses</h2>
          <p class="muted">New → Working → Qualified → Converted (or Disqualified). Only qualified leads convert.</p>
        </section>

        <section class="card">
          <h2>TMS integration</h2>
          <p><span class="badge" [attr.data-tone]="meta.integration.configured ? 'good' : 'neutral'">{{ meta.integration.mode }}</span> {{ meta.integration.provider }}</p>
          <p class="muted">{{ meta.integration.safety }}</p>
        </section>

        <section class="card wide">
          <h2>About this app</h2>
          <p class="muted">A clean-room portfolio freight CRM built with .NET 10, EF Core, PostgreSQL and Angular 22, hosted on Cloudflare Workers
            and Containers. All data is fictional. It is not modelled on any employer's product and contains no employer code, data or screens.</p>
        </section>
      </div>
    } @else if (session.metaError()) {
      <div class="alert" role="alert">{{ session.metaError() }}</div>
    } @else {
      <div class="loading" role="status">Loading…</div>
    }`
})
export class SettingsPage {
  readonly session = inject(Session);
  readonly tone = tone;
  initials = (name: string) => name.split(' ').map(p => p[0]).join('');
}
