import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from '../auth.service';

export interface LogEntry {
  timestamp: string;
  level: string;
  source: string;
  title: string;
  message: string;
  metadata?: Record<string, any>;
}

export interface ServiceStatus {
  name: string;
  status: 'running' | 'stopped' | 'unhealthy';
  uptime: string;
  lastCheck: string;
}

@Injectable({
  providedIn: 'root'
})
export class MonitoringService {
  constructor(private http: HttpClient, private auth: AuthService) {}

  /**
   * Connect to real-time log stream using Server-Sent Events
   */
  connectToLogStream(level?: string, source?: string): Observable<LogEntry> {
    return new Observable(observer => {
      const params = new URLSearchParams();
      if (level) params.append('level', level);
      if (source) params.append('source', source);

      const token = this.auth.getToken();
      if (token && !this.auth.isTokenExpired(token)) {
        params.append('access_token', token);
      }

      const url = `/api/monitoring/logs/stream${params.toString() ? '?' + params.toString() : ''}`;
      const eventSource = new EventSource(url);

      eventSource.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          observer.next(data);
        } catch (error) {
          console.error('Error parsing SSE data:', error);
        }
      };

      eventSource.onerror = (error) => {
        console.error('SSE connection error:', error);
        observer.error(error);
      };

      return () => {
        eventSource.close();
      };
    });
  }

  /**
   * Get recent logs with optional filters
   */
  getRecentLogs(limit = 100, level?: string, source?: string): Observable<LogEntry[]> {
    const params: Record<string, any> = { limit: limit.toString() };
    if (level) params['level'] = level;
    if (source) params['source'] = source;

    return this.http.get<LogEntry[]>('/api/monitoring/logs', { params });
  }

  /**
   * Get count of recent errors (last 5 minutes)
   */
  getErrorCount(): Observable<number> {
    return this.http.get<number>('/api/monitoring/logs/error-count');
  }

  /**
   * Get status of all services
   */
  getServiceStatus(): Observable<ServiceStatus[]> {
    return this.http.get<ServiceStatus[]>('/api/monitoring/services/status');
  }
}
