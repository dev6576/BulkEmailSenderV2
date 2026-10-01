import { ApplicationConfig, ErrorHandler } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { ApplicationErrorHandler } from './logging/client-logger';
import { httpErrorLoggingInterceptor } from './logging/http-error-logging.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([httpErrorLoggingInterceptor])),
    { provide: ErrorHandler, useClass: ApplicationErrorHandler }
  ]
};
