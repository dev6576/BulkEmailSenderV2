import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './features/auth/auth.service';
import { ClientLogger } from './logging/client-logger';

type PaletteId = 'warm' | 'ocean' | 'forest' | 'lavender' | 'rose' | 'slate';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
  host: {
    '[attr.data-palette]': 'palette()',
    '[style.--ui-page]': 'paletteColors().page',
    '[style.--ui-surface]': 'paletteColors().surface',
    '[style.--ui-surface-alt]': 'paletteColors().surfaceAlt',
    '[style.--ui-text]': 'paletteColors().text',
    '[style.--ui-muted]': 'paletteColors().muted',
    '[style.--ui-border]': 'paletteColors().border',
    '[style.--ui-accent]': 'paletteColors().accent',
    '[style.--ui-accent-hover]': 'paletteColors().accentHover',
    '[style.--ui-accent-soft]': 'paletteColors().accentSoft',
    '[style.--ui-accent-text]': 'paletteColors().accentText'
  }
})
export class App implements OnInit {
  readonly auth = inject(AuthService);
  passwordValue = '';
  readonly loginError = signal('');
  readonly settingsOpen = signal(false);
  readonly palettes = [
    { id: 'warm', label: 'Warm paper', color: '#a65037', colors: { page: '#f4f0e9', surface: '#fbf8f2', surfaceAlt: '#f3eee6', text: '#302b27', muted: '#776d63', border: '#e3dacf', accent: '#bf642d', accentHover: '#a95020', accentSoft: '#fff0e2', accentText: '#fffaf5' } },
    { id: 'ocean', label: 'Ocean blue', color: '#155eef', colors: { page: '#f4f7fb', surface: '#ffffff', surfaceAlt: '#edf4ff', text: '#182230', muted: '#667085', border: '#dbe3ef', accent: '#155eef', accentHover: '#004eeb', accentSoft: '#edf4ff', accentText: '#ffffff' } },
    { id: 'forest', label: 'Forest green', color: '#28735c', colors: { page: '#f2f7f3', surface: '#fbfdfb', surfaceAlt: '#eaf3ec', text: '#20372b', muted: '#66796c', border: '#d7e4d9', accent: '#28735c', accentHover: '#205c49', accentSoft: '#eaf3ec', accentText: '#ffffff' } },
    { id: 'lavender', label: 'Lavender', color: '#7657a6', colors: { page: '#f6f3fa', surface: '#fdfbff', surfaceAlt: '#eee8f7', text: '#332a40', muted: '#766c83', border: '#e1d8ed', accent: '#7657a6', accentHover: '#63458f', accentSoft: '#eee8f7', accentText: '#ffffff' } },
    { id: 'rose', label: 'Rose', color: '#b44764', colors: { page: '#fbf3f4', surface: '#fffafb', surfaceAlt: '#f7e8eb', text: '#3e2b30', muted: '#806b71', border: '#ead7dc', accent: '#b44764', accentHover: '#983750', accentSoft: '#f7e8eb', accentText: '#ffffff' } },
    { id: 'slate', label: 'Slate', color: '#536579', colors: { page: '#f1f4f6', surface: '#fbfcfd', surfaceAlt: '#e8edf1', text: '#263442', muted: '#687784', border: '#d6dee5', accent: '#536579', accentHover: '#405365', accentSoft: '#e8edf1', accentText: '#ffffff' } }
  ] as const;
  readonly palette = signal<PaletteId>('warm');
  readonly paletteColors = computed(() => this.palettes.find(option => option.id === this.palette())!.colors);
  private readonly logger = inject(ClientLogger);

  ngOnInit(): void {
    const savedPalette = localStorage.getItem('bulk-email-ui-palette');
    if (this.palettes.some(option => option.id === savedPalette)) {
      this.palette.set(savedPalette as PaletteId);
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

  setPalette(palette: PaletteId): void {
    this.palette.set(palette);
    localStorage.setItem('bulk-email-ui-palette', palette);
    this.settingsOpen.set(false);
  }

  login(): void { this.loginError.set(''); this.auth.login(this.passwordValue).subscribe({ next: () => { this.passwordValue = ''; this.auth.authenticated.set(true); this.auth.refresh(); }, error: () => this.loginError.set('Password was not accepted.') }); }
  logout(): void { this.auth.logout().subscribe({ next: () => { this.auth.authenticated.set(false); this.auth.status.set({ connected: false, emailAddress: null }); } }); }

  disconnect(): void {
    this.auth.disconnect().subscribe({ next: () => this.auth.refresh(), error: () => this.auth.error.set('Unable to disconnect Gmail. Please try again.') });
  }
}
