import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './features/auth/auth.service';
import { ClientLogger } from './logging/client-logger';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  readonly auth = inject(AuthService);
  passwordValue = '';
  readonly loginError = signal('');
  private readonly logger = inject(ClientLogger);

  ngOnInit(): void {
    this.logger.write('info', 'Application', 'Frontend application initialized.');
    this.auth.checkSession();
    const params = new URLSearchParams(window.location.search);
    const result = params.get('gmail') ?? params.get('error');
    if (result) {
      if (result === 'connected') this.auth.refresh();
      else this.auth.error.set(result === 'authorization_denied' ? 'Gmail authorization was canceled.' : 'Unable to connect Gmail. Please try again.');
      window.history.replaceState({}, document.title, window.location.pathname);
    }
  }

  login(): void { this.loginError.set(''); this.auth.login(this.passwordValue).subscribe({ next: () => { this.passwordValue = ''; this.auth.authenticated.set(true); this.auth.refresh(); }, error: () => this.loginError.set('Password was not accepted.') }); }
  logout(): void { this.auth.logout().subscribe({ next: () => { this.auth.authenticated.set(false); this.auth.status.set({ connected: false, emailAddress: null }); } }); }

  disconnect(): void {
    this.auth.disconnect().subscribe({ next: () => this.auth.refresh(), error: () => this.auth.error.set('Unable to disconnect Gmail. Please try again.') });
  }
}
