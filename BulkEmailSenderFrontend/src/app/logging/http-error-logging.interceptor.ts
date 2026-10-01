import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ClientLogger } from './client-logger';

export const httpErrorLoggingInterceptor: HttpInterceptorFn = (request, next) => {
  const logger = inject(ClientLogger);
  return next(request).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && request.url !== '/api/client-logs') {
      if (error.status === 0 || error.status >= 500) {
        const safeUrl = request.url.split('?')[0];
        logger.write(error.status === 0 ? 'error' : 'warning', 'HttpClient',
          `${request.method} ${safeUrl} failed with HTTP status ${error.status}.`);
      }
    }
    return throwError(() => error);
  }));
};
