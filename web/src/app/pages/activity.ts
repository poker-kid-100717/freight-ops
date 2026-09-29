import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { dateTime } from '../core/format';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-activity',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Insights</p><h1>Activity Log</h1><p class="muted">Every call, email, meeting and note across all accounts.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.logActivity()">Log activity</button></div>
    </header>
    <div class="toolbar">
      <label class="filter"><span>Rep</span>
        <select (change)="rep.set($any($event.target).value); page.set(1)">
          <option value="">Everyone</option>
          @for (r of session.meta()?.reps ?? []; track r.id) { <option [value]="r.id">{{ r.name }}</option> }
        </select></label>
      <label class="filter"><span>Type</span>
        <select (change)="type.set($any($event.target).value); page.set(1)">
          <option value="">All types</option>
          @for (t of session.meta()?.activityTypes ?? []; track t) { <option [value]="t">{{ t }}</option> }
        </select></label>
      <label class="filter"><span>From</span><input type="date" [value]="from()" (change)="from.set($any($event.target).value); page.set(1)" /></label>
      <label class="filter"><span>To</span><input type="date" [value]="to()" (change)="to.set($any($event.target).value); page.set(1)" /></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">When</th><th scope="col">Type</th><th scope="col">Account</th><th scope="col">Summary</th><th scope="col">Rep</th></tr></thead>
          <tbody>
            @for (a of p.items; track a.id) {
              <tr>
                <td class="nowrap">{{ dateTime(a.occurredAt) }}</td>
                <td><span class="badge" data-tone="neutral">{{ a.type }}</span></td>
                <td><a [routerLink]="['/accounts', a.customerId]">{{ a.customerName }}</a>@if (a.contactName) { <div class="muted small">with {{ a.contactName }}</div> }</td>
                <td>{{ a.summary }}</td>
                <td>{{ a.repName ?? '—' }}</td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty-row">No activity matches these filters.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class ActivityPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly rep = signal('');
  readonly type = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly page = signal(1);
  readonly data = load(() => this.api.activities({
    rep: this.rep(), type: this.type(), from: this.from(), to: this.to(), page: this.page(), pageSize: 30
  }));
  readonly dateTime = dateTime;
}
