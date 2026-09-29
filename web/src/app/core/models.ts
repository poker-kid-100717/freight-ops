// Shapes returned by the Freight Ops API (camelCase JSON).

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface Rep { id: string; name: string; email: string; title: string; }

export interface Opportunity {
  customerId: string; customerName: string; primaryLane: string;
  priorityScore: number; whyNow: string; recommendedAction: string;
}

export interface CustomerSnapshot {
  id: string; name: string; laneOrigin: string; laneDestination: string; primaryLane: string; stage: string;
  monthlyLoads: number; monthlyRevenue: number; monthlyGrossMargin: number; ownerId: string | null; ownerName: string | null;
  lastTouchAt: string | null; daysSinceTouch: number; openFollowUps: number; overdueFollowUps: number;
  score: number; whyNow: string; recommendedAction: string;
}

export interface Contact {
  id: string; customerId: string; customerName: string; name: string; role: string | null;
  email: string | null; phone: string | null; isPrimary: boolean;
}

export interface Activity {
  id: string; customerId: string; customerName: string; type: string; summary: string; occurredAt: string;
  contactId: string | null; contactName: string | null; repId: string | null; repName: string | null;
}

export interface FollowUp {
  id: string; customerId: string; customerName: string; dueOn: string; description: string; status: string;
  completedAt: string | null; repId: string | null; repName: string | null; overdue: boolean;
}

export interface Quote {
  id: string; number: string; customerId: string; customerName: string; laneId: string | null; origin: string; destination: string;
  equipment: string; pallets: number; weight: number; rate: number; carrierId: string | null; carrierName: string | null;
  carrierCost: number | null; margin: number | null; status: string; repId: string | null; repName: string | null;
  createdAt: string; sentAt: string | null; closedAt: string | null;
}

export interface Lane {
  id: string; customerId: string; customerName: string; origin: string; destination: string; equipment: string;
  estimatedLoadsPerMonth: number; targetRate: number; quotesWon: number;
}

export interface Lead {
  id: string; company: string; contactName: string; email: string | null; phone: string | null; source: string; status: string;
  ownerId: string | null; ownerName: string | null; notes: string | null; createdAt: string; convertedCustomerId: string | null;
}

export interface Carrier {
  id: string; name: string; mcNumber: string; equipmentTypes: string[]; homeRegion: string; status: string;
  rating: number; notes: string | null; quotesWon: number;
}

export interface CustomerDetail {
  customer: CustomerSnapshot; notes: string | null; createdAt: string; updatedAt: string;
  contacts: Contact[]; activities: Activity[]; followUps: FollowUp[]; quotes: Quote[]; lanes: Lane[];
}

export interface Dashboard {
  activeCustomers: number; openOpportunities: number; monthlyLoads: number; monthlyRevenue: number; grossMargin: number;
  followUpsDue: number; followUpsOverdue: number; quotesWonThisMonth: number; wonValueThisMonth: number;
  pipelineValue: number; atRiskAccounts: number; topOpportunities: Opportunity[];
}

export interface MyDay {
  today: string; overdue: FollowUp[]; dueToday: FollowUp[]; upcoming: FollowUp[];
  openQuotes: Quote[]; recentActivity: Activity[]; priorityAccounts: CustomerSnapshot[];
}

export interface SearchHit { type: string; id: string; title: string; subtitle: string; url: string; }

export interface ReportColumn { key: string; label: string; kind: 'text' | 'number' | 'money' | 'percent'; }
export interface Report {
  name: string; title: string; description: string; columns: ReportColumn[];
  rows: Record<string, string | number | null>[]; chartLabel: string; chartValue: string;
}

export interface IntegrationStatus { provider: string; mode: string; configured: boolean; safety: string; }

export interface Meta {
  stages: string[]; activityTypes: string[]; quoteStatuses: string[]; leadSources: string[]; leadStatuses: string[];
  equipmentTypes: string[]; carrierStatuses: string[]; reports: string[]; reps: Rep[];
  storage: { mode: string; persistent: boolean; ready: boolean };
  demoReset: { scheduled: boolean; schedule: string };
  integration: IntegrationStatus;
}

export interface ExternalLoad {
  loadNumber: string; customerName: string; status: string; scheduledPickupAt?: string; scheduledDeliveryAt?: string;
  requiredEquipment: string[]; weight?: number; source: string;
}
export interface LoadResult { provider: string; live: boolean; degraded: boolean; degradedReason?: string; loads: ExternalLoad[]; }
