import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  OnInit,
  signal,
  viewChildren,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, readAuthError } from '../../../services/auth/auth';

@Component({
  selector: 'app-otp',
  imports: [RouterLink],
  templateUrl: './otp.html',
  styleUrl: './otp.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Otp implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly digitInputs = viewChildren<ElementRef<HTMLInputElement>>('digitInput');
  private countdownTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly indexes = [0, 1, 2, 3, 4, 5] as const;
  protected readonly email = signal('');
  protected readonly digits = signal(['', '', '', '', '', '']);
  protected readonly submitting = signal(false);
  protected readonly resending = signal(false);
  protected readonly resendRemaining = signal(0);
  protected readonly errorMessage = signal('');
  protected readonly successMessage = signal('');

  ngOnInit(): void {
    const pendingEmail = this.auth.pendingEmail() ?? this.navigationEmail();
    if (!pendingEmail) {
      void this.router.navigateByUrl('/login');
      return;
    }

    this.email.set(pendingEmail);
    this.startCountdown(this.auth.pendingResendSeconds());
    queueMicrotask(() => this.focusDigit(0));
  }

  ngOnDestroy(): void {
    this.stopCountdown();
  }

  onInput(index: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const value = input.value.replace(/\D/g, '').slice(0, 1);
    this.setDigit(index, value);
    input.value = value;
    this.errorMessage.set('');

    if (value && index < 5) this.focusDigit(index + 1);
  }

  onKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.digits()[index] && index > 0) {
      this.setDigit(index - 1, '');
      this.focusDigit(index - 1);
    }
  }

  onPaste(event: ClipboardEvent): void {
    event.preventDefault();
    const pasted = event.clipboardData?.getData('text').replace(/\D/g, '').slice(0, 6) ?? '';
    if (!pasted) return;

    const nextDigits = this.indexes.map((index) => pasted[index] ?? '');
    this.digits.set(nextDigits);
    this.errorMessage.set('');
    this.focusDigit(Math.min(pasted.length, 6) - 1);
  }

  verify(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    const code = this.code();
    this.errorMessage.set('');
    if (!/^\d{6}$/.test(code) || this.submitting()) {
      this.errorMessage.set('Enter the six-digit code.');
      this.focusFirstEmpty();
      return;
    }

    this.submitting.set(true);
    this.auth.verifyOtp(this.email(), code).subscribe({
      next: () => {
        this.submitting.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (error: unknown) => {
        const authError = readAuthError(error);
        this.submitting.set(false);
        this.errorMessage.set(authError.error ?? 'Incorrect code.');
        this.clearDigits();
        this.focusDigit(0);
      },
    });
  }

  resend(): void {
    if (this.resendRemaining() > 0 || this.resending()) return;

    this.resending.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.auth.requestOtp(this.email()).subscribe({
      next: (response) => {
        this.resending.set(false);
        this.successMessage.set(response.message);
        this.clearDigits();
        this.startCountdown(response.resendAfterSeconds);
        this.focusDigit(0);
      },
      error: (error: unknown) => {
        const authError = readAuthError(error);
        this.resending.set(false);
        this.errorMessage.set(authError.error ?? 'Could not resend the code. Try again.');
        if (error instanceof HttpErrorResponse && error.status === 429) {
          const retryAfter = Number(error.headers.get('Retry-After'));
          if (Number.isFinite(retryAfter)) this.startCountdown(retryAfter);
        }
      },
    });
  }

  protected canVerify(): boolean {
    return /^\d{6}$/.test(this.code()) && !this.submitting();
  }

  protected maskedEmail(): string {
    const [name, domain] = this.email().split('@');
    if (!name || !domain) return this.email();
    return `${name.slice(0, 2)}•••@${domain}`;
  }

  protected countdownLabel(): string {
    if (this.resendRemaining() <= 0) return 'You can resend the code now.';

    const minutes = Math.floor(this.resendRemaining() / 60);
    const seconds = this.resendRemaining() % 60;
    return `You can resend the code in ${minutes}:${String(seconds).padStart(2, '0')}.`;
  }

  private code(): string {
    return this.digits().join('');
  }

  private setDigit(index: number, value: string): void {
    this.digits.update((digits) => digits.map((digit, i) => (i === index ? value : digit)));
  }

  private clearDigits(): void {
    this.digits.set(['', '', '', '', '', '']);
  }

  private focusFirstEmpty(): void {
    const index = this.digits().findIndex((digit) => !digit);
    this.focusDigit(index === -1 ? 0 : index);
  }

  private focusDigit(index: number): void {
    const input = this.digitInputs()[index]?.nativeElement;
    input?.focus();
    input?.select();
  }

  private startCountdown(seconds: number): void {
    this.stopCountdown();
    this.resendRemaining.set(Math.max(0, seconds));
    if (seconds <= 0) return;

    this.countdownTimer = setInterval(() => {
      this.resendRemaining.update((remaining) => {
        const next = Math.max(0, remaining - 1);
        if (next === 0) this.stopCountdown();
        return next;
      });
    }, 1000);
  }

  private stopCountdown(): void {
    if (!this.countdownTimer) return;
    clearInterval(this.countdownTimer);
    this.countdownTimer = null;
  }

  private navigationEmail(): string | null {
    const state = this.document.defaultView?.history.state as Record<string, unknown> | undefined;
    return typeof state?.['email'] === 'string' ? state['email'] : null;
  }
}
