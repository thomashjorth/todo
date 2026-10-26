import { InjectionToken } from '@angular/core';

/**
 * Loads the app again from an address. A token rather than a call to window.location, so a spec can
 * see that it happened: jsdom's location cannot be spied on.
 */
export const RELOAD = new InjectionToken<(url: string) => void>('RELOAD', {
  providedIn: 'root',
  factory: () => (url: string) => window.location.assign(url),
});
