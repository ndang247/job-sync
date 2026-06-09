import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApplicationsService, JobApplication } from '../../../services/applications';
import { DeleteModal } from './delete-modal';

describe('DeleteModal', () => {
  const application: JobApplication = {
    id: 'app-1',
    companyName: 'Canva',
    jobRole: 'Frontend Engineer',
    email: 'jobs@example.com',
    appliedDate: '20-05-2026',
    status: 'Applied',
    statusKey: 'applied',
  };

  let fixture: ComponentFixture<DeleteModal>;
  let service: Pick<
    ApplicationsService,
    | 'deleteModalOpen'
    | 'deleteInProgress'
    | 'deleteError'
    | 'pendingDeleteApplication'
    | 'closeDeleteModal'
    | 'confirmDeleteApplication'
  >;

  beforeEach(async () => {
    service = {
      deleteModalOpen: signal(true),
      deleteInProgress: signal(false),
      deleteError: signal(''),
      pendingDeleteApplication: signal(application),
      closeDeleteModal: vi.fn(),
      confirmDeleteApplication: vi.fn(() => Promise.resolve()),
    };

    await TestBed.configureTestingModule({
      imports: [DeleteModal],
      providers: [{ provide: ApplicationsService, useValue: service }],
    }).compileComponents();

    fixture = TestBed.createComponent(DeleteModal);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('disables the confirm button and shows Deleting while delete is in progress', () => {
    service.deleteInProgress.set(true);
    fixture.detectChanges();

    const confirmButton = fixture.nativeElement.querySelector(
      '.table-action-danger',
    ) as HTMLButtonElement;

    expect(confirmButton.disabled).toBe(true);
    expect(confirmButton.textContent?.trim()).toBe('Deleting...');
  });

  it('restores the Delete label when delete progress clears after failure', () => {
    service.deleteInProgress.set(true);
    fixture.detectChanges();
    service.deleteInProgress.set(false);
    service.deleteError.set('Could not delete this application. Try again.');
    fixture.detectChanges();

    const confirmButton = fixture.nativeElement.querySelector(
      '.table-action-danger',
    ) as HTMLButtonElement;

    expect(confirmButton.disabled).toBe(false);
    expect(confirmButton.textContent?.trim()).toBe('Delete');
    expect(fixture.nativeElement.textContent).toContain('Could not delete this application.');
  });

  it('does not call confirm when disabled', () => {
    service.deleteInProgress.set(true);
    fixture.detectChanges();

    const confirmButton = fixture.nativeElement.querySelector(
      '.table-action-danger',
    ) as HTMLButtonElement;
    confirmButton.click();

    expect(service.confirmDeleteApplication).not.toHaveBeenCalled();
  });
});
