import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ApplicationsService } from '../../../services/applications';
import { ApplicationsTable } from './applications-table';

describe('ApplicationsTable', () => {
  let fixture: ComponentFixture<ApplicationsTable>;
  let service: ApplicationsService;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApplicationsTable],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    service = TestBed.inject(ApplicationsService);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(ApplicationsTable);
  });

  it('renders an edit action that navigates to the selected application edit route', async () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    service.loading.set(false);
    service.applications.set([
      {
        id: 'app-1',
        companyName: 'Canva',
        jobRole: 'Frontend Engineer',
        email: 'jobs@example.com',
        appliedDate: '20-05-2026',
        status: 'Interviewing',
        statusKey: 'interviewing',
      },
    ]);
    service.totalCount.set(1);

    fixture.detectChanges();
    await fixture.whenStable();

    const editButton = fixture.nativeElement.querySelector(
      '[data-testid="edit-application"]',
    ) as HTMLButtonElement | null;

    expect(editButton).not.toBeNull();
    editButton?.click();

    expect(navigateSpy).toHaveBeenCalledWith(['/applications', 'app-1', 'edit']);
  });
});
