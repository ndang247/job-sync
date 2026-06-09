import { computed, inject, Injectable, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom, map, Observable, tap } from 'rxjs';
import { StorageService } from './storage/storage';

export type JobApplicationStatus =
  | 'Applied'
  | 'Interviewing'
  | 'Offered'
  | 'Company Rejected'
  | 'Candidate Rejected';

export type JobApplicationStatusKey =
  | 'applied'
  | 'interviewing'
  | 'offered'
  | 'company-rejected'
  | 'candidate-rejected';

export interface JobApplication {
  id: string;
  companyName: string;
  jobRole: string;
  email: string;
  status: JobApplicationStatus;
  statusKey: JobApplicationStatusKey;
  appliedDate: string;
}

interface JobApplicationResponse {
  id: string;
  companyName: string;
  jobRole: string;
  email: string;
  appliedDate: string;
  status: string;
}

interface ApplicationsPageResponse {
  items: JobApplicationResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface UpdateApplicationRequest {
  companyName: string;
  jobRole: string;
  status: JobApplicationStatus;
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

const API_BASE_URL = 'http://localhost:5084';
const LAST_SYNC_KEY = 'lastSyncTimestamp';
const STATUS_LABELS: Record<JobApplicationStatusKey, JobApplicationStatus> = {
  applied: 'Applied',
  interviewing: 'Interviewing',
  offered: 'Offered',
  'company-rejected': 'Company Rejected',
  'candidate-rejected': 'Candidate Rejected',
};

@Injectable({
  providedIn: 'root',
})
export class ApplicationsService {
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly storage = inject(StorageService);

  readonly applications = signal<JobApplication[]>([]);
  readonly loading = signal(true);
  readonly searchQuery = signal('');
  readonly currentPage = signal(1);
  readonly pageSize = signal(PAGE_SIZE);
  readonly totalCount = signal(0);
  readonly totalPages = signal(1);
  readonly hasPrevious = signal(false);
  readonly hasNext = signal(false);
  readonly accounts = signal<GoogleAccount[]>([]);
  readonly lastSyncLabel = signal(this.getStoredSyncLabel());
  readonly statusNote = signal('');
  readonly syncState = signal<SyncState>({ syncing: false, progress: 0, stage: '', caption: '' });
  readonly syncModalOpen = signal(false);
  readonly selectedAccountId = signal('primary');
  readonly deleteModalOpen = signal(false);
  readonly pendingDeleteId = signal<string | null>(null);
  readonly deleteInProgress = signal(false);
  readonly deleteError = signal('');

  private deleteModalTrigger: HTMLElement | null = null;

  readonly filteredApplications = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const applications = this.applications();
    if (!query) return applications;
    return applications.filter((app) => {
      const haystack = [app.companyName, app.jobRole, app.email, app.status, app.appliedDate]
        .join(' ')
        .toLowerCase();
      return haystack.includes(query);
    });
  });

  readonly safePage = computed(() => Math.min(this.currentPage(), this.totalPages()));

  readonly pageItems = computed(() => {
    return this.filteredApplications();
  });

  readonly paginationStart = computed(() => {
    if (this.pageItems().length === 0) return 0;
    return (this.safePage() - 1) * this.pageSize() + 1;
  });

  readonly paginationEnd = computed(() => {
    if (this.pageItems().length === 0) return 0;
    return this.paginationStart() + this.pageItems().length - 1;
  });

  readonly pendingDeleteApplication = computed(() => {
    const id = this.pendingDeleteId();
    if (!id) return null;
    return this.applications().find((application) => application.id === id) ?? null;
  });

  loadApplications(page = 1): void {
    this.loading.set(true);
    this.http
      .get<ApplicationsPageResponse>(`${API_BASE_URL}/api/v1/applications`, {
        params: {
          page: String(page),
          pageSize: String(PAGE_SIZE),
        },
      })
      .subscribe({
        next: (response) => {
          if (
            response.items.length === 0 &&
            response.totalCount > 0 &&
            page > response.totalPages
          ) {
            this.loadApplications(response.totalPages);
            return;
          }

          this.applications.set(
            response.items.map((application) => ({
              id: application.id,
              companyName: application.companyName,
              jobRole: application.jobRole,
              email: application.email,
              appliedDate: application.appliedDate,
              status: this.toStatusLabel(application.status),
              statusKey: this.toStatusKey(application.status),
            })),
          );
          this.currentPage.set(response.page);
          this.pageSize.set(response.pageSize);
          this.totalCount.set(response.totalCount);
          this.totalPages.set(response.totalPages);
          this.hasPrevious.set(response.hasPrevious);
          this.hasNext.set(response.hasNext);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
        },
      });
  }

  getApplication(id: string): Observable<JobApplication> {
    return this.http
      .get<JobApplicationResponse>(`${API_BASE_URL}/api/v1/applications/${id}`)
      .pipe(map((response) => this.mapApplication(response)));
  }

