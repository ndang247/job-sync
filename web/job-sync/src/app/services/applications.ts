import { computed, Injectable, signal } from '@angular/core';

export interface JobApplication {
  companyName: string;
  jobRole: string;
  status: 'applied' | 'reviewing' | 'interviewing' | 'offer' | 'rejected';
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

const MOCK_COMPANIES = [
  'Amazon',
  'CYOS Solutions',
  'Canva',
  'Atlassian',
  'WiseTech Global',
  'Airwallex',
  'Culture Amp',
  'Xero',
  'GitHub',
  'Google',
  'Microsoft',
  'Stripe',
  'SafetyCulture',
  'Linktree',
  'Rokt',
  'Carsales',
  'Notion',
  'Block',
  'Myob',
  'Seek',
];

const MOCK_ROLES = [
  'Software Development Engineer, Public Sector Customer Engineering',
  'Cloud Software Engineer',
  'Frontend Engineer, Growth Platform',
  'Product Engineer, Candidate Experience',
  'Full Stack Developer, Internal Tools',
  'Software Engineer, Developer Infrastructure',
  'Applications Engineer, Platform Services',
  'Backend Engineer, Identity',
  'UI Engineer, Design Systems',
  'Software Engineer, Integrations',
  'Senior Web Engineer',
  'Engineer, Automation & Workflow',
];

const MOCK_STATUSES: JobApplication['status'][] = [
  'applied',
  'reviewing',
  'interviewing',
  'offer',
  'rejected',
];

const SYNC_STAGES = [
  {
    progress: 14,
    stage: 'Checking connected Google accounts',
    caption: 'Confirming which inboxes are ready for sync.',
  },
  {
    progress: 37,
    stage: 'Scanning application threads',
    caption: 'Looking for new emails that contain role and company details.',
  },
  {
    progress: 63,
    stage: 'Extracting role, company, and status',
    caption: 'Turning email content into structured job application rows.',
  },
  {
    progress: 84,
    stage: 'Writing the latest snapshot to the server',
    caption: 'Saving parsed rows so the applications list stays current.',
  },
  {
    progress: 100,
    stage: 'Finishing sync',
    caption: 'Refreshing the visible list with the latest server response.',
  },
];

const PAGE_SIZE = 10;

function formatMockDate(offset: number): string {
  const date = new Date(Date.UTC(2026, 4, 20 - offset));
  const day = String(date.getUTCDate()).padStart(2, '0');
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const year = date.getUTCFullYear();
  return `${day}-${month}-${year}`;
}

function generateMockApplications(): JobApplication[] {
  return Array.from({ length: 100 }, (_, i) => ({
    companyName: MOCK_COMPANIES[i % MOCK_COMPANIES.length],
    jobRole: MOCK_ROLES[i % MOCK_ROLES.length],
    status: MOCK_STATUSES[i % MOCK_STATUSES.length],
    appliedDate: formatMockDate(i),
  }));
}

function parseAppliedDate(value: string): number {
  const [day, month, year] = value.split('-').map(Number);
  return new Date(year, month - 1, day).getTime();
}

@Injectable({
  providedIn: 'root',
})
export class ApplicationsService {
  readonly applications = signal<JobApplication[]>([]);
  readonly loading = signal(true);
  readonly searchQuery = signal('');
  readonly currentPage = signal(1);
  readonly accounts = signal<GoogleAccount[]>([
    { id: 'primary', label: 'candidate.primary@gmail.com' },
  ]);
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

  async loadApplications(): Promise<void> {
    this.loading.set(true);
    await new Promise((resolve) => setTimeout(resolve, 900));
    this.applications.set(generateMockApplications());
    this.currentPage.set(1);
    this.loading.set(false);
  }

  addGoogleAccount(): void {
    const nextIndex = this.accounts().length + 1;
    const newAccount: GoogleAccount = {
      id: `account-${nextIndex}`,
      label: `candidate.${nextIndex}@gmail.com`,
    };
    this.accounts.update((accs) => [...accs, newAccount]);
    this.selectedAccountId.set(newAccount.id);
    this.statusNote.set(
      `Google account added. ${this.accounts().length} accounts are ready for the next sync.`,
    );
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

    for (const step of SYNC_STAGES) {
      await new Promise((resolve) => setTimeout(resolve, 700));
      this.syncState.set({
        syncing: true,
        progress: step.progress,
        stage: `${step.stage} — ${account.label}`,
        caption: step.caption,
      });
    }

    this.applications.set(generateMockApplications());
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
