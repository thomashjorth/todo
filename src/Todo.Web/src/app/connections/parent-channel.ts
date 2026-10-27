import { Injectable, InjectionToken, inject } from '@angular/core';

import { resolveLanguage } from '../i18n/resolve-language';
import { HELPER_SOURCE, HelperMessageBody } from './helper-message';

/** The page's query string, where the app names its origin and its language. */
export const PAGE_SEARCH = new InjectionToken<string>('PAGE_SEARCH', {
  factory: () => window.location.search,
});

/** The window around the frame, or null when the page is opened on its own. */
export const PARENT_WINDOW = new InjectionToken<Pick<Window, 'postMessage'> | null>(
  'PARENT_WINDOW',
  {
    factory: () => (window.parent !== window ? window.parent : null),
  },
);

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
