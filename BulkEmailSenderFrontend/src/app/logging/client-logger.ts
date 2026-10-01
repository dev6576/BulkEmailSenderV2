import { ErrorHandler, Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ClientLogger {
  write(level: 'trace' | 'debug' | 'info' | 'warning' | 'error', category: string, message: string): void {
    // Keep the payload small and avoid logging request bodies, tokens, or email data.
    void fetch('/api/client-logs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ level, category, message: message.slice(0, 4000) }),
      keepalive: true
    }).catch(() => undefined);
  }
}

@Injectable()
export class ApplicationErrorHandler implements ErrorHandler {
  constructor(private readonly logger: ClientLogger) {}

  handleError(error: unknown): void {
    const detail = error instanceof Error ? `${error.name}: ${error.message}\n${error.stack ?? ''}` : String(error);
    this.logger.write('error', 'UnhandledException', detail);
    console.error(error);
  }
}
