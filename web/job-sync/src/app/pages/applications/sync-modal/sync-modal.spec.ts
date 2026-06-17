import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { ApplicationsService, GoogleAccount } from '../../../services/applications';
import { SyncModal } from './sync-modal';

describe('SyncModal', () => {
  let fixture: ComponentFixture<SyncModal>;
  let service: Pick<
    ApplicationsService,
    'accounts' | 'syncModalOpen' | 'selectedAccountId' | 'closeSyncModal' | 'runSync'
  >;

  const accounts: GoogleAccount[] = [{ id: 'account-1', label: 'jobs@example.com' }];

  async function createComponent(accountList = accounts): Promise<void> {
    service = {
      accounts: signal(accountList),
      syncModalOpen: signal(true),
      selectedAccountId: signal(accountList[0]?.id ?? ''),
      closeSyncModal: vi.fn(),
      runSync: vi.fn(() => Promise.resolve()),
    };

    await TestBed.configureTestingModule({
      imports: [SyncModal],
      providers: [{ provide: ApplicationsService, useValue: service }],
    }).compileComponents();

    fixture = TestBed.createComponent(SyncModal);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function localToday(): string {
    const today = new Date();
    const year = today.getFullYear();
    const month = String(today.getMonth() + 1).padStart(2, '0');
    const day = String(today.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  function setDateValue(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('prefills start and end dates with the user local date', async () => {
    await createComponent();

    const startInput = fixture.nativeElement.querySelector('#syncStartDate') as HTMLInputElement;
    const endInput = fixture.nativeElement.querySelector('#syncEndDate') as HTMLInputElement;

    expect(startInput.value).toBe(localToday());
    expect(endInput.value).toBe(localToday());
  });

  it('submits the selected date range with the browser timezone', async () => {
    await createComponent();
    setDateValue('#syncStartDate', '2026-06-01');
    setDateValue('#syncEndDate', '2026-06-17');

    const submitButton = fixture.nativeElement.querySelector('#startSyncButton') as HTMLButtonElement;
    submitButton.click();
    fixture.detectChanges();

    expect(service.runSync).toHaveBeenCalledWith('account-1', {
      startDate: '2026-06-01',
      endDate: '2026-06-17',
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    });
  });

  it('shows a format error and blocks sync for a non yyyy-MM-dd start date', async () => {
    await createComponent();
    const component = fixture.componentInstance as unknown as {
      form: FormGroup<{
        startDate: import('@angular/forms').FormControl<string>;
        endDate: import('@angular/forms').FormControl<string>;
      }>;
    };
    component.form.controls.startDate.setValue('17-06-2026');

    const submitButton = fixture.nativeElement.querySelector('#startSyncButton') as HTMLButtonElement;
    submitButton.click();
    fixture.detectChanges();

    expect(service.runSync).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Use yyyy-MM-dd.');
  });

  it('shows a range error and blocks sync when end date is before start date', async () => {
    await createComponent();
    setDateValue('#syncStartDate', '2026-06-17');
    setDateValue('#syncEndDate', '2026-06-01');

    const submitButton = fixture.nativeElement.querySelector('#startSyncButton') as HTMLButtonElement;
    submitButton.click();
    fixture.detectChanges();

    expect(service.runSync).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('End date must be on or after start date.');
  });

  it('disables start sync when no connected accounts exist', async () => {
    await createComponent([]);

    const submitButton = fixture.nativeElement.querySelector('#startSyncButton') as HTMLButtonElement;

    expect(submitButton.disabled).toBe(true);
  });
});
