import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './features/auth/auth.service';
import { ClientLogger } from './logging/client-logger';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
  host: {
    '[attr.data-palette]': 'palette()',
    '[style.--ui-accent]': 'customization().accent',
    '[style.--ui-logo-mark]': 'customization().logoMark',
    '[style.--ui-logo-text]': 'customization().logoText',
    '[style.--ui-active-tab-text]': 'customization().activeTabText',
    '[style.--ui-active-tab-highlight]': 'customization().activeTabHighlight'
  }
})
export class App implements OnInit {
  readonly auth = inject(AuthService);
  passwordValue = '';
  readonly loginError = signal('');
  readonly settingsOpen = signal(false);
  readonly palettes = [
    { id: 'warm', label: 'Warm paper', color: '#a65037' },
    { id: 'ocean', label: 'Ocean blue', color: '#155eef' },
    { id: 'forest', label: 'Forest green', color: '#28735c' }
  ] as const;
  readonly palette = signal<'warm' | 'ocean' | 'forest'>('warm');
  readonly customization = signal({
    accent: '#a65037',
    logoMark: '#a65037',
    logoText: '#302b27',
    activeTabText: '#8f422d',
    activeTabHighlight: '#a65037'
  });
  private readonly logger = inject(ClientLogger);

  ngOnInit(): void {
    const savedPalette = localStorage.getItem('bulk-email-ui-palette');
    if (savedPalette === 'warm' || savedPalette === 'ocean' || savedPalette === 'forest') {
      this.palette.set(savedPalette);
      this.applyPaletteDefaults(savedPalette);
    }
    const savedCustomization = localStorage.getItem('bulk-email-ui-colors');
    if (savedCustomization) {
      try {
        const colors = JSON.parse(savedCustomization) as Record<string, unknown>;
        this.customization.set({
          accent: this.validColor(colors['accent'], '#a65037'),
          logoMark: this.validColor(colors['logoMark'], '#a65037'),
          logoText: this.validColor(colors['logoText'], '#302b27'),
          activeTabText: this.validColor(colors['activeTabText'], '#8f422d'),
          activeTabHighlight: this.validColor(colors['activeTabHighlight'], '#a65037')
        });
      } catch { /* Ignore malformed saved color preferences. */ }
    }
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

  setPalette(palette: 'warm' | 'ocean' | 'forest'): void {
    this.palette.set(palette);
    this.applyPaletteDefaults(palette);
    localStorage.setItem('bulk-email-ui-palette', palette);
    this.settingsOpen.set(false);
  }

  private applyPaletteDefaults(palette: 'warm' | 'ocean' | 'forest'): void {
    const colors = {
      warm: { accent: '#bf642d', logoMark: '#a65037', logoText: '#302b27', activeTabText: '#a95020', activeTabHighlight: '#d8783d' },
      ocean: { accent: '#155eef', logoMark: '#155eef', logoText: '#172b4d', activeTabText: '#155eef', activeTabHighlight: '#155eef' },
      forest: { accent: '#28735c', logoMark: '#28735c', logoText: '#20372b', activeTabText: '#28735c', activeTabHighlight: '#28735c' }
    }[palette];
    this.customization.update(current => ({ ...current, ...colors }));
    localStorage.setItem('bulk-email-ui-colors', JSON.stringify(this.customization()));
  }

  updateColor(key: 'accent' | 'logoMark' | 'logoText' | 'activeTabText' | 'activeTabHighlight', value: string): void {
    if (!/^#[0-9a-fA-F]{6}$/.test(value)) return;
    const colors = { ...this.customization(), [key]: value };
    this.customization.set(colors);
    localStorage.setItem('bulk-email-ui-colors', JSON.stringify(colors));
  }

  private validColor(value: unknown, fallback: string): string {
    return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value) ? value : fallback;
  }

  login(): void { this.loginError.set(''); this.auth.login(this.passwordValue).subscribe({ next: () => { this.passwordValue = ''; this.auth.authenticated.set(true); this.auth.refresh(); }, error: () => this.loginError.set('Password was not accepted.') }); }
  logout(): void { this.auth.logout().subscribe({ next: () => { this.auth.authenticated.set(false); this.auth.status.set({ connected: false, emailAddress: null }); } }); }

  disconnect(): void {
    this.auth.disconnect().subscribe({ next: () => this.auth.refresh(), error: () => this.auth.error.set('Unable to disconnect Gmail. Please try again.') });
  }
}
