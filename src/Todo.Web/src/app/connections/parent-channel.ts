import { Injectable, InjectionToken, inject } from '@angular/core';

import { resolveLanguage } from '../i18n/resolve-language';
import { HELPER_SOURCE, HelperMessageBody } from './helper-message';

/** The page's query string, where the app names its origin and its language. */
export const PAGE_SEARCH = new InjectionToken<string>('PAGE_SEARCH', {
  factory: () => window.location.search,
});

/** The one thing the page does with the window around it. */
export interface ParentWindow {
  postMessage(message: unknown, targetOrigin: string): void;
}

/**
 * The window around the frame, or null when the page is opened on its own. Wrapped rather than the
 * window itself: Angular looks for ngOnDestroy on whatever a provider hands out, and reading any
 * property off a cross-origin window throws a SecurityError - which stopped the page from starting.
 */
export const PARENT_WINDOW = new InjectionToken<ParentWindow | null>('PARENT_WINDOW', {
  factory: () => {
    if (window.parent === window) return null;

    const parent = window.parent;
    return { postMessage: (message, targetOrigin) => parent.postMessage(message, targetOrigin) };
  },
});

/**
 * The page's one line to the app around it. A message goes only to the origin the app named in
 * ?parent=, so the browser drops it if anything else framed the page; and only an http(s) origin is
 * taken, so a malformed or javascript: value posts nothing at all.
 */
@Injectable({ providedIn: 'root' })
export class ParentChannel {
  private readonly parent = inject(PARENT_WINDOW);
  private readonly params = new URLSearchParams(inject(PAGE_SEARCH));

  readonly parentOrigin = originOf(this.params.get('parent'));

  /** The app's language, so the page speaks it from the first render. */
  readonly language = resolveLanguage(this.params.get('lang'), navigator.language);

  get framed(): boolean {
    return this.parent !== null && this.parentOrigin !== null;
  }

  post(message: HelperMessageBody): void {
    if (this.parent === null || this.parentOrigin === null) return;

    this.parent.postMessage({ ...message, source: HELPER_SOURCE }, this.parentOrigin);
  }
}

function originOf(value: string | null): string | null {
  if (value === null) return null;

  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.origin : null;
  } catch {
    return null;
  }
}
