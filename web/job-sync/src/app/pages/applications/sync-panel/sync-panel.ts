import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ApplicationsService } from '../../../services/applications';

@Component({
  selector: 'app-sync-panel',
  imports: [],
  templateUrl: './sync-panel.html',
  styleUrl: './sync-panel.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SyncPanel {
  protected readonly service = inject(ApplicationsService);
}
