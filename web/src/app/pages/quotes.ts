import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { day, money, num, tone } from '../core/format';
import { Quote } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-quotes',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Sales</p><h1>Quotes</h1><p class="muted">Every rate quote, with carrier cost and margin.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newQuote()">New quote</button></div>
    </header>

    <div class="toolbar">
      <div class="tabs" role="tablist" aria-label="Quote status">
        @for (s of statuses; track s) {
          <button type="button" role="tab" [attr.aria-selected]="status() === s" [class.active]="status() === s" (click)="status.set(s); page.set(1)">{{ s || 'All' }}</button>
        }
      </div>
      <label class="filter"><span class="sr-only">Search quotes</span>
        <input type="search" placeholder="Search number, account or city" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Quote</th><th scope="col">Account</th><th scope="col">Route</th><th scope="col">Status</th>
            <th scope="col" class="num">Rate</th><th scope="col">Carrier</th><th scope="col" class="num">Margin</th><th scope="col">Created</th>
            <th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (q of p.items; track q.id) {
              <tr [class.highlight]="q.id === open()">
                <td><strong>{{ q.number }}</strong><div class="muted small">{{ q.repName ?? 'Unassigned' }}</div></td>
                <td><a [routerLink]="['/accounts', q.customerId]">{{ q.customerName }}</a></td>
                <td>{{ q.origin }} → {{ q.destination }}<div class="muted small">{{ q.equipment }} · {{ q.pallets }} pallets · {{ num(q.weight) }} lb</div></td>
                <td><span class="badge" [attr.data-tone]="tone(q.status)">{{ q.status }}</span></td>
                <td class="num">{{ money(q.rate) }}</td>
                <td>{{ q.carrierName ?? '—' }}</td>
                <td class="num">{{ money(q.margin) }}</td>
                <td>{{ day(q.createdAt) }}</td>
                <td class="row-actions">
                  @if (q.status === 'Draft') { <button type="button" class="small" (click)="move(q, 'send')">Send</button>
                    <button type="button" class="ghost small" (click)="actions.editQuote(q)">Edit</button> }
                  @if (q.status === 'Sent') { <button type="button" class="small" (click)="move(q, 'win')">Won</button>
                    <button type="button" class="ghost small" (click)="move(q, 'lose')">Lost</button> }
                </td>
              </tr>
            } @empty { <tr><td colspan="9" class="empty-row">No quotes match.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class QuotesPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  private readonly session = inject(Session);
  readonly open = input<string>();
  readonly statuses = ['', 'Draft', 'Sent', 'Won', 'Lost'];
  readonly status = signal('');
  readonly search = signal('');
  readonly page = signal(1);
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.quotes({ status: this.status(), search: this.search(), page: this.page(), pageSize: 25 }));
  readonly money = money;
  readonly num = num;
  readonly day = day;
  readonly tone = tone;

  constructor() {
    // /quotes?open=<id> (from search): open drafts for editing, otherwise search for the number to highlight it.
    effect(async () => {
      const id = this.open();
      if (!id || !this.session.meta()) return;
      const quote = await this.api.quote(id);
      if (quote.status === 'Draft') this.actions.editQuote(quote);
      else this.search.set(quote.number);
    });
  }

  async move(q: Quote, action: 'send' | 'win' | 'lose'): Promise<void> {
    this.actionError.set(null);
    try { await this.actions.moveQuote(q, action); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }
}
