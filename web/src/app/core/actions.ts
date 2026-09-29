import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from './api';
import { Field, Forms, Option, options } from './form';
import { Carrier, Contact, CustomerSnapshot, Lane, Lead, Meta, Quote } from './models';
import { Session } from './session';
import { todayIso } from './format';

type Values = Record<string, unknown>;

const text = (v: unknown) => (typeof v === 'string' && v.trim() !== '' ? v.trim() : null);
const id = (v: unknown) => (typeof v === 'string' && v !== '' ? v : null);
const number = (v: unknown) => (v === null || v === '' || v === undefined ? 0 : Number(v));
const optionalNumber = (v: unknown) => (v === null || v === '' || v === undefined ? null : Number(v));

/** Every create/edit flow in the app, as drawer forms. Pages and the "+ New" menu both call these. */
@Injectable({ providedIn: 'root' })
export class Actions {
  private readonly api = inject(Api);
  private readonly forms = inject(Forms);
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  private get meta(): Meta {
    const meta = this.session.meta();
    if (!meta) throw new Error('Settings are still loading.');
    return meta;
  }

  private repOptions(): Option[] {
    return [{ value: '', label: 'Unassigned' }, ...this.meta.reps.map(r => ({ value: r.id, label: r.name }))];
  }

  private async customerOptions(): Promise<{ list: CustomerSnapshot[]; options: Option[] }> {
    const page = await this.api.customers({ pageSize: 100, sort: 'name' });
    return { list: page.items, options: page.items.map(c => ({ value: c.id, label: c.name })) };
  }

  private done = (navigateTo?: (result: unknown) => string | null) => (result: unknown) => {
    this.session.changed();
    const url = navigateTo?.(result);
    if (url) void this.router.navigateByUrl(url);
  };

  // ---------- accounts ----------

  private customerFields(): Field[] {
    return [
      { key: 'name', label: 'Company name', type: 'text', required: true, wide: true },
      { key: 'laneOrigin', label: 'Primary lane origin', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'laneDestination', label: 'Primary lane destination', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'stage', label: 'Stage', type: 'select', required: true, options: options(this.meta.stages) },
      { key: 'ownerId', label: 'Owner', type: 'select', options: this.repOptions() },
      { key: 'monthlyLoads', label: 'Loads per month', type: 'number', min: 0 },
      { key: 'monthlyRevenue', label: 'Revenue per month', type: 'money', min: 0 },
      { key: 'monthlyGrossMargin', label: 'Gross margin per month', type: 'money', min: 0 },
      { key: 'notes', label: 'Notes', type: 'textarea', wide: true }
    ];
  }

  private customerBody(v: Values) {
    return {
      name: text(v['name']), laneOrigin: text(v['laneOrigin']), laneDestination: text(v['laneDestination']), stage: v['stage'],
      ownerId: id(v['ownerId']), monthlyLoads: number(v['monthlyLoads']), monthlyRevenue: number(v['monthlyRevenue']),
      monthlyGrossMargin: number(v['monthlyGrossMargin']), notes: text(v['notes'])
    };
  }

  newAccount(): void {
    this.forms.open({
      title: 'New account',
      fields: this.customerFields(),
      initial: { stage: 'Prospect', ownerId: this.session.repId() ?? '', monthlyLoads: 0, monthlyRevenue: 0, monthlyGrossMargin: 0 },
      submitLabel: 'Create account',
      submit: v => this.api.post<{ id: string }>('/api/customers', this.customerBody(v)),
      saved: this.done(r => `/accounts/${(r as { id: string }).id}`)
    });
  }

  editAccount(c: CustomerSnapshot, notes: string | null): void {
    this.forms.open({
      title: `Edit ${c.name}`,
      fields: this.customerFields(),
      initial: { ...c, ownerId: c.ownerId ?? '', notes: notes ?? '' },
      submitLabel: 'Save changes',
      submit: v => this.api.put(`/api/customers/${c.id}`, this.customerBody(v)),
      saved: this.done()
    });
  }

