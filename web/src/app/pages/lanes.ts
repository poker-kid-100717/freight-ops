import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api, ApiError } from '../core/api';
import { money } from '../core/format';
import { Lane } from '../core/models';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-lanes',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Operations</p><h1>Lanes</h1><p class="muted">Recurring origin–destination pairs per account, with target rates to quote from.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newLane()">New lane</button></div>
    </header>
    <div class="toolbar">
      <label class="filter"><span class="sr-only">Search lanes</span>
        <input type="search" placeholder="Search city or account" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
      <label class="filter"><span>Equipment</span>
        <select (change)="equipment.set($any($event.target).value); page.set(1)">
          <option value="">All</option>
          @for (e of session.meta()?.equipmentTypes ?? []; track e) { <option [value]="e">{{ e }}</option> }
        </select></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (actionError()) { <div class="alert" role="alert">{{ actionError() }}</div> }
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Route</th><th scope="col">Account</th><th scope="col">Equipment</th><th scope="col" class="num">Loads / mo</th>
            <th scope="col" class="num">Target rate</th><th scope="col" class="num">Quotes won</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (l of p.items; track l.id) {
              <tr>
                <td><strong>{{ l.origin }} → {{ l.destination }}</strong></td>
                <td><a [routerLink]="['/accounts', l.customerId]">{{ l.customerName }}</a></td>
                <td>{{ l.equipment }}</td><td class="num">{{ l.estimatedLoadsPerMonth }}</td>
                <td class="num">{{ money(l.targetRate) }}</td><td class="num">{{ l.quotesWon }}</td>
                <td class="row-actions"><button type="button" class="small" (click)="actions.newQuote(l.customerId, l)">Quote</button>
                  <button type="button" class="ghost small" (click)="actions.editLane(l)">Edit</button>
                  <button type="button" class="ghost small danger" (click)="remove(l)">Delete</button></td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty-row">No lanes match.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class LanesPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly search = signal('');
  readonly equipment = signal('');
  readonly page = signal(1);
  readonly actionError = signal<string | null>(null);
  readonly data = load(() => this.api.lanes({ search: this.search(), equipment: this.equipment(), page: this.page(), pageSize: 25 }));
  readonly money = money;

  async remove(l: Lane): Promise<void> {
    if (!confirm(`Delete the ${l.origin} → ${l.destination} lane for ${l.customerName}?`)) return;
    this.actionError.set(null);
    try { await this.actions.deleteLane(l); } catch (e) { this.actionError.set(ApiError.from(e).message); }
  }
}
