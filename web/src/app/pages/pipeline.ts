import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { day, money } from '../core/format';
import { Quote } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-pipeline',
  imports: [RouterLink, State],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Sales</p><h1>Pipeline</h1>
        <p class="muted">Quotes by stage{{ session.rep() ? ' for ' + session.rep()!.name : ' for the whole team' }}. Move a quote with the buttons on its card.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newQuote()">New quote</button></div>
    </header>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }

    @if (data.value()) {
      <div class="board">
        @for (column of columns(); track column.status) {
          <section class="column" [attr.aria-label]="column.status + ' quotes'">
            <header><h2>{{ column.status }}</h2><span class="count">{{ column.quotes.length }}</span>
              <span class="muted small column-total">{{ money(column.total) }}</span></header>
            <ul>
              @for (q of column.quotes; track q.id) {
                <li class="deal">
                  <div class="deal-top"><strong>{{ q.number }}</strong><span>{{ money(q.rate) }}</span></div>
                  <a [routerLink]="['/accounts', q.customerId]">{{ q.customerName }}</a>
                  <p class="muted small">{{ q.origin }} → {{ q.destination }} · {{ q.equipment }}</p>
                  <p class="muted small">@if (q.margin !== null) { Margin {{ money(q.margin) }} · } {{ q.repName ?? 'Unassigned' }} · {{ day(q.createdAt) }}</p>
                  @if (q.status === 'Draft' || q.status === 'Sent') {
                    <div class="deal-actions">
                      @if (q.status === 'Draft') {
                        <button type="button" class="small" (click)="move(q, 'send')">Mark sent</button>
                        <button type="button" class="ghost small" (click)="actions.editQuote(q)">Edit</button>
                        <button type="button" class="ghost small" (click)="move(q, 'lose')">Lost</button>
                      } @else {
                        <button type="button" class="small" (click)="move(q, 'win')">Won</button>
                        <button type="button" class="ghost small" (click)="move(q, 'lose')">Lost</button>
                      }
                    </div>
                  }
                </li>
              } @empty { <li class="empty-row">None</li> }
            </ul>
          </section>
        }
      </div>
    }`
})
export class PipelinePage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.quotes({ rep: this.session.repId(), pageSize: 100 }));
  readonly columns = computed(() => ['Draft', 'Sent', 'Won', 'Lost'].map(status => {
    const quotes = (this.data.value()?.items ?? []).filter(q => q.status === status);
    return { status, quotes, total: quotes.reduce((sum, q) => sum + q.rate, 0) };
  }));
  readonly money = money;
  readonly day = day;

  async move(q: Quote, action: 'send' | 'win' | 'lose'): Promise<void> {
    this.actionError.set(null);
    try { await this.actions.moveQuote(q, action); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }
}
