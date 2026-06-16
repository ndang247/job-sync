import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService, readAuthError } from '../../../services/auth/auth';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly email = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email, Validators.maxLength(256)],
  });
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');

  protected showError(): boolean {
    return this.email.invalid && (this.email.dirty || this.email.touched);
  }

  submit(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.email.markAsTouched();
    this.errorMessage.set('');
    if (this.email.invalid || this.submitting()) return;

    const email = this.email.value.trim().toLowerCase();
    this.submitting.set(true);
    this.auth.requestOtp(email).subscribe({
      next: () => {
        this.submitting.set(false);
        void this.router.navigate(['/otp'], {
          queryParams: {
            returnUrl: this.route.snapshot.queryParamMap.get('returnUrl') ?? '/',
          },
          state: { email },
        });
      },
      error: (error: unknown) => {
        const authError = readAuthError(error);
        this.submitting.set(false);
        this.errorMessage.set(authError.error ?? this.fallbackErrorMessage(error));
      },
    });
  }

  private fallbackErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 0) {
      return 'Could not reach the authentication server. Check the API is running.';
    }

    return 'Could not send a verification code. Try again.';
  }
}
