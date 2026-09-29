import { Component, effect, inject, input, signal } from '@angular/core';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { tone } from '../core/format';
import { Session } from '../core/session';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-carriers',
  imports: [State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Operations</p><h1>Carriers</h1><p class="muted">Capacity partners you can put on a quote. Inactive carriers can't be selected.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newCarrier()">New carrier</button></div>
    </header>
    <div class="toolbar">
      <label class="filter"><span class="sr-only">Search carriers</span>
        <input type="search" placeholder="Search name, MC or region" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
      <label class="filter"><span>Equipment</span>
        <select (change)="equipment.set($any($event.target).value); page.set(1)">
          <option value="">All</option>
          @for (e of session.meta()?.equipmentTypes ?? []; track e) { <option [value]="e">{{ e }}</option> }
        </select></label>
      <label class="filter"><span>Status</span>
        <select (change)="status.set($any($event.target).value); page.set(1)">
          <option value="">All</option>
          @for (s of session.meta()?.carrierStatuses ?? []; track s) { <option [value]="s">{{ s }}</option> }
        </select></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Carrier</th><th scope="col">MC number</th><th scope="col">Equipment</th><th scope="col">Home region</th>
            <th scope="col">Rating</th><th scope="col">Status</th><th scope="col" class="num">Quotes won</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (c of p.items; track c.id) {
              <tr [class.highlight]="c.id === open()">
                <td><strong>{{ c.name }}</strong>@if (c.notes) { <div class="muted small clamp">{{ c.notes }}</div> }</td>
                <td>{{ c.mcNumber }}</td><td>{{ c.equipmentTypes.join(', ') }}</td><td>{{ c.homeRegion }}</td>
                <td><span class="stars" [attr.aria-label]="c.rating + ' out of 5'">{{ '★'.repeat(c.rating) }}<span class="dim">{{ '★'.repeat(5 - c.rating) }}</span></span></td>
                <td><span class="badge" [attr.data-tone]="tone(c.status)">{{ c.status }}</span></td>
                <td class="num">{{ c.quotesWon }}</td>
                <td class="row-actions"><button type="button" class="ghost small" (click)="actions.editCarrier(c)">Edit</button></td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty-row">No carriers match.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class CarriersPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly session = inject(Session);
  readonly open = input<string>();
  readonly search = signal('');
  readonly equipment = signal('');
  readonly status = signal('');
  readonly page = signal(1);
  readonly data = load(() => this.api.carriers({ search: this.search(), equipment: this.equipment(), status: this.status(), page: this.page(), pageSize: 25 }));
  readonly tone = tone;

  constructor() {
    effect(async () => {
      const id = this.open();
      if (id && this.session.meta()) this.actions.editCarrier(await this.api.carrier(id));
    });
  }
}
