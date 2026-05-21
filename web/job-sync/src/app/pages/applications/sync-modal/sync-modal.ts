import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  effect,
  viewChild,
} from '@angular/core';
import { ApplicationsService } from '../../../services/applications';

@Component({
  selector: 'app-sync-modal',
  imports: [],
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

  constructor() {
    effect(() => {
      if (this.service.syncModalOpen()) {
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
    this.service.runSync(this.service.selectedAccountId());
  }

  onOverlayClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-overlay')) {
      this.service.closeSyncModal();
    }
  }
}
