import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ApplicationsService } from '../../../services/applications';

@Component({
  selector: 'app-applications-table',
  imports: [],
  templateUrl: './applications-table.html',
  styleUrl: './applications-table.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationsTable {
  protected readonly service = inject(ApplicationsService);

  protected readonly summary = computed(() => {
    if (this.service.loading()) return 'Loading the latest applications from your server…';
    const apps = this.service.applications();
    const filtered = this.service.filteredApplications();
    const query = this.service.searchQuery().trim();
    if (apps.length === 0)
      return 'The server is connected, but there are no job applications to show yet.';
    if (filtered.length === 0)
      return `${apps.length} applications are available, but nothing matches "${query}".`;
    if (query) {
      return `${filtered.length} ${filtered.length === 1 ? 'match' : 'matches'} for "${query}". Showing ${this.service.paginationStart()}–${this.service.paginationEnd()} on page ${this.service.safePage()} of ${this.service.totalPages()}.`;
    }
    return `${apps.length} ${apps.length === 1 ? 'application' : 'applications'} currently stored on the server. Showing the latest ${this.service.paginationStart()}–${this.service.paginationEnd()}.`;
  });

  protected readonly resultCountText = computed(() => {
    if (this.service.loading()) return 'Loading rows…';
    return `${this.service.filteredApplications().length} total rows`;
  });

  protected readonly showLoading = computed(() => this.service.loading());
  protected readonly showEmpty = computed(
    () => !this.service.loading() && this.service.applications().length === 0,
  );
  protected readonly showSearchEmpty = computed(
    () =>
      !this.service.loading() &&
      this.service.applications().length > 0 &&
      this.service.filteredApplications().length === 0,
  );
  protected readonly showTable = computed(
    () => !this.service.loading() && this.service.filteredApplications().length > 0,
  );

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.service.search(input.value);
  }

  onPrevious(): void {
    this.service.previousPage();
  }

  onNext(): void {
    this.service.nextPage();
  }

  onSyncAgain(): void {
    this.service.openSyncModal();
  }
}