  // ---------- contacts ----------

  private contactFields(customers?: Option[]): Field[] {
    const fields: Field[] = [
      { key: 'name', label: 'Name', type: 'text', required: true },
      { key: 'role', label: 'Role', type: 'text', placeholder: 'Logistics Manager' },
      { key: 'email', label: 'Email', type: 'email' },
      { key: 'phone', label: 'Phone', type: 'tel' },
      { key: 'isPrimary', label: 'Primary contact for this account', type: 'checkbox', wide: true }
    ];
    return customers ? [{ key: 'customerId', label: 'Account', type: 'select', required: true, options: customers, wide: true }, ...fields] : fields;
  }

  private contactBody = (v: Values) =>
    ({ name: text(v['name']), role: text(v['role']), email: text(v['email']), phone: text(v['phone']), isPrimary: v['isPrimary'] === true });

  newContact(customerId?: string): void {
    void this.forms.openAsync(async () => {
      const customers = customerId ? undefined : (await this.customerOptions()).options;
      return {
        title: 'New contact',
        fields: this.contactFields(customers),
        initial: { customerId: customerId ?? '' },
        submitLabel: 'Add contact',
        submit: v => this.api.post(`/api/customers/${customerId ?? v['customerId']}/contacts`, this.contactBody(v)),
        saved: this.done()
      };
    });
  }

  editContact(c: Contact): void {
    this.forms.open({
      title: `Edit ${c.name}`,
      description: c.customerName,
      fields: this.contactFields(),
      initial: { ...c, role: c.role ?? '', email: c.email ?? '', phone: c.phone ?? '' },
      submitLabel: 'Save contact',
      submit: v => this.api.put(`/api/contacts/${c.id}`, this.contactBody(v)),
      saved: this.done()
    });
  }

  // ---------- activity & follow-ups ----------

  logActivity(customerId?: string, contacts: Contact[] = []): void {
    void this.forms.openAsync(async () => {
      const customers = customerId ? undefined : (await this.customerOptions()).options;
      const fields: Field[] = [
        { key: 'type', label: 'Type', type: 'select', required: true, options: options(this.meta.activityTypes) },
        { key: 'occurredAt', label: 'When', type: 'datetime', required: true },
        { key: 'summary', label: 'Summary', type: 'textarea', required: true, wide: true, placeholder: 'What was discussed and what happens next?' }
      ];
      if (contacts.length) fields.splice(1, 0, { key: 'contactId', label: 'Contact', type: 'select', options: [{ value: '', label: 'No specific contact' }, ...contacts.map(c => ({ value: c.id, label: c.name }))] });
      if (customers) fields.unshift({ key: 'customerId', label: 'Account', type: 'select', required: true, options: customers, wide: true });
      return {
        title: 'Log activity',
        fields,
        initial: { type: 'Call', occurredAt: localNow(), customerId: customerId ?? '', contactId: contacts.find(c => c.isPrimary)?.id ?? '' },
        submitLabel: 'Log activity',
        submit: v => this.api.post(`/api/customers/${customerId ?? v['customerId']}/activities`, {
          type: v['type'], summary: text(v['summary']), contactId: id(v['contactId']), repId: this.session.repId(),
          occurredAt: v['occurredAt'] ? new Date(String(v['occurredAt'])).toISOString() : null
        }),
        saved: this.done()
      };
    });
  }

  newFollowUp(customerId?: string): void {
    void this.forms.openAsync(async () => {
      const customers = customerId ? undefined : (await this.customerOptions()).options;
      const fields: Field[] = [
        { key: 'dueOn', label: 'Due', type: 'date', required: true },
        { key: 'repId', label: 'Assigned to', type: 'select', options: [{ value: '', label: 'Account owner' }, ...this.meta.reps.map(r => ({ value: r.id, label: r.name }))] },
        { key: 'description', label: 'What needs to happen', type: 'textarea', required: true, wide: true }
      ];
      if (customers) fields.unshift({ key: 'customerId', label: 'Account', type: 'select', required: true, options: customers, wide: true });
      return {
        title: 'New follow-up',
        fields,
        initial: { dueOn: todayIso(), repId: this.session.repId() ?? '', customerId: customerId ?? '' },
        submitLabel: 'Schedule follow-up',
        submit: v => this.api.post(`/api/customers/${customerId ?? v['customerId']}/follow-ups`,
          { dueOn: v['dueOn'], description: text(v['description']), repId: id(v['repId']) }),
        saved: this.done()
      };
    });
  }

