import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationsService } from './applications';

describe('ApplicationsService', () => {
  let service: ApplicationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(ApplicationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
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
    let result = undefined;

    service.getApplication('app-1').subscribe((application) => {
      result = application;
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
});
