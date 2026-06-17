import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApplicationsService, JobApplication } from './applications';

const connectionMock = {
  on: vi.fn(),
  start: vi.fn(() => Promise.resolve()),
  invoke: vi.fn(() => Promise.resolve()),
  stop: vi.fn(() => Promise.resolve()),
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(function (this: {
    withUrl: ReturnType<typeof vi.fn>;
    withAutomaticReconnect: ReturnType<typeof vi.fn>;
    build: ReturnType<typeof vi.fn>;
  }) {
    this.withUrl = vi.fn().mockReturnThis();
    this.withAutomaticReconnect = vi.fn().mockReturnThis();
    this.build = vi.fn(() => connectionMock);
  }),
}));

describe('ApplicationsService', () => {
  const application: JobApplication = {
    id: 'app-1',
    companyName: 'Canva',
    jobRole: 'Frontend Engineer',
    email: 'jobs@example.com',
    appliedDate: '20-05-2026',
    status: 'Applied',
    statusKey: 'applied',
  };

  let service: ApplicationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(ApplicationsService);
    http = TestBed.inject(HttpTestingController);
    service.loading.set(false);
    service.applications.set([application]);
    service.currentPage.set(1);
    service.totalPages.set(1);
    service.openDeleteModal(application.id);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('maps application display statuses to stable status keys', () => {
    service.loadApplications();

    const request = http.expectOne(
      'http://localhost:5084/api/v1/applications?page=1&pageSize=10',
    );
    request.flush({
      items: [
        {
          id: 'app-1',
          companyName: 'Canva',
          jobRole: 'Frontend Engineer',
          email: 'jobs@example.com',
          appliedDate: '20-05-2026',
          status: 'Company Rejected',
        },
      ],
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
      hasPrevious: false,
      hasNext: false,
    });

    expect(service.applications()[0].status).toBe('Company Rejected');
    expect(service.applications()[0].statusKey).toBe('company-rejected');
  });

  it('fetches a single application for edit prefill', () => {
    let result: JobApplication | undefined;

    service.getApplication('app-1').subscribe((fetchedApplication) => {
      result = fetchedApplication;
    });

    const request = http.expectOne('http://localhost:5084/api/v1/applications/app-1');
    request.flush({
      id: 'app-1',
      companyName: 'Atlassian',
      jobRole: 'Platform Engineer',
      email: 'jobs@example.com',
      appliedDate: '21-05-2026',
      status: 'Interviewing',
    });

    expect(result).toEqual({
      id: 'app-1',
      companyName: 'Atlassian',
      jobRole: 'Platform Engineer',
      email: 'jobs@example.com',
      appliedDate: '21-05-2026',
      status: 'Interviewing',
      statusKey: 'interviewing',
    });
  });

  it('updates an application and refreshes the loaded row', () => {
    service.applications.set([
      {
        id: 'app-1',
        companyName: 'BeforeCo',
        jobRole: 'Before Role',
        email: 'jobs@example.com',
        appliedDate: '20-05-2026',
        status: 'Applied',
        statusKey: 'applied',
      },
    ]);

    service
      .updateApplication('app-1', {
        companyName: 'AfterCo',
        jobRole: 'After Role',
        status: 'Offered',
        appliedDate: '22-05-2026',
      })
      .subscribe();

    const request = http.expectOne('http://localhost:5084/api/v1/applications/app-1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      companyName: 'AfterCo',
      jobRole: 'After Role',
      status: 'Offered',
      appliedDate: '22-05-2026',
    });

    request.flush({
      id: 'app-1',
      companyName: 'AfterCo',
      jobRole: 'After Role',
      email: 'jobs@example.com',
      appliedDate: '22-05-2026',
      status: 'Offered',
    });

    expect(service.applications()[0]).toMatchObject({
      companyName: 'AfterCo',
      jobRole: 'After Role',
      status: 'Offered',
      statusKey: 'offered',
      appliedDate: '22-05-2026',
    });
  });

  it('sends one delete request while delete is in progress and reloads on success', async () => {
    const firstDelete = service.confirmDeleteApplication();
    const secondDelete = service.confirmDeleteApplication();

    expect(service.deleteInProgress()).toBe(true);
    const deleteRequests = http.match('http://localhost:5084/api/v1/applications/app-1');
    expect(deleteRequests.length).toBe(1);
    const [deleteRequest] = deleteRequests;
    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush(null);

    await Promise.all([firstDelete, secondDelete]);

    expect(service.deleteModalOpen()).toBe(false);
    expect(service.statusNote()).toBe('Canva was removed from the tracker.');

    const reloadRequest = http.expectOne(
      'http://localhost:5084/api/v1/applications?page=1&pageSize=10',
    );
    expect(reloadRequest.request.method).toBe('GET');
    reloadRequest.flush({
      items: [],
      page: 1,
      pageSize: 10,
      totalCount: 0,
      totalPages: 1,
      hasPrevious: false,
      hasNext: false,
    });
  });

  it('keeps the modal open and clears delete progress on failure', async () => {
    const deletePromise = service.confirmDeleteApplication();

    const deleteRequest = http.expectOne('http://localhost:5084/api/v1/applications/app-1');
    deleteRequest.flush({ error: 'Server error' }, { status: 500, statusText: 'Server Error' });

    await deletePromise;

    expect(service.deleteInProgress()).toBe(false);
    expect(service.deleteModalOpen()).toBe(true);
    expect(service.deleteError()).toBe('Could not delete this application. Try again.');
  });

  it('posts the selected sync date range when starting a sync', async () => {
    service.accounts.set([{ id: 'account-1', label: 'jobs@example.com' }]);

    const syncPromise = service.runSync('account-1', {
      startDate: '2026-06-01',
      endDate: '2026-06-17',
      timeZone: 'Australia/Sydney',
    });
    await Promise.resolve();

    const request = http.expectOne('http://localhost:5084/api/v1/sync');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      emailConnectionId: 'account-1',
      dateRange: {
        startDate: '2026-06-01',
        endDate: '2026-06-17',
        timeZone: 'Australia/Sydney',
      },
    });

    request.flush({ jobId: 'job-1' });
    await syncPromise;

    expect(connectionMock.invoke).toHaveBeenCalledWith('JoinJob', 'job-1');
  });
});
