import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Actions } from '../core/actions';
import { Api } from '../core/api';
import { load } from '../shared/load';
import { Pager } from '../shared/pager';
import { State } from '../shared/state';

@Component({
  selector: 'app-contacts',
  imports: [RouterLink, State, Pager],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Sales</p><h1>Contacts</h1><p class="muted">The people you work with at each account.</p></div>
      <div class="head-actions"><button type="button" (click)="actions.newContact()">New contact</button></div>
    </header>
    <div class="toolbar">
      <label class="filter"><span class="sr-only">Search contacts</span>
        <input type="search" placeholder="Search name, email or account" [value]="search()" (input)="search.set($any($event.target).value); page.set(1)" /></label>
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as p) {
      <div class="table-wrap">
        <table>
          <thead><tr><th scope="col">Name</th><th scope="col">Account</th><th scope="col">Role</th><th scope="col">Email</th>
            <th scope="col">Phone</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            @for (c of p.items; track c.id) {
              <tr>
                <td><strong>{{ c.name }}</strong>@if (c.isPrimary) { <span class="badge" data-tone="good">Primary</span> }</td>
                <td><a [routerLink]="['/accounts', c.customerId]">{{ c.customerName }}</a></td>
                <td>{{ c.role ?? '—' }}</td>
                <td>@if (c.email) { <a [href]="'mailto:' + c.email">{{ c.email }}</a> } @else { — }</td>
                <td>@if (c.phone) { <a [href]="'tel:' + c.phone">{{ c.phone }}</a> } @else { — }</td>
                <td class="row-actions"><button type="button" class="ghost small" (click)="actions.editContact(c)">Edit</button></td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty-row">No contacts match.</td></tr> }
          </tbody>
        </table>
      </div>
      <app-pager [page]="p.page" [pageSize]="p.pageSize" [total]="p.total" (go)="page.set($event)" />
    }`
})
export class ContactsPage {
  private readonly api = inject(Api);
  readonly actions = inject(Actions);
  readonly search = signal('');
  readonly page = signal(1);
  readonly data = load(() => this.api.contacts({ search: this.search(), page: this.page(), pageSize: 25 }));
}
