import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

/**
 * The schemes a link may open with. Marked turns a bare address in a note into mailto:, so refusing
 * it would make an ordinary note look broken.
 */
const ALLOWED_SCHEMES = new Set(['http:', 'https:', 'mailto:']);

@Injectable({ providedIn: 'root' })
export class SystemStore {
  private readonly transloco = inject(TranslocoService);

  readonly error = signal<string | null>(null);

  /**
   * Opens a link in a new tab or window, never in place of the app: the app window has neither an
   * address bar nor a back button, so a navigation inside it is one-way. 'noopener', because the page
   * that opens is somebody else's and has no business holding a handle on the app's window.
   */
  openLink(url: string): void {
    this.error.set(null);

    if (!ALLOWED_SCHEMES.has(schemeOf(url))) {
      // The key lives under errors.* because it was the server's error code until 2.0.0.
      // ErrorCodeTranslationTests walks from code to key and never the other way, so a key without
      // a code fells nothing.
      this.error.set(this.transloco.translate('errors.system.unsupportedScheme'));
      return;
    }

    window.open(url, '_blank', 'noopener');
  }

  clearError(): void {
    this.error.set(null);
  }
}

/** The scheme with its colon, spelled as URL.protocol spells it, or '' for text that is not a URL. */
function schemeOf(url: string): string {
  try {
    return new URL(url).protocol;
  } catch {
    return '';
  }
}
