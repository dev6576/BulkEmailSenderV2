import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './features/auth/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  readonly auth = inject(AuthService);

  ngOnInit(): void {
    const params = new URLSearchParams(window.location.search);
    const result = params.get('gmail') ?? params.get('error');
    if (result) {
      if (result === 'connected') this.auth.refresh();
      else this.auth.error.set(result === 'authorization_denied' ? 'Gmail authorization was canceled.' : 'Unable to connect Gmail. Please try again.');
      window.history.replaceState({}, document.title, window.location.pathname);
    }
    this.auth.refresh();
  }

  disconnect(): void {
    this.auth.disconnect().subscribe({ next: () => this.auth.refresh(), error: () => this.auth.error.set('Unable to disconnect Gmail. Please try again.') });
  }
}
