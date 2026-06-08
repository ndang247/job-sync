import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  ApplicationsService,
  JobApplication,
  JobApplicationStatus,
  UpdateApplicationRequest,
} from '../../../services/applications';

const STATUS_OPTIONS: JobApplicationStatus[] = [
  'Applied',
  'Interviewing',
  'Offered',
  'Company Rejected',
  'Candidate Rejected',
];

@Component({
  selector: 'app-application-edit',
  imports: [ReactiveFormsModule],
  templateUrl: './application-edit.html',
  styleUrl: './application-edit.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationEdit implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(ApplicationsService);

  protected readonly statusOptions = STATUS_OPTIONS;
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly application = signal<JobApplication | null>(null);
  protected readonly confirmOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly draft = signal<UpdateApplicationRequest | null>(null);

  protected readonly form = new FormGroup({
    companyName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    jobRole: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    status: new FormControl<JobApplicationStatus>('Applied', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    appliedDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');
      if (!id) {
        this.showNotFound();
        return;
      }

      this.loading.set(true);
      this.errorMessage.set('');
      this.service.getApplication(id).subscribe({
        next: (application) => {
          this.application.set(application);
          this.form.setValue({
            companyName: application.companyName,
            jobRole: application.jobRole,
            status: application.status,
            appliedDate: this.displayDateToInput(application.appliedDate),
          });
          this.notFound.set(false);
          this.loading.set(false);
        },
        error: () => {
          this.showNotFound();
        },
      });
    });
  }

  protected onCancel(event?: Event): void {
    event?.preventDefault();
    void this.router.navigate(['/']);
  }

  protected onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.draft.set(this.collectDraft());
    this.confirmOpen.set(true);
  }

  protected closeConfirm(): void {
    this.confirmOpen.set(false);
  }

  protected confirmChanges(): void {
    const application = this.application();
    const draft = this.draft();
    if (!application || !draft || this.saving()) return;

    this.saving.set(true);
    this.errorMessage.set('');
    this.service.updateApplication(application.id, draft).subscribe({
      next: (updated) => {
        this.service.statusNote.set(`Saved changes for ${updated.companyName}.`);
        void this.router.navigate(['/']);
      },
      error: () => {
        this.saving.set(false);
        this.confirmOpen.set(false);
        this.errorMessage.set('The application could not be saved. Try again.');
      },
    });
  }

  protected displayDate(): string {
    return this.draft()?.appliedDate ?? '';
  }

  private showNotFound(): void {
    this.application.set(null);
    this.loading.set(false);
    this.notFound.set(true);
    this.confirmOpen.set(false);
    this.errorMessage.set('The requested application is no longer available.');
  }

  private collectDraft(): UpdateApplicationRequest {
    const value = this.form.getRawValue();
    return {
      companyName: value.companyName.trim(),
      jobRole: value.jobRole.trim(),
      status: value.status,
      appliedDate: this.inputDateToDisplay(value.appliedDate),
    };
  }

  private displayDateToInput(value: string): string {
    const match = /^(\d{2})-(\d{2})-(\d{4})$/.exec(value);
    if (!match) return '';
    return `${match[3]}-${match[2]}-${match[1]}`;
  }

  private inputDateToDisplay(value: string): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) return value;
    return `${match[3]}-${match[2]}-${match[1]}`;
  }
}
