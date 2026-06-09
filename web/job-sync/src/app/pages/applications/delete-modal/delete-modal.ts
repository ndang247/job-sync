import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { ApplicationsService } from '../../../services/applications';

@Component({
  selector: 'app-delete-modal',
  imports: [],
  templateUrl: './delete-modal.html',
  styleUrl: './delete-modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown.escape)': 'onCancel()',
  },
})
export class DeleteModal {
  protected readonly service = inject(ApplicationsService);
  private readonly confirmRef = viewChild<ElementRef<HTMLButtonElement>>('confirmDeleteButton');

  constructor() {
    effect(() => {
      if (this.service.deleteModalOpen()) {
        setTimeout(() => this.confirmRef()?.nativeElement.focus());
      }
    });
  }

  onCancel(): void {
    this.service.closeDeleteModal();
  }

  onConfirm(): void {
    void this.service.confirmDeleteApplication();
  }

  onOverlayClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-overlay')) {
      this.service.closeDeleteModal();
    }
  }
}
