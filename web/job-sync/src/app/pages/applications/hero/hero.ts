import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ApplicationsService } from '../../../services/applications';

@Component({
  selector: 'app-hero',
  imports: [],
  templateUrl: './hero.html',
  styleUrl: './hero.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Hero {
  protected readonly service = inject(ApplicationsService);

  onAddAccount(): void {
    this.service.addGoogleAccount();
  }

  onSync(): void {
    this.service.openSyncModal();
  }
}
