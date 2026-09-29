import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { date, tone } from '../core/format';
import { Lead } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-leads',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Sales</p><h1>Leads</h1><p class="muted">Qualify new interest, then convert it into an account.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newLead()">New lead</button></div>
    </header>

    <div class="toolbar">
      <div class="tabs" role="tablist" aria-label="Lead status">
        @for (s of statuses; track s) {
          <button type="button" role="tab" [attr.aria-selected]="status() === s" [class.active]="status() === s" (click)="status.set(s); page.set(1)">{{ s || 'All' }}</button>
        }
      </div>
      <label class="filter"><span class="sr-only">Search leads</span>
        <input type="search" placeholder="Search company or contact" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Company</th><th scope="col">Contact</th><th scope="col">Source</th><th scope="col">Status</th>
            <th scope="col">Owner</th><th scope="col">Created</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (l of p.items; track l.id) {
              <tr [class.highlight]="l.id === open()">
                <td><strong>{{ l.company }}</strong>@if (l.notes) {<div class="muted small clamp">{{ l.notes }}</div>}</td>
                <td>{{ l.contactName }}<div class="muted small">{{ l.email }}</div></td>
                <td>{{ l.source }}</td>
                <td><span class="badge" [attr.data-tone]="tone(l.status)">{{ l.status }}</span></td>
                <td>{{ l.ownerName ?? 'Unassigned' }}</td>
                <td>{{ date(l.createdAt) }}</td>
                <td class="row-actions">
                  @if (l.status === 'Converted') {
                    <a [routerLink]="['/accounts', l.convertedCustomerId]">View account</a>
                  } @else {
                    @if (l.status === 'Qualified') { <button type="button" class="small" (click)="actions.convertLead(l)">Convert</button> }
                    <button type="button" class="ghost small" (click)="actions.editLead(l)">Edit</button>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="empty-row">No leads match. <button type="button" class="link" (click)="actions.newLead()">Add a lead</button></td></tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class LeadsPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  private readonly session = inject(Session);
  readonly open = input<string>();
  readonly statuses = ['', 'New', 'Working', 'Qualified', 'Disqualified', 'Converted'];
  readonly status = signal('');
  readonly search = signal('');
  readonly page = signal(1);
  readonly data = load(() => this.api.leads({ status: this.status(), search: this.search(), page: this.page(), pageSize: 25 }));
  readonly date = date;
  readonly tone = tone;

  constructor() {
    // /leads?open=<id> (from global search) opens that lead for editing.
    effect(async () => {
      const id = this.open();
      if (!id || !this.session.meta()) return;
      const lead: Lead = await this.api.lead(id);
      if (lead.status !== 'Converted') this.actions.editLead(lead);
    });
  }
}
