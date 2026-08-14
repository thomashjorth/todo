import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';

import { API_BASE_URL } from './api/todo-client';
import { routes } from './app.routes';
import { TranslationLoader } from './i18n/translation-loader';

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
    { provide: API_BASE_URL, useValue: '' }
  ]
};
