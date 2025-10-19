import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MonitoringService, ServiceStatus } from '../monitoring.service';
import { interval, Subscription } from 'rxjs';
import { startWith, switchMap } from 'rxjs/operators';

@Component({
  selector: 'wib-service-status',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './service-status.component.html',
  styleUrls: ['./service-status.component.css']
})
export class ServiceStatusComponent implements OnInit, OnDestroy {
  services: ServiceStatus[] = [];
  loading = true;
  error: string | null = null;
  private subscription?: Subscription;

  constructor(private monitoringService: MonitoringService) {}

  ngOnInit(): void {
    // Poll every 15 seconds
    this.subscription = interval(15000)
      .pipe(
        startWith(0),
        switchMap(() => this.monitoringService.getServiceStatus())
      )
      .subscribe({
        next: (services) => {
          this.services = services;
          this.loading = false;
          this.error = null;
        },
        error: (err) => {
          console.error('Error fetching service status:', err);
          this.error = 'Errore nel caricamento dello stato dei servizi';
          this.loading = false;
        }
      });
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'running':
        return 'status-running';
      case 'stopped':
        return 'status-stopped';
      case 'unhealthy':
        return 'status-unhealthy';
      default:
        return 'status-unknown';
    }
  }

  getStatusText(status: string): string {
    switch (status) {
      case 'running':
        return 'In Esecuzione';
      case 'stopped':
        return 'Fermato';
      case 'unhealthy':
        return 'Non Salutare';
      default:
        return 'Sconosciuto';
    }
  }

  refreshNow(): void {
    this.loading = true;
    this.monitoringService.getServiceStatus().subscribe({
      next: (services) => {
        this.services = services;
        this.loading = false;
        this.error = null;
      },
      error: (err) => {
        console.error('Error fetching service status:', err);
        this.error = 'Errore nel caricamento dello stato dei servizi';
        this.loading = false;
      }
    });
  }
}
