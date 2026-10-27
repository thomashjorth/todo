import { EnvironmentProviders, isDevMode } from '@angular/core';
import { provideTransloco } from '@jsverse/transloco';

import { TranslationLoader } from './translation-loader';

/** The one Transloco setup, shared by the app and the helper's Connections page. */
export function provideAppTransloco(): EnvironmentProviders[] {
  return provideTransloco({
    config: {
      availableLangs: ['da', 'en'],
      defaultLang: 'da',
      fallbackLang: 'en',
      reRenderOnLangChange: true,
      prodMode: !isDevMode(),
    },
    loader: TranslationLoader,
  });
}