  async completeFollowUp(followUpId: string): Promise<void> {
    await this.api.post(`/api/follow-ups/${followUpId}/complete`);
    this.session.changed();
  }

  // ---------- quotes ----------

  private async quoteForm(title: string, initial: Values, fixedCustomer: string | null, submit: (body: unknown) => Promise<unknown>, navigate: boolean) {
    const [{ list, options: customers }, carriers] = await Promise.all([
      this.customerOptions(),
      this.api.carriers({ status: 'Active', pageSize: 100 })
    ]);
    const lanesFor = async (customerId: string): Promise<Option[]> => {
      if (!customerId) return [{ value: '', label: 'Custom route' }];
      const lanes = await this.api.lanes({ customerId, pageSize: 100 });
      laneCache.set(customerId, lanes.items);
      return [{ value: '', label: 'Custom route' }, ...lanes.items.map(l => ({ value: l.id, label: `${l.origin} → ${l.destination} · ${l.equipment}` }))];
    };
    const laneCache = new Map<string, Lane[]>();
    const startCustomer = String(initial['customerId'] ?? fixedCustomer ?? list[0]?.id ?? '');
    // One lane field object: changing the account swaps its options in place.
    const laneField: Field = {
      key: 'laneId', label: 'Lane', type: 'select', options: await lanesFor(startCustomer), wide: true,
      onChange: value => {
        const lane = [...laneCache.values()].flat().find(l => l.id === value);
        return lane ? { origin: lane.origin, destination: lane.destination, equipment: lane.equipment, rate: lane.targetRate } : undefined;
      }
    };

    const fields: Field[] = [
      { key: 'customerId', label: 'Account', type: 'select', required: true, options: customers, wide: true,
        onChange: async value => { laneField.options = await lanesFor(String(value)); return { laneId: '' }; } },
      laneField,
      { key: 'origin', label: 'Origin', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'destination', label: 'Destination', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'equipment', label: 'Equipment', type: 'select', required: true, options: options(this.meta.equipmentTypes) },
      { key: 'pallets', label: 'Pallets', type: 'number', required: true, min: 1, max: 30 },
      { key: 'weight', label: 'Weight (lb)', type: 'number', required: true, min: 1, max: 48000 },
      { key: 'rate', label: 'Customer rate', type: 'money', required: true, min: 1 },
      { key: 'carrierId', label: 'Carrier', type: 'select', options: [{ value: '', label: 'Not selected yet' }, ...carriers.items.map((c: Carrier) => ({ value: c.id, label: `${c.name} · ${c.equipmentTypes.join(', ')}` }))] },
      { key: 'carrierCost', label: 'Carrier cost', type: 'money', min: 0, hint: 'Margin = rate − carrier cost', when: v => !!v['carrierId'] }
    ];
    return {
      title,
      fields,
      initial: { equipment: 'Dry Van', pallets: 8, weight: 8000, laneId: '', carrierId: '', ...initial, customerId: startCustomer },
      submitLabel: 'Save quote',
      submit: (v: Values) => submit({
        customerId: v['customerId'], laneId: id(v['laneId']), origin: text(v['origin']), destination: text(v['destination']),
        equipment: v['equipment'], pallets: number(v['pallets']), weight: number(v['weight']), rate: number(v['rate']),
        carrierId: id(v['carrierId']), carrierCost: v['carrierId'] ? optionalNumber(v['carrierCost']) : null, repId: this.session.repId()
      }),
      saved: this.done(navigate ? () => '/quotes' : undefined)
    };
  }

