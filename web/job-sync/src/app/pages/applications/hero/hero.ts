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

  async onAddAccount(): Promise<void> {
    await this.service.addGoogleAccount();
  }

  onSync(): void {
    this.service.openSyncModal();
  }

  onLogout(): void {
    this.service.logout();
  }
}
