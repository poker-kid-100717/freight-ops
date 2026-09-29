import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

type Opportunity = {
  customerId: string;
  customerName: string;
  primaryLane: string;
  priorityScore: number;
  whyNow: string;
  recommendedAction: string;
};

type Dashboard = {
  activeCustomers: number;
  openOpportunities: number;
  monthlyLoads: number;
  grossMargin: number;
  followUpsDue: number;
  topOpportunities: Opportunity[];
};

type ExternalLoad = {
  loadNumber: string;
  customerName: string;
  status: string;
  scheduledPickupAt?: string;
  scheduledDeliveryAt?: string;
  requiredEquipment: string[];
  weight?: number;
  source: string;
};

type LoadResult = {
  provider: string;
  live: boolean;
  degraded: boolean;
  degradedReason?: string;
  loads: ExternalLoad[];
};

@Component({
  selector: 'app-root',
  standalone: true,
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly dashboard = signal<Dashboard | null>(null);
  readonly loads = signal<LoadResult | null>(null);
  readonly loading = signal(true);
  readonly externalLabel = computed(() => {
    const value = this.loads();
    if (!value) return 'Loading integration';
    return value.live ? 'Live Alvys read' : 'Synthetic demo provider';
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadDashboard(), this.refreshLoads()]);
    this.loading.set(false);
  }

  private async loadDashboard(): Promise<void> {
    this.dashboard.set(await firstValueFrom(this.http.get<Dashboard>('/api/dashboard')));
  }

  async refreshLoads(): Promise<void> {
    this.loads.set(await firstValueFrom(this.http.get<LoadResult>('/api/loads')));
  }

  money(value: number): string {
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(value);
  }

  date(value?: string): string {
    return value ? new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }).format(new Date(value)) : 'Not scheduled';
  }
}