  updateApplication(id: string, request: UpdateApplicationRequest): Observable<JobApplication> {
    return this.http
      .put<JobApplicationResponse>(`${API_BASE_URL}/api/v1/applications/${id}`, request)
      .pipe(
        map((response) => this.mapApplication(response)),
        tap((updated) => {
          this.applications.update((applications) =>
            applications.map((application) =>
              application.id === updated.id ? updated : application,
            ),
          );
        }),
      );
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

  openDeleteModal(id: string, trigger?: HTMLElement): void {
    if (this.loading() || this.syncState().syncing || this.deleteInProgress()) return;

    const application = this.applications().find((item) => item.id === id);
    if (!application) return;

    this.pendingDeleteId.set(id);
    this.deleteError.set('');
    this.deleteModalOpen.set(true);
    this.deleteModalTrigger = trigger ?? null;
  }

  closeDeleteModal(): void {
    if (this.deleteInProgress()) return;

    this.deleteModalOpen.set(false);
    this.pendingDeleteId.set(null);
    this.deleteError.set('');
    this.focusDeleteModalTrigger();
  }

  async confirmDeleteApplication(): Promise<void> {
    if (this.deleteInProgress()) return;

    const application = this.pendingDeleteApplication();
    if (!application) {
      this.closeDeleteModal();
      return;
    }

    this.deleteInProgress.set(true);
    this.deleteError.set('');

    try {
      await firstValueFrom(
        this.http.delete<void>(`${API_BASE_URL}/api/v1/applications/${application.id}`),
      );

      this.deleteInProgress.set(false);
      this.deleteModalOpen.set(false);
      this.pendingDeleteId.set(null);
      this.deleteError.set('');
      this.statusNote.set(`${application.companyName} was removed from the tracker.`);
      this.loadApplications(this.safePage());
      this.focusDeleteModalTrigger();
    } catch {
      this.deleteInProgress.set(false);
      this.deleteError.set('Could not delete this application. Try again.');
    }
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
      caption: 'Initiating sync job on the server.',
    });
    this.statusNote.set(`Sync started for ${account.label}.`);

    try {
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE_URL}/hubs/sync`)
        .withAutomaticReconnect()
        .build();

      connection.on('SyncProgress', (stage: string, percent: number) => {
        this.syncState.set({
          syncing: true,
          progress: percent,
          stage: `${stage} — ${account.label}`,
          caption: `${stage}`,
        });
      });

      connection.on('SyncCompleted', () => {
        this.loadApplications(1);
        this.syncState.set({ syncing: false, progress: 0, stage: '', caption: '' });
        const now = new Date();
        this.storage.setItem(LAST_SYNC_KEY, now.toISOString());
        this.lastSyncLabel.set(this.formatSyncLabel(now));
        this.statusNote.set(`Sync complete — applications refreshed from ${account.label}.`);
        connection.stop();
      });

      connection.on('SyncFailed', (error: string) => {
        this.syncState.set({ syncing: false, progress: 0, stage: '', caption: '' });
        this.statusNote.set(`Sync failed: ${error}`);
        connection.stop();
      });

      await connection.start();

      const { jobId } = await firstValueFrom(
        this.http.post<{ jobId: string }>(`${API_BASE_URL}/api/v1/sync`, {
          emailConnectionId: account.id,
        }),
      );

      await connection.invoke('JoinJob', jobId);
    } catch (error) {
      console.error('Failed to start sync:', error);
      this.syncState.set({ syncing: false, progress: 0, stage: '', caption: '' });
      this.statusNote.set('Failed to start sync.');
    }
  }

  private formatSyncLabel(date: Date): string {
    const formatter = new Intl.DateTimeFormat(undefined, {
      day: 'numeric',
      month: 'short',
      hour: 'numeric',
      minute: '2-digit',
    });
    return `Last sync at ${formatter.format(date)}`;
  }

  private getStoredSyncLabel(): string {
    const stored = this.storage.getItem(LAST_SYNC_KEY);
    if (stored) {
      const date = new Date(stored);
      if (!isNaN(date.getTime())) return this.formatSyncLabel(date);
    }
    return 'No sync yet';
  }

  search(query: string): void {
    this.searchQuery.set(query);
  }

  previousPage(): void {
    if (this.hasPrevious()) {
      this.loadApplications(this.safePage() - 1);
    }
  }

  nextPage(): void {
    if (this.hasNext()) {
      this.loadApplications(this.safePage() + 1);
    }
  }

  private focusDeleteModalTrigger(): void {
    const trigger = this.deleteModalTrigger;
    this.deleteModalTrigger = null;

    if (trigger && this.document.body.contains(trigger)) {
      trigger.focus();
    }
  }

  private mapApplication(application: JobApplicationResponse): JobApplication {
    return {
      id: application.id,
      companyName: application.companyName,
      jobRole: application.jobRole,
      email: application.email,
      appliedDate: application.appliedDate,
      status: this.toStatusLabel(application.status),
      statusKey: this.toStatusKey(application.status),
    };
  }

  private toStatusLabel(status: string): JobApplicationStatus {
    return STATUS_LABELS[this.toStatusKey(status)];
  }

  private toStatusKey(status: string): JobApplicationStatusKey {
    const normalized = status
      .trim()
      .toLowerCase()
      .replace(/[\s-]+/g, '');
    switch (normalized) {
      case 'interviewing':
        return 'interviewing';
      case 'offer':
      case 'offered':
        return 'offered';
      case 'rejected':
      case 'companyrejected':
        return 'company-rejected';
      case 'candidaterejected':
        return 'candidate-rejected';
      case 'applied':
      default:
        return 'applied';
    }
  }
}
