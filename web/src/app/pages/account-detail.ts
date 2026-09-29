import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { date, dateTime, day, due, money, num, tone, todayIso } from '../core/format';
import { Lane, Quote } from '../core/models';
import { load } from '../shared/load';
import { State } from '../shared/state';

type Tab = 'overview' | 'contacts' | 'activity' | 'follow-ups' | 'quotes' | 'lanes';

@Component({
  selector: 'app-account-detail',
  imports: [RouterLink, State],
  template: `
    <nav class="breadcrumbs" aria-label="Breadcrumb"><a routerLink="/accounts">Accounts</a><span aria-hidden="true">/</span>
      <span aria-current="page">{{ data.value()?.customer?.name ?? 'Account' }}</span></nav>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }

    @if (data.value(); as d) {
      <section class="card account-header">
        <div class="account-title">
          <div>
            <h1>{{ d.customer.name }}</h1>
            <p class="muted">{{ d.customer.primaryLane }} · Owner: {{ d.customer.ownerName ?? 'Unassigned' }}</p>
          </div>
          <span class="badge large" [attr.data-tone]="tone(d.customer.stage)">{{ d.customer.stage }}</span>
        </div>
        <dl class="kpis">
          <div><dt>Priority score</dt><dd><span class="score small-score">{{ d.customer.score }}</span></dd></div>
          <div><dt>Loads / month</dt><dd>{{ num(d.customer.monthlyLoads) }}</dd></div>
          <div><dt>Revenue / month</dt><dd>{{ money(d.customer.monthlyRevenue) }}</dd></div>
          <div><dt>Margin / month</dt><dd>{{ money(d.customer.monthlyGrossMargin) }}</dd></div>
          <div><dt>Last touch</dt><dd [class.bad-text]="d.customer.daysSinceTouch >= 10">{{ d.customer.daysSinceTouch === 0 ? 'Today' : d.customer.daysSinceTouch + ' days ago' }}</dd></div>
        </dl>
        <div class="button-row">
          <button type="button" (click)="actions.logActivity(d.customer.id, d.contacts)">Log activity</button>
          <button type="button" class="ghost" (click)="actions.newFollowUp(d.customer.id)">New follow-up</button>
          <button type="button" class="ghost" (click)="actions.newQuote(d.customer.id)">New quote</button>
          <button type="button" class="ghost" (click)="actions.editAccount(d.customer, d.notes)">Edit account</button>
        </div>
      </section>

      <div class="tabs underline" role="tablist" aria-label="Account sections">
        @for (t of tabs; track t.id) {
          <button type="button" role="tab" [id]="'tab-' + t.id" [attr.aria-selected]="tab() === t.id" [attr.aria-controls]="'panel-' + t.id"
                  [class.active]="tab() === t.id" (click)="tab.set(t.id)">{{ t.label }} <span class="count">{{ count(t.id) }}</span></button>
        }
      </div>

      <section class="card" role="tabpanel" [id]="'panel-' + tab()" [attr.aria-labelledby]="'tab-' + tab()">
        @switch (tab()) {
          @case ('overview') {
            <div class="two-col flat">
              <div>
                <h2>Why now</h2>
                <p>{{ d.customer.whyNow }}</p>
                <p class="next-step"><strong>Next step:</strong> {{ d.customer.recommendedAction }}</p>
                <h2>Notes</h2>
                <p class="pre">{{ d.notes || 'No notes yet.' }}</p>
                <p class="muted small">Customer since {{ date(d.createdAt) }} · updated {{ dateTime(d.updatedAt) }}</p>
              </div>
              <div>
                <h2>Open follow-ups</h2>
                <ul class="task-list">
                  @for (f of openFollowUps(); track f.id) {
                    <li [class.overdue]="f.overdue">
                      <button type="button" class="check-button" (click)="complete(f.id)" [attr.aria-label]="'Mark done: ' + f.description"><span aria-hidden="true">✓</span></button>
                      <div><p>{{ f.description }}</p><p class="muted small">{{ due(f.dueOn, today) }}</p></div>
                    </li>
                  } @empty { <li class="empty-row">No open follow-ups.</li> }
                </ul>
                <h2>Latest activity</h2>
                <ul class="timeline">
                  @for (a of d.activities.slice(0, 4); track a.id) {
                    <li><span class="badge" data-tone="neutral">{{ a.type }}</span><div><p>{{ a.summary }}</p><p class="muted small">{{ dateTime(a.occurredAt) }}</p></div></li>
                  } @empty { <li class="empty-row">No activity yet.</li> }
                </ul>
              </div>
            </div>
          }
          @case ('contacts') {
            <div class="card-head"><h2>Contacts</h2><button type="button" class="small" (click)="actions.newContact(d.customer.id)">Add contact</button></div>
            <ul class="people">
              @for (c of d.contacts; track c.id) {
                <li>
                  <div><strong>{{ c.name }}</strong> @if (c.isPrimary) { <span class="badge" data-tone="good">Primary</span> }
                    <div class="muted small">{{ c.role ?? 'Contact' }}</div></div>
                  <div class="contact-links">
                    @if (c.email) { <a [href]="'mailto:' + c.email">{{ c.email }}</a> }
                    @if (c.phone) { <a [href]="'tel:' + c.phone">{{ c.phone }}</a> }
                  </div>
                  <button type="button" class="ghost small" (click)="actions.editContact(c)">Edit</button>
                </li>
              } @empty { <li class="empty-row">No contacts yet.</li> }
            </ul>
          }
          @case ('activity') {
            <div class="card-head"><h2>Activity</h2><button type="button" class="small" (click)="actions.logActivity(d.customer.id, d.contacts)">Log activity</button></div>
            <ul class="timeline">
              @for (a of d.activities; track a.id) {
                <li><span class="badge" data-tone="neutral">{{ a.type }}</span>
                  <div><p>{{ a.summary }}</p><p class="muted small">{{ dateTime(a.occurredAt) }}
                    @if (a.contactName) { · with {{ a.contactName }} } @if (a.repName) { · {{ a.repName }} }</p></div></li>
              } @empty { <li class="empty-row">No activity yet.</li> }
            </ul>
          }
          @case ('follow-ups') {
            <div class="card-head"><h2>Follow-ups</h2><button type="button" class="small" (click)="actions.newFollowUp(d.customer.id)">New follow-up</button></div>
            <ul class="task-list">
              @for (f of d.followUps; track f.id) {
                <li [class.overdue]="f.overdue" [class.done]="f.status === 'Done'">
                  @if (f.status === 'Open') {
                    <button type="button" class="check-button" (click)="complete(f.id)" [attr.aria-label]="'Mark done: ' + f.description"><span aria-hidden="true">✓</span></button>
                  } @else { <span class="check-button done" aria-hidden="true">✓</span> }
                  <div><p>{{ f.description }}</p>
                    <p class="muted small">{{ f.status === 'Done' ? 'Done ' + date(f.completedAt) : due(f.dueOn, today) }} · {{ f.repName ?? 'Unassigned' }}</p></div>
                </li>
              } @empty { <li class="empty-row">No follow-ups yet.</li> }
            </ul>
          }
          @case ('quotes') {
            <div class="card-head"><h2>Quotes</h2><button type="button" class="small" (click)="actions.newQuote(d.customer.id)">New quote</button></div>
            <div class="table-wrap flat">
              <table>
                <thead><tr><th scope="col">Quote</th><th scope="col">Route</th><th scope="col">Status</th><th scope="col" class="num">Rate</th>
                  <th scope="col" class="num">Margin</th><th scope="col">Created</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
                <tbody>
                  @for (q of d.quotes; track q.id) {
                    <tr>
                      <td><strong>{{ q.number }}</strong></td><td>{{ q.origin }} → {{ q.destination }}<div class="muted small">{{ q.equipment }}</div></td>
                      <td><span class="badge" [attr.data-tone]="tone(q.status)">{{ q.status }}</span></td>
                      <td class="num">{{ money(q.rate) }}</td><td class="num">{{ money(q.margin) }}</td><td>{{ day(q.createdAt) }}</td>
                      <td class="row-actions">
                        @if (q.status === 'Draft') { <button type="button" class="small" (click)="move(q, 'send')">Send</button>
                          <button type="button" class="ghost small" (click)="actions.editQuote(q)">Edit</button> }
                        @if (q.status === 'Sent') { <button type="button" class="small" (click)="move(q, 'win')">Won</button>
                          <button type="button" class="ghost small" (click)="move(q, 'lose')">Lost</button> }
                      </td>
                    </tr>
                  } @empty { <tr><td colspan="7" class="empty-row">No quotes yet.</td></tr> }
                </tbody>
              </table>
            </div>
          }
          @case ('lanes') {
            <div class="card-head"><h2>Lanes</h2><button type="button" class="small" (click)="actions.newLane(d.customer.id)">Add lane</button></div>
            <div class="table-wrap flat">
              <table>
                <thead><tr><th scope="col">Route</th><th scope="col">Equipment</th><th scope="col" class="num">Loads / mo</th>
                  <th scope="col" class="num">Target rate</th><th scope="col" class="num">Won</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
                <tbody>
                  @for (l of d.lanes; track l.id) {
                    <tr><td>{{ l.origin }} → {{ l.destination }}</td><td>{{ l.equipment }}</td><td class="num">{{ l.estimatedLoadsPerMonth }}</td>
                      <td class="num">{{ money(l.targetRate) }}</td><td class="num">{{ l.quotesWon }}</td>
                      <td class="row-actions"><button type="button" class="small" (click)="actions.newQuote(d.customer.id, l)">Quote</button>
                        <button type="button" class="ghost small" (click)="actions.editLane(l)">Edit</button>
                        <button type="button" class="ghost small danger" (click)="removeLane(l)">Delete</button></td></tr>
                  } @empty { <tr><td colspan="6" class="empty-row">No lanes yet.</td></tr> }
                </tbody>
              </table>
            </div>
          }
        }
      </section>
    }`
})
export class AccountDetailPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly id = input.required<string>();
  readonly tab = signal<Tab>('overview');
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.customer(this.id()));
  readonly tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Overview' }, { id: 'contacts', label: 'Contacts' }, { id: 'activity', label: 'Activity' },
    { id: 'follow-ups', label: 'Follow-ups' }, { id: 'quotes', label: 'Quotes' }, { id: 'lanes', label: 'Lanes' }
  ];
  readonly today = todayIso();
  readonly money = money;
  readonly num = num;
  readonly date = date;
  readonly day = day;
  readonly dateTime = dateTime;
  readonly due = due;
  readonly tone = tone;

  openFollowUps = () => this.data.value()?.followUps.filter(f => f.status === 'Open') ?? [];

  count(tab: Tab): string {
    const d = this.data.value();
    if (!d) return '';
    const n = { overview: -1, contacts: d.contacts.length, activity: d.activities.length, 'follow-ups': this.openFollowUps().length,
      quotes: d.quotes.length, lanes: d.lanes.length }[tab];
    return n < 0 ? '' : String(n);
  }

  private async attempt(work: () => Promise<void>): Promise<void> {
    this.actionError.set(null);
    try { await work(); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }

  complete = (id: string) => this.attempt(() => this.actions.completeFollowUp(id));
  move = (q: Quote, action: 'send' | 'win' | 'lose') => this.attempt(() => this.actions.moveQuote(q, action));
  removeLane = (l: Lane) => confirm(`Delete the ${l.origin} → ${l.destination} lane?`) ? this.attempt(() => this.actions.deleteLane(l)) : Promise.resolve();
}
