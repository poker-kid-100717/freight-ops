import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from './api';
import { Meta } from './models';

const REP_KEY = 'freight-ops.viewing-as';

/** App-wide state: reference data, the "viewing as" rep, and a counter pages watch to reload after saves. */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(Api);

  readonly meta = signal<Meta | null>(null);
  readonly metaError = signal<string | null>(null);
  readonly repId = signal<string | null>(read(REP_KEY));
  readonly version = signal(0);
  readonly rep = computed(() => this.meta()?.reps.find(r => r.id === this.repId()) ?? null);

  async loadMeta(): Promise<void> {
    try {
      this.meta.set(await this.api.meta());
      this.metaError.set(null);
    } catch {
      this.metaError.set('Could not load settings from the server.');
    }
  }

  setRep(id: string | null): void {
    this.repId.set(id);
    try { id ? localStorage.setItem(REP_KEY, id) : localStorage.removeItem(REP_KEY); } catch { /* storage unavailable */ }
  }

  /** Signal every page that data changed. */
  changed(): void {
    this.version.update(v => v + 1);
  }
}

function read(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}
