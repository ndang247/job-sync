import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApplicationsService, JobApplication } from '../../../services/applications';
import { ApplicationEdit } from './application-edit';

describe('ApplicationEdit', () => {
  const application: JobApplication = {
    id: 'app-1',
    companyName: 'Canva',
    jobRole: 'Frontend Engineer',
    email: 'jobs@example.com',
    appliedDate: '20-05-2026',
    status: 'Interviewing',
    statusKey: 'interviewing',
  };

  let fixture: ComponentFixture<ApplicationEdit>;
  let service: Pick<ApplicationsService, 'getApplication' | 'updateApplication' | 'statusNote'>;
  let router: Pick<Router, 'navigate'>;

  async function createComponent(getApplication = of(application)) {
    service = {
      getApplication: vi.fn(() => getApplication),
      updateApplication: vi.fn(() => of({ ...application, companyName: 'Updated Canva' })),
      statusNote: signal(''),
    };
    router = {
      navigate: vi.fn(() => Promise.resolve(true)),
    };

    await TestBed.configureTestingModule({
      imports: [ApplicationEdit],
      providers: [
        { provide: ApplicationsService, useValue: service },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'app-1' })) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicationEdit);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('loads the selected application and renders email as read-only', async () => {
    await createComponent();

    const companyInput = fixture.nativeElement.querySelector('#companyName') as HTMLInputElement;
    const emailInput = fixture.nativeElement.querySelector('#email') as HTMLInputElement;

    expect(companyInput.value).toBe('Canva');
    expect(emailInput.value).toBe('jobs@example.com');
    expect(emailInput.readOnly).toBe(true);
  });

  it('confirms before saving and sends the dd-MM-yyyy update payload', async () => {
    await createComponent();

    const companyInput = fixture.nativeElement.querySelector('#companyName') as HTMLInputElement;
    const dateInput = fixture.nativeElement.querySelector('#appliedDate') as HTMLInputElement;
    companyInput.value = 'Updated Canva';
    companyInput.dispatchEvent(new Event('input'));
    dateInput.value = '2026-05-22';
    dateInput.dispatchEvent(new Event('input'));

    const submitButton = fixture.nativeElement.querySelector('#submitButton') as HTMLButtonElement;
    submitButton.click();
    fixture.detectChanges();

    expect(service.updateApplication).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('#confirmModal')).not.toBeNull();

    const confirmButton = fixture.nativeElement.querySelector(
      '#confirmSubmitButton',
    ) as HTMLButtonElement;
    confirmButton.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(service.updateApplication).toHaveBeenCalledWith('app-1', {
      companyName: 'Updated Canva',
      jobRole: 'Frontend Engineer',
      status: 'Interviewing',
      appliedDate: '22-05-2026',
    });
    expect(router.navigate).toHaveBeenCalledWith(['/']);
    expect(service.statusNote()).toBe('Saved changes for Updated Canva.');
  });

  it('shows the not found state when the selected application is missing', async () => {
    await createComponent(throwError(() => ({ status: 404 })));

    expect(fixture.nativeElement.textContent).toContain('That application could not be found.');
    expect(fixture.nativeElement.querySelector('#editPanel')).toBeNull();
  });
});