  newQuote(customerId?: string, lane?: Lane): void {
    void this.forms.openAsync(() => this.quoteForm('New quote',
      lane ? { customerId: lane.customerId, laneId: lane.id, origin: lane.origin, destination: lane.destination, equipment: lane.equipment, rate: lane.targetRate } : { customerId },
      customerId ?? null, body => this.api.post('/api/quotes', body), !customerId && !lane));
  }

  editQuote(q: Quote): void {
    void this.forms.openAsync(() => this.quoteForm(`Edit ${q.number}`,
      { ...q, laneId: q.laneId ?? '', carrierId: q.carrierId ?? '', carrierCost: q.carrierCost ?? '' },
      q.customerId, body => this.api.put(`/api/quotes/${q.id}`, body), false));
  }

  async moveQuote(q: Quote, action: 'send' | 'win' | 'lose'): Promise<void> {
    await this.api.post(`/api/quotes/${q.id}/${action}`);
    this.session.changed();
  }

  // ---------- leads ----------

  private leadFields(): Field[] {
    return [
      { key: 'company', label: 'Company', type: 'text', required: true, wide: true },
      { key: 'contactName', label: 'Contact name', type: 'text', required: true },
      { key: 'email', label: 'Email', type: 'email' },
      { key: 'phone', label: 'Phone', type: 'tel' },
      { key: 'source', label: 'Source', type: 'select', required: true, options: options(this.meta.leadSources) },
      { key: 'status', label: 'Status', type: 'select', required: true, options: options(this.meta.leadStatuses) },
      { key: 'ownerId', label: 'Owner', type: 'select', options: this.repOptions() },
      { key: 'notes', label: 'Notes', type: 'textarea', wide: true }
    ];
  }

  private leadBody = (v: Values) => ({
    company: text(v['company']), contactName: text(v['contactName']), email: text(v['email']), phone: text(v['phone']),
    source: v['source'], status: v['status'], ownerId: id(v['ownerId']), notes: text(v['notes'])
  });

  newLead(): void {
    this.forms.open({
      title: 'New lead',
      fields: this.leadFields(),
      initial: { source: 'Inbound', status: 'New', ownerId: this.session.repId() ?? '' },
      submitLabel: 'Create lead',
      submit: v => this.api.post('/api/leads', this.leadBody(v)),
      saved: this.done(() => '/leads')
    });
  }

  editLead(l: Lead): void {
    this.forms.open({
      title: `Edit ${l.company}`,
      fields: this.leadFields(),
      initial: { ...l, email: l.email ?? '', phone: l.phone ?? '', ownerId: l.ownerId ?? '', notes: l.notes ?? '' },
      submitLabel: 'Save lead',
      submit: v => this.api.put(`/api/leads/${l.id}`, this.leadBody(v)),
      saved: this.done()
    });
  }

  convertLead(l: Lead): void {
    this.forms.open({
      title: `Convert ${l.company}`,
      description: `Creates a prospect account with ${l.contactName} as its primary contact.`,
      fields: [
        { key: 'laneOrigin', label: 'Primary lane origin', type: 'text', required: true, placeholder: 'City, ST' },
        { key: 'laneDestination', label: 'Primary lane destination', type: 'text', required: true, placeholder: 'City, ST' },
        { key: 'ownerId', label: 'Account owner', type: 'select', options: this.repOptions() }
      ],
      initial: { ownerId: l.ownerId ?? '' },
      submitLabel: 'Convert to account',
      submit: v => this.api.post<{ customerId: string }>(`/api/leads/${l.id}/convert`,
        { laneOrigin: text(v['laneOrigin']), laneDestination: text(v['laneDestination']), ownerId: id(v['ownerId']) }),
      saved: this.done(r => `/accounts/${(r as { customerId: string }).customerId}`)
    });
  }

  // ---------- lanes ----------

