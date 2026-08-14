import { InjectionToken } from '@angular/core';

// The language the browser is set to, behind a token so a test can be run in either one.
export const SYSTEM_LANGUAGE = new InjectionToken<string>('SYSTEM_LANGUAGE', {
  providedIn: 'root',
  factory: () => navigator.language,
});
