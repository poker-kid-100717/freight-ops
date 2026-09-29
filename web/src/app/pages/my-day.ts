import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { dateTime, due, money, tone } from '../core/format';
import { FollowUp } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-my-day',
  imports: [RouterLink, State],
  template: `
    <header class="page-head">
      <div>
        <p class="eyebrow">Home</p>
        <h1>My Day</h1>
        <p class="muted">{{ session.rep()?.name ?? 'Whole team' }} · follow-ups, open quotes and the accounts to call first.
          Change who you're viewing as in the top bar.</p>
      </div>
      <div class="head-actions">
        <button type="button" class="ghost" (click)="actions.logActivity()">Log activity</button>
        <button type="button" (click)="actions.newFollowUp()">New follow-up</button>
      </div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }

    @if (data.value(); as d) {
      <div class="two-col">
        <section class="card">
          <div class="card-head"><h2>Follow-ups</h2><span class="muted small">Next 7 days</span></div>
          @for (group of [{ title: 'Overdue', items: d.overdue }, { title: 'Due today', items: d.dueToday }, { title: 'Upcoming', items: d.upcoming }]; track group.title) {
            <h3 class="list-title">{{ group.title }} <span class="count">{{ group.items.length }}</span></h3>
            <ul class="task-list">
              @for (f of group.items; track f.id) {
                <li [class.overdue]="f.overdue">
                  <button type="button" class="check-button" (click)="complete(f)" [attr.aria-label]="'Mark done: ' + f.description">
                    <span aria-hidden="true">✓</span>
                  </button>
                  <div>
                    <p>{{ f.description }}</p>
                    <p class="muted small"><a [routerLink]="['/accounts', f.customerId]">{{ f.customerName }}</a> · {{ due(f.dueOn, d.today) }}
                      @if (f.repName) { · {{ f.repName }} }</p>
                  </div>
                </li>
              } @empty {
                <li class="empty-row">Nothing {{ group.title.toLowerCase() }}.</li>
              }
            </ul>
          }
        </section>

        <div class="stack">
          <section class="card">
            <div class="card-head"><h2>Call first</h2><a routerLink="/accounts">Accounts</a></div>
            <ul class="compact-list">
              @for (a of d.priorityAccounts; track a.id) {
                <li>
                  <span class="score small-score">{{ a.score }}</span>
                  <div><a [routerLink]="['/accounts', a.id]">{{ a.name }}</a>
                    <div class="muted small">{{ a.daysSinceTouch }} days since last touch · {{ a.stage }}</div></div>
                </li>
              } @empty { <li class="empty-row">No accounts assigned.</li> }
            </ul>
          </section>

          <section class="card">
            <div class="card-head"><h2>Open quotes</h2><a routerLink="/pipeline">Pipeline</a></div>
            <ul class="compact-list">
              @for (q of d.openQuotes; track q.id) {
                <li>
                  <span class="badge" [attr.data-tone]="tone(q.status)">{{ q.status }}</span>
                  <div><a routerLink="/quotes" [queryParams]="{ open: q.id }">{{ q.number }}</a> · {{ q.customerName }}
                    <div class="muted small">{{ q.origin }} → {{ q.destination }} · {{ money(q.rate) }}</div></div>
                </li>
              } @empty { <li class="empty-row">No open quotes.</li> }
            </ul>
          </section>

          <section class="card">
            <div class="card-head"><h2>Recent activity</h2><a routerLink="/activity">Activity Log</a></div>
            <ul class="timeline">
              @for (a of d.recentActivity; track a.id) {
                <li><span class="badge" data-tone="neutral">{{ a.type }}</span>
                  <div><p>{{ a.summary }}</p><p class="muted small"><a [routerLink]="['/accounts', a.customerId]">{{ a.customerName }}</a> · {{ dateTime(a.occurredAt) }}</p></div></li>
              } @empty { <li class="empty-row">No recent activity.</li> }
            </ul>
          </section>
        </div>
      </div>
    }`
})
export class MyDayPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.myDay(this.session.repId()));
  readonly due = due;
  readonly money = money;
  readonly dateTime = dateTime;
  readonly tone = tone;

  async complete(f: FollowUp): Promise<void> {
    this.actionError.set(null);
    try { await this.actions.completeFollowUp(f.id); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }
}