  private laneFields(): Field[] {
    return [
      { key: 'origin', label: 'Origin', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'destination', label: 'Destination', type: 'text', required: true, placeholder: 'City, ST' },
      { key: 'equipment', label: 'Equipment', type: 'select', required: true, options: options(this.meta.equipmentTypes) },
      { key: 'estimatedLoadsPerMonth', label: 'Estimated loads per month', type: 'number', min: 0 },
      { key: 'targetRate', label: 'Target rate', type: 'money', min: 0 }
    ];
  }

  private laneBody = (v: Values) => ({
    origin: text(v['origin']), destination: text(v['destination']), equipment: v['equipment'],
    estimatedLoadsPerMonth: number(v['estimatedLoadsPerMonth']), targetRate: number(v['targetRate'])
  });

  newLane(customerId?: string): void {
    void this.forms.openAsync(async () => {
      const customers = customerId ? undefined : (await this.customerOptions()).options;
      const fields = this.laneFields();
      if (customers) fields.unshift({ key: 'customerId', label: 'Account', type: 'select', required: true, options: customers, wide: true });
      return {
        title: 'New lane',
        fields,
        initial: { equipment: 'Dry Van', estimatedLoadsPerMonth: 4, targetRate: 1500, customerId: customerId ?? '' },
        submitLabel: 'Add lane',
        submit: v => this.api.post(`/api/customers/${customerId ?? v['customerId']}/lanes`, this.laneBody(v)),
        saved: this.done()
      };
    });
  }

  editLane(l: Lane): void {
    this.forms.open({
      title: 'Edit lane',
      description: l.customerName,
      fields: this.laneFields(),
      initial: { ...l },
      submitLabel: 'Save lane',
      submit: v => this.api.put(`/api/lanes/${l.id}`, this.laneBody(v)),
      saved: this.done()
    });
  }

  async deleteLane(l: Lane): Promise<void> {
    await this.api.delete(`/api/lanes/${l.id}`);
    this.session.changed();
  }

  // ---------- carriers ----------

  private carrierFields(): Field[] {
    return [
      { key: 'name', label: 'Carrier name', type: 'text', required: true, wide: true },
      { key: 'mcNumber', label: 'MC number', type: 'text', required: true, placeholder: 'MC-000000' },
      { key: 'homeRegion', label: 'Home region', type: 'text', required: true },
      { key: 'equipmentTypes', label: 'Equipment', type: 'checks', required: true, options: options(this.meta.equipmentTypes), wide: true },
      { key: 'status', label: 'Status', type: 'select', required: true, options: options(this.meta.carrierStatuses) },
      { key: 'rating', label: 'Rating (1–5)', type: 'number', required: true, min: 1, max: 5 },
      { key: 'notes', label: 'Notes', type: 'textarea', wide: true }
    ];
  }

  private carrierBody = (v: Values) => ({
    name: text(v['name']), mcNumber: text(v['mcNumber']), homeRegion: text(v['homeRegion']),
    equipmentTypes: (v['equipmentTypes'] as string[]) ?? [], status: v['status'], rating: number(v['rating']), notes: text(v['notes'])
  });

  newCarrier(): void {
    this.forms.open({
      title: 'New carrier',
      fields: this.carrierFields(),
      initial: { status: 'Active', rating: 3, equipmentTypes: ['Dry Van'] },
      submitLabel: 'Add carrier',
      submit: v => this.api.post('/api/carriers', this.carrierBody(v)),
      saved: this.done(() => '/carriers')
    });
  }

  editCarrier(c: Carrier): void {
    this.forms.open({
      title: `Edit ${c.name}`,
      fields: this.carrierFields(),
      initial: { ...c, notes: c.notes ?? '' },
      submitLabel: 'Save carrier',
      submit: v => this.api.put(`/api/carriers/${c.id}`, this.carrierBody(v)),
      saved: this.done()
    });
  }
}

function localNow(): string {
  const now = new Date();
  now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
  return now.toISOString().slice(0, 16);
}
