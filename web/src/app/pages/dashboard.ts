import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { money, num } from '../core/format';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, State],
  template: `
    <header class="page-head">
      <div>
        <p class="eyebrow">Home</p>
        <h1>Dashboard</h1>
        <p class="muted">Where the book of business stands today, and which accounts need attention first.</p>
      </div>
      <div class="head-actions"><a class="button ghost" routerLink="/my-day">Open My Day</a></div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />

    @if (data.value(); as d) {
      <section class="stat-grid" aria-label="Key numbers">
        <a class="stat" routerLink="/accounts"><span>Active accounts</span><strong>{{ d.activeCustomers }}</strong><small>{{ d.atRiskAccounts }} at risk</small></a>
        <div class="stat"><span>Loads per month</span><strong>{{ num(d.monthlyLoads) }}</strong><small>{{ money(d.monthlyRevenue) }} revenue</small></div>
        <div class="stat"><span>Gross margin per month</span><strong>{{ money(d.grossMargin) }}</strong><small>{{ pct(d.grossMargin, d.monthlyRevenue) }} margin</small></div>
        <a class="stat" routerLink="/my-day"><span>Follow-ups due</span><strong>{{ d.followUpsDue }}</strong>
          <small [class.bad-text]="d.followUpsOverdue > 0">{{ d.followUpsOverdue }} overdue</small></a>
        <a class="stat" routerLink="/pipeline"><span>Open quotes</span><strong>{{ d.openOpportunities }}</strong><small>{{ money(d.pipelineValue) }} sent</small></a>
        <a class="stat" routerLink="/reports"><span>Won this month</span><strong>{{ d.quotesWonThisMonth }}</strong><small>{{ money(d.wonValueThisMonth) }}</small></a>
      </section>

      <section class="card">
        <div class="card-head">
          <div><h2>Accounts to work next</h2><p class="muted">Ranked by an explainable score: days since last touch, volume, margin, stage and overdue follow-ups.</p></div>
          <a routerLink="/accounts">All accounts</a>
        </div>
        <ol class="opportunity-list">
          @for (item of d.topOpportunities; track item.customerId) {
            <li>
              <span class="score" [attr.aria-label]="'Score ' + item.priorityScore">{{ item.priorityScore }}</span>
              <div>
                <a class="strong-link" [routerLink]="['/accounts', item.customerId]">{{ item.customerName }}</a>
                <div class="muted small">{{ item.primaryLane }}</div>
                <p>{{ item.whyNow }}</p>
                <p class="next-step"><strong>Next step:</strong> {{ item.recommendedAction }}</p>
              </div>
            </li>
          }
        </ol>
      </section>
    }`
})
export class DashboardPage {
  private readonly api = inject(Api);
  readonly data = load(() => this.api.dashboard());
  readonly money = money;
  readonly num = num;
  pct = (part: number, whole: number) => (whole > 0 ? `${((part / whole) * 100).toFixed(1)}%` : '—');
}
