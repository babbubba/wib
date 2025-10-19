import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ServiceStatusComponent } from './service-status/service-status.component';
import { LogViewerComponent } from './log-viewer/log-viewer.component';

@Component({
  selector: 'wib-monitoring',
  standalone: true,
  imports: [CommonModule, RouterLink, ServiceStatusComponent, LogViewerComponent],
  templateUrl: './monitoring.component.html',
  styleUrls: ['./monitoring.component.css']
})
export class MonitoringComponent {}
