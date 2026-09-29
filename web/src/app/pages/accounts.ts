import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { money, num, tone } from '../core/format';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-accounts',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Sales</p><h1>Accounts</h1><p class="muted">Every customer and prospect, ranked by who needs attention.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newAccount()">New account</button></div>
    </header>

    <div class="toolbar">
      <label class="filter"><span class="sr-only">Search accounts</span>
        <input type="search" placeholder="Search name or city" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
      <label class="filter"><span>Stage</span>
        <select (change)="stage.set($any($event.target).value); page.set(1)">
          <option value="">All stages</option>
          @for (s of session.meta()?.stages ?? []; track s) { <option [value]="s">{{ s }}</option> }
        </select></label>
      <label class="filter"><span>Owner</span>
        <select (change)="owner.set($any($event.target).value); page.set(1)">
          <option value="">Anyone</option>
          @for (r of session.meta()?.reps ?? []; track r.id) { <option [value]="r.id">{{ r.name }}</option> }
        </select></label>
      <label class="filter"><span>Sort</span>
        <select (change)="sort.set($any($event.target).value)">
          <option value="score">Priority score</option><option value="name">Name</option><option value="loads">Loads</option>
          <option value="margin">Margin</option><option value="touch">Longest without a touch</option>
        </select></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Account</th><th scope="col">Stage</th><th scope="col" class="num">Loads / mo</th>
            <th scope="col" class="num">Margin / mo</th><th scope="col">Last touch</th><th scope="col">Owner</th><th scope="col" class="num">Score</th></tr></thead>
          <tbody>
            @for (c of p.items; track c.id) {
              <tr>
                <td><a class="strong-link" [routerLink]="['/accounts', c.id]">{{ c.name }}</a><div class="muted small">{{ c.primaryLane }}</div></td>
                <td><span class="badge" [attr.data-tone]="tone(c.stage)">{{ c.stage }}</span></td>
                <td class="num">{{ num(c.monthlyLoads) }}</td>
                <td class="num">{{ money(c.monthlyGrossMargin) }}</td>
                <td [class.bad-text]="c.daysSinceTouch >= 10">{{ c.daysSinceTouch === 0 ? 'Today' : c.daysSinceTouch + ' days ago' }}
                  @if (c.overdueFollowUps) { <div class="small bad-text">{{ c.overdueFollowUps }} overdue follow-up{{ c.overdueFollowUps > 1 ? 's' : '' }}</div> }</td>
                <td>{{ c.ownerName ?? 'Unassigned' }}</td>
                <td class="num"><span class="score small-score">{{ c.score }}</span></td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="empty-row">No accounts match these filters.</td></tr>
            }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class AccountsPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly search = signal('');
  readonly stage = signal('');
  readonly owner = signal('');
  readonly sort = signal('score');
  readonly page = signal(1);
  readonly data = load(() => this.api.customers({
    search: this.search(), stage: this.stage(), owner: this.owner(), sort: this.sort(), page: this.page(), pageSize: 25
  }));
  readonly money = money;
  readonly num = num;
  readonly tone = tone;
}
