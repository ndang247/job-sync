import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { ApplicationsService } from '../../services/applications';
import { Hero } from './hero/hero';
import { SyncPanel } from './sync-panel/sync-panel';
import { ApplicationsTable } from './applications-table/applications-table';
import { SyncModal } from './sync-modal/sync-modal';

@Component({
  selector: 'app-applications',
  imports: [Hero, SyncPanel, ApplicationsTable, SyncModal],
  templateUrl: './applications.html',
  styleUrl: './applications.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Applications implements OnInit {
  private readonly service = inject(ApplicationsService);

  ngOnInit(): void {
    this.service.loadApplications();
  }
}
