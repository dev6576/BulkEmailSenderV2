import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

export interface GmailConnectionStatus {
  connected: boolean;
  emailAddress: string | null;
  error?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly status = signal<GmailConnectionStatus>({ connected: false, emailAddress: null });
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly authenticated = signal(false);

  checkSession(): void {
    this.http.get<{ authenticated: boolean }>('/api/auth/session').subscribe({
      next: () => { this.authenticated.set(true); this.refresh(); },
      error: () => { this.authenticated.set(false); this.loading.set(false); }
    });
  }
  login(password: string): Observable<void> {
    return this.http.post<void>('/api/auth/login', { password });
  }
  logout(): Observable<void> { return this.http.post<void>('/api/auth/logout', {}); }

  refresh(): void {
    this.loading.set(true);
    this.http.get<GmailConnectionStatus>('/api/auth/status').subscribe({
      next: value => { this.status.set(value); this.loading.set(false); this.error.set(null); },
      error: () => { this.status.set({ connected: false, emailAddress: null }); this.loading.set(false); this.error.set('Gmail connection status is unavailable.'); }
    });
  }

  connect(): void { window.location.assign('/api/auth/google'); }

  disconnect(): Observable<void> { return this.http.post<void>('/api/auth/google/disconnect', {}); }
}
