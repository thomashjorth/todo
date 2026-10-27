import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { API_BASE_URL } from './api/todo-client';
import { unauthorizedInterceptor } from './auth/unauthorized.interceptor';
import { routes } from './app.routes';
import { provideAppTransloco } from './i18n/transloco';
import { SettingsStore } from './settings/settings-store';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([unauthorizedInterceptor])),
    provideAppTransloco(),
    { provide: API_BASE_URL, useValue: '' },
    // The stored language decides the first render, so Danish never flashes past on the way to English.
    provideAppInitializer(() => inject(SettingsStore).start()),
  ],
};
