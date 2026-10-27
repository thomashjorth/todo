import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

import { provideAppTransloco } from '../i18n/transloco';

/**
 * The Connections page's own providers, not the app's: no router (the app's routes know no
 * /connections), no 401 interceptor (the helper answers 403, never 401) and no settings initializer
 * (GET /api/settings is the server's, and the helper has none). The language comes from ?lang=.
 */
export const connectionsConfig: ApplicationConfig = {
  providers: [provideBrowserGlobalErrorListeners(), provideHttpClient(), provideAppTransloco()],
};
