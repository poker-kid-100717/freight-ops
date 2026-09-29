import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../core/api';
import { money, num } from '../core/format';
import { ReportColumn } from '../core/models';
import { Bar, BarChart } from '../shared/bar-chart';
import { load } from '../shared/load';
import { State } from '../shared/state';

@Component({
  selector: 'app-reports',
  imports: [State, BarChart],
  template: `
    <header class="page-head">
      <div><p class="eyebrow">Insights</p><h1>Reports</h1><p class="muted">Computed live from the CRM data. Download any report as CSV.</p></div>
    </header>

    <div class="tabs" role="tablist" aria-label="Reports">
      @for (r of reports; track r.name) {
        <button type="button" role="tab" [attr.aria-selected]="name() === r.name" [class.active]="name() === r.name" (click)="name.set(r.name)">{{ r.label }}</button>
      }
    </div>

    <app-state [loading]="data.loading()" [error]="data.error()" [hasValue]="!!data.value()" (retry)="data.reload()" />
    @if (data.value(); as report) {
      <section class="card">
        <div class="card-head">
          <div><h2>{{ report.title }}</h2><p class="muted">{{ report.description }}</p></div>
          <a class="button ghost" [href]="'/api/reports/' + report.name + '?format=csv'" [attr.download]="report.name + '.csv'">Download CSV</a>
        </div>
        <app-bar-chart [bars]="bars()" [label]="report.title" />
      </section>
      <div class="table-wrap">
        <table>
          <thead><tr>@for (c of report.columns; track c.key) { <th scope="col" [class.num]="c.kind !== 'text'">{{ c.label }}</th> }</tr></thead>
          <tbody>
            @for (row of report.rows; track $index) {
              <tr>@for (c of report.columns; track c.key) { <td [class.num]="c.kind !== 'text'">{{ cell(c, row[c.key]) }}</td> }</tr>
            }
          </tbody>
        </table>
      </div>
    }`
})
export class ReportsPage {
  private readonly api = inject(Api);
  readonly reports = [
    { name: 'revenue-by-customer', label: 'Revenue by customer' },
    { name: 'quotes-by-month', label: 'Quotes by month' },
    { name: 'activity-by-rep', label: 'Activity by rep' },
    { name: 'stage-distribution', label: 'Accounts by stage' }
  ];
  readonly name = signal(this.reports[0].name);
  readonly data = load(() => this.api.report(this.name()));

  readonly bars = computed<Bar[]>(() => {
    const report = this.data.value();
    if (!report) return [];
    const column = report.columns.find(c => c.key === report.chartValue)!;
    return report.rows.map(row => ({
      label: String(row[report.chartLabel] ?? ''),
      value: Number(row[report.chartValue] ?? 0),
      display: this.cell(column, row[report.chartValue])
    }));
  });

  cell(column: ReportColumn, value: string | number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    switch (column.kind) {
      case 'money': return money(Number(value));
      case 'number': return num(Number(value));
      case 'percent': return `${Number(value).toFixed(1)}%`;
      default: return String(value);
    }
  }
}
