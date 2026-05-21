import { computed, inject, Injectable, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';

export interface JobApplication {
  companyName: string;
  jobRole: string;
  status: 'applied';
  appliedDate: string;
}

export interface GoogleAccount {
  id: string;
  label: string;
}

export interface SyncState {
  syncing: boolean;
  progress: number;
  stage: string;
  caption: string;
}

const PAGE_SIZE = 10;

function parseAppliedDate(value: string): number {
  const [day, month, year] = value.split('-').map(Number);
  return new Date(year, month - 1, day).getTime();
}

const API_BASE_URL = 'http://localhost:5084';

@Injectable({
  providedIn: 'root',
})
export class ApplicationsService {
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);

  readonly applications = signal<JobApplication[]>([]);
  readonly loading = signal(true);
  readonly searchQuery = signal('');
  readonly currentPage = signal(1);
  readonly accounts = signal<GoogleAccount[]>([]);
  readonly lastSyncLabel = signal('Last sync just now');
  readonly statusNote = signal('');
  readonly syncState = signal<SyncState>({ syncing: false, progress: 0, stage: '', caption: '' });
  readonly syncModalOpen = signal(false);
  readonly selectedAccountId = signal('primary');

  readonly filteredApplications = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const sorted = [...this.applications()].sort(
      (a, b) => parseAppliedDate(b.appliedDate) - parseAppliedDate(a.appliedDate),
    );
    if (!query) return sorted;
    return sorted.filter((app) => {
      const haystack = [app.companyName, app.jobRole, app.status, app.appliedDate]
        .join(' ')
        .toLowerCase();
      return haystack.includes(query);
    });
  });

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredApplications().length / PAGE_SIZE)),
  );

  readonly safePage = computed(() => Math.min(this.currentPage(), this.totalPages()));

  readonly pageItems = computed(() => {
    const start = (this.safePage() - 1) * PAGE_SIZE;
    return this.filteredApplications().slice(start, start + PAGE_SIZE);
  });

  readonly paginationStart = computed(() => {
    if (this.filteredApplications().length === 0) return 0;
    return (this.safePage() - 1) * PAGE_SIZE + 1;
  });

  readonly paginationEnd = computed(() => {
    if (this.filteredApplications().length === 0) return 0;
    return (this.safePage() - 1) * PAGE_SIZE + this.pageItems().length;
  });

  loadApplications(): void {
    this.loading.set(true);
    this.http
      .get<
        { companyName: string; jobRole: string; appliedDate: string; status: string }[]
      >(`${API_BASE_URL}/api/v1/applications`)
      .subscribe({
        next: (applications) => {
          this.applications.set(
            applications.map((application) => ({
              companyName: application.companyName,
              jobRole: application.jobRole,
              appliedDate: application.appliedDate,
              status: application.status.toLowerCase() as JobApplication['status'],
            })),
          );
          this.currentPage.set(1);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        },
      });
  }

  loadConnections(): void {
    this.http.get<{ id: string; email: string }[]>(`${API_BASE_URL}/api/v1/connections`).subscribe({
      next: (connections) => {
        this.accounts.set(connections.map((c) => ({ id: c.id, label: c.email })));
      },
    });
  }

  addGoogleAccount(): void {
    const window = this.document.defaultView;
    if (window) {
      window.location.href = `${API_BASE_URL}/api/v1/mail-connect/gmail/start`;
    }
  }

  openSyncModal(): void {
    if (this.loading() || this.syncState().syncing || this.accounts().length === 0) return;
    this.syncModalOpen.set(true);
  }

  closeSyncModal(): void {
    this.syncModalOpen.set(false);
  }

  async runSync(accountId: string): Promise<void> {
    const account = this.accounts().find((a) => a.id === accountId) ?? this.accounts()[0];
    if (!account || this.syncState().syncing) return;

    this.selectedAccountId.set(account.id);
    this.syncModalOpen.set(false);
    this.syncState.set({
      syncing: true,
      progress: 0,
      stage: `Preparing ${account.label}`,
      caption: 'Connecting the selected account to the latest server job feed.',
    });
    this.statusNote.set(`Sync started for ${account.label}.`);

    // TODO: replace with real SignalR-driven sync progress
    this.syncState.set({ syncing: true, progress: 50, stage: 'Syncing...', caption: '' });

    this.loadApplications();
    this.syncState.set({ syncing: false, progress: 0, stage: '', caption: '' });
    const formatter = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });
    this.lastSyncLabel.set(`Last sync at ${formatter.format(new Date())}`);
    this.statusNote.set(`Sync complete — applications refreshed from ${account.label}.`);
    this.currentPage.set(1);
  }

  search(query: string): void {
    this.searchQuery.set(query);
    this.currentPage.set(1);
  }

  previousPage(): void {
    if (this.safePage() > 1) {
      this.currentPage.update((p) => p - 1);
    }
  }

  nextPage(): void {
    if (this.safePage() < this.totalPages()) {
      this.currentPage.update((p) => p + 1);
    }
  }
}
