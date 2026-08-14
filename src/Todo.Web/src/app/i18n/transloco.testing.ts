import { TranslocoTestingModule, TranslocoTestingOptions } from '@jsverse/transloco';
import da from '../../../public/i18n/da.json';
import en from '../../../public/i18n/en.json';

// Serves the real translation files, so a spec fails on a key nobody wrote.
export function translocoTesting(options: TranslocoTestingOptions = {}) {
  return TranslocoTestingModule.forRoot({
    langs: { da, en },
    translocoConfig: {
      availableLangs: ['da', 'en'],
      defaultLang: 'da',
      fallbackLang: 'en',
      reRenderOnLangChange: true,
    },
    preloadLangs: true,
    ...options,
  });
}
