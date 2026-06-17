import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  effect,
  viewChild,
} from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ApplicationsService } from '../../../services/applications';

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function localDateString(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function isCalendarDate(value: string): boolean {
  if (!DATE_PATTERN.test(value)) return false;

  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));

  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day
  );
}

function syncDateValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim();
  if (!value) return null;
  return isCalendarDate(value) ? null : { dateFormat: true };
}

const dateRangeValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const startDate = control.get('startDate')?.value as string | undefined;
  const endDate = control.get('endDate')?.value as string | undefined;

  if (!startDate || !endDate || !isCalendarDate(startDate) || !isCalendarDate(endDate)) {
    return null;
  }

  return endDate < startDate ? { dateOrder: true } : null;
};

@Component({
  selector: 'app-sync-modal',
  imports: [ReactiveFormsModule],
  templateUrl: './sync-modal.html',
  styleUrl: './sync-modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown.escape)': 'onCancel()',
  },
})
export class SyncModal {
  protected readonly service = inject(ApplicationsService);
  private readonly selectRef = viewChild<ElementRef<HTMLSelectElement>>('accountSelect');

  protected readonly form = new FormGroup(
    {
      startDate: new FormControl(localDateString(new Date()), {
        nonNullable: true,
        validators: [Validators.required, syncDateValidator],
      }),
      endDate: new FormControl(localDateString(new Date()), {
        nonNullable: true,
        validators: [Validators.required, syncDateValidator],
      }),
    },
    { validators: dateRangeValidator },
  );

  private hasSubmitted = false;

  constructor() {
    effect(() => {
      if (this.service.syncModalOpen()) {
        this.resetDateRange();
        setTimeout(() => this.selectRef()?.nativeElement.focus());
      }
    });
  }

  onAccountChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.service.selectedAccountId.set(select.value);
  }

  onCancel(): void {
    this.service.closeSyncModal();
  }

  onConfirm(): void {
    this.hasSubmitted = true;
    this.form.markAllAsTouched();
    this.form.updateValueAndValidity();

    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    void this.service.runSync(this.service.selectedAccountId(), {
      startDate: value.startDate,
      endDate: value.endDate,
      timeZone: this.browserTimeZone(),
    });
  }

  onOverlayClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-overlay')) {
      this.service.closeSyncModal();
    }
  }

  protected startDateError(): string {
    return this.fieldError(this.form.controls.startDate, 'Enter start date.');
  }

  protected endDateError(): string {
    return this.fieldError(this.form.controls.endDate, 'Enter end date.');
  }

  protected dateRangeError(): string {
    if (!this.shouldShowRangeError()) return '';
    return this.form.hasError('dateOrder') ? 'End date must be on or after start date.' : '';
  }

  protected startDateInvalid(): boolean {
    return this.shouldShowFieldError(this.form.controls.startDate);
  }

  protected endDateInvalid(): boolean {
    return this.shouldShowFieldError(this.form.controls.endDate) || this.shouldShowRangeError();
  }

  private resetDateRange(): void {
    const today = localDateString(new Date());
    this.hasSubmitted = false;
    this.form.reset({ startDate: today, endDate: today });
  }

  private fieldError(control: FormControl<string>, requiredMessage: string): string {
    if (!this.shouldShowFieldError(control)) return '';
    if (control.hasError('required')) return requiredMessage;
    if (control.hasError('dateFormat')) return 'Use yyyy-MM-dd.';
    return '';
  }

  private shouldShowFieldError(control: FormControl<string>): boolean {
    return control.invalid && (control.touched || this.hasSubmitted);
  }

  private shouldShowRangeError(): boolean {
    return (
      this.form.hasError('dateOrder') &&
      (this.hasSubmitted ||
        this.form.controls.startDate.dirty ||
        this.form.controls.endDate.dirty ||
        this.form.controls.startDate.touched ||
        this.form.controls.endDate.touched)
    );
  }

  private browserTimeZone(): string | undefined {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || undefined;
  }
}
