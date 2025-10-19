import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MonitoringService } from './monitoring/monitoring.service';
import { interval, Subscription } from 'rxjs';
import { startWith, switchMap } from 'rxjs/operators';

@Component({
  selector: 'wib-home',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.css'],
})
export class HomeComponent implements OnInit, OnDestroy {
  errorCount = 0;
  private subscription?: Subscription;

  constructor(private monitoringService: MonitoringService) {}

  ngOnInit(): void {
    // Poll error count every 30 seconds
    this.subscription = interval(30000)
      .pipe(
        startWith(0),
        switchMap(() => this.monitoringService.getErrorCount())
      )
      .subscribe({
        next: (count) => {
          this.errorCount = count;
        },
        error: (err) => {
          console.error('Error fetching error count:', err);
        }
      });
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
  }
}
