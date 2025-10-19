import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MonitoringService, LogEntry } from '../monitoring.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'wib-log-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './log-viewer.component.html',
  styleUrls: ['./log-viewer.component.css']
})
export class LogViewerComponent implements OnInit, OnDestroy {
  logs: LogEntry[] = [];
  filteredLogs: LogEntry[] = [];
  selectedLevels: Set<string> = new Set(['ERROR', 'WARNING', 'INFO', 'DEBUG', 'VERBOSE']);
  selectedSources: Set<string> = new Set(['worker', 'api', 'ml', 'ocr']);
  searchText = '';
  isPaused = false;
  autoScroll = true;
  maxLogs = 500;
  expandedLogIndex: number | null = null;

  private streamSubscription?: Subscription;

  levels = ['ERROR', 'WARNING', 'INFO', 'DEBUG', 'VERBOSE'];
  sources = ['worker', 'api', 'ml', 'ocr'];

  constructor(private monitoringService: MonitoringService) {}

  ngOnInit(): void {
    this.loadRecentLogs();
    this.connectToStream();
  }

  ngOnDestroy(): void {
    this.streamSubscription?.unsubscribe();
  }

  loadRecentLogs(): void {
    this.monitoringService.getRecentLogs(100).subscribe({
      next: (logs) => {
        this.logs = logs;
        this.applyFilters();
      },
      error: (err) => {
        console.error('Error loading recent logs:', err);
      }
    });
  }

  connectToStream(): void {
    this.streamSubscription = this.monitoringService.connectToLogStream().subscribe({
      next: (log) => {
        if (!this.isPaused) {
          this.logs.unshift(log);

          // Keep only the last maxLogs entries
          if (this.logs.length > this.maxLogs) {
            this.logs = this.logs.slice(0, this.maxLogs);
          }

          this.applyFilters();

          if (this.autoScroll) {
            setTimeout(() => this.scrollToTop(), 50);
          }
        }
      },
      error: (err) => {
        console.error('Error in log stream:', err);
        // Try to reconnect after 5 seconds
        setTimeout(() => this.connectToStream(), 5000);
      }
    });
  }

  applyFilters(): void {
    this.filteredLogs = this.logs.filter(log => {
      const levelMatch = this.selectedLevels.has(log.level.toUpperCase());
      const sourceMatch = this.selectedSources.has(log.source.toLowerCase());
      const textMatch = this.searchText === '' ||
        log.message.toLowerCase().includes(this.searchText.toLowerCase()) ||
        log.title.toLowerCase().includes(this.searchText.toLowerCase());

      return levelMatch && sourceMatch && textMatch;
    });
  }

  toggleLevel(level: string): void {
    if (this.selectedLevels.has(level)) {
      this.selectedLevels.delete(level);
    } else {
      this.selectedLevels.add(level);
    }
    this.applyFilters();
  }

  toggleSource(source: string): void {
    if (this.selectedSources.has(source)) {
      this.selectedSources.delete(source);
    } else {
      this.selectedSources.add(source);
    }
    this.applyFilters();
  }

  togglePause(): void {
    this.isPaused = !this.isPaused;
  }

  toggleAutoScroll(): void {
    this.autoScroll = !this.autoScroll;
  }

  clearLogs(): void {
    if (confirm('Vuoi cancellare tutti i log visualizzati?')) {
      this.logs = [];
      this.filteredLogs = [];
    }
  }

  toggleExpandLog(index: number): void {
    this.expandedLogIndex = this.expandedLogIndex === index ? null : index;
  }

  getLevelClass(level: string): string {
    switch (level.toUpperCase()) {
      case 'ERROR':
        return 'log-error';
      case 'WARNING':
        return 'log-warning';
      case 'INFO':
        return 'log-info';
      case 'DEBUG':
        return 'log-debug';
      case 'VERBOSE':
        return 'log-verbose';
      default:
        return '';
    }
  }

  getLevelBadgeClass(level: string): string {
    switch (level.toUpperCase()) {
      case 'ERROR':
        return 'badge bg-danger';
      case 'WARNING':
        return 'badge bg-warning text-dark';
      case 'INFO':
        return 'badge bg-info text-dark';
      case 'DEBUG':
        return 'badge bg-secondary';
      case 'VERBOSE':
        return 'badge bg-light text-dark';
      default:
        return 'badge bg-secondary';
    }
  }

  getSourceBadgeClass(source: string): string {
    switch (source.toLowerCase()) {
      case 'worker':
        return 'badge bg-primary';
      case 'api':
        return 'badge bg-success';
      case 'ml':
        return 'badge bg-warning text-dark';
      case 'ocr':
        return 'badge bg-info text-dark';
      default:
        return 'badge bg-secondary';
    }
  }

  private scrollToTop(): void {
    const container = document.querySelector('.logs-container');
    if (container) {
      container.scrollTop = 0;
    }
  }

  hasMetadata(log: LogEntry): boolean {
    return log.metadata != null && Object.keys(log.metadata).length > 0;
  }

  formatMetadata(metadata: Record<string, any>): string {
    return JSON.stringify(metadata, null, 2);
  }

  onSearchChange(): void {
    this.applyFilters();
  }
}
