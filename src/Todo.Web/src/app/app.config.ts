import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';

import { API_BASE_URL } from './api/todo-client';
import { routes } from './app.routes';
import { TranslationLoader } from './i18n/translation-loader';
import { SettingsStore } from './settings/settings-store';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    provideTransloco({
      config: {
        availableLangs: ['da', 'en'],
        defaultLang: 'da',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslationLoader,
    }),
    { provide: API_BASE_URL, useValue: '' },
    // The stored language decides the first render, so Danish never flashes past on the way to English.
    provideAppInitializer(() => inject(SettingsStore).start()),
  ],
};
