import {
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';

import { isHelperMessage } from '../connections/helper-message';
import { SystemStore } from '../system/system-store';
import { SettingsStore } from './settings-store';

/** How long the page waits for the helper's `ready` before it says where connections are set up. */
export const HELPER_READY_TIMEOUT_MS = new InjectionToken<number>('HELPER_READY_TIMEOUT_MS', {
  factory: () => 3000,
});

/**
 * The host's view of the hello lags the helper's save by the time it takes the helper to say hello
 * again, so a `changed` is read twice: at once, and once more after this long.
 */
const SECOND_READ_MS = 1500;

/** Tall enough for the server part, until the page says how tall it really is. */
const FALLBACK_HEIGHT = 480;

/**
 * The helper's Connections page in a frame. The page lives on the helper's origin on the PC, so the
 * tokens typed into it never reach this one; what comes back is three messages, and each is taken
 * only from that origin and from this frame's own window.
 */
@Component({
  selector: 'app-helper-frame',
  imports: [TranslocoPipe],
  templateUrl: './helper-frame.html',
  host: {
    '(window:message)': 'receive($event)',
  },
})
export class HelperFrame {
  private readonly settings = inject(SettingsStore);
  private readonly transloco = inject(TranslocoService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);
  private readonly system = inject(SystemStore);

  protected readonly ready = signal(false);
  protected readonly timedOut = signal(false);
  protected readonly height = signal(FALLBACK_HEIGHT);

  /** The page URL from the settings, only if it is http(s). */
  protected readonly pageUrl = computed(() => httpUrl(this.settings.helperPageUrl()));

  /**
   * Where the page is told to post (this origin) and what to speak (this language). Read once: a
   * new src would reload the page and drop whatever the user was typing.
   */
  protected readonly src = computed<SafeResourceUrl | null>(() => {
    const url = this.pageUrl();
    if (url === null) return null;

    const page = new URL(url);
    page.searchParams.set('parent', window.location.origin);
    page.searchParams.set('lang', this.transloco.getActiveLang());

    return this.sanitizer.bypassSecurityTrustResourceUrl(page.toString());
  });

  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');

  constructor() {
    const timer = setTimeout(
      () => this.timedOut.set(!this.ready()),
      inject(HELPER_READY_TIMEOUT_MS),
    );
    this.destroyRef.onDestroy(() => clearTimeout(timer));
  }

  protected openPage(): void {
    const page = this.pageUrl();
    if (page !== null) this.system.openLink(page.toString());
  }

  protected receive(event: MessageEvent): void {
    const page = this.pageUrl();

    if (page === null || event.origin !== page.origin) return;
    const frame = this.frame()?.nativeElement;
    if (frame === undefined || event.source !== frame.contentWindow) return;
    if (!isHelperMessage(event.data)) return;

    switch (event.data.type) {
      case 'ready':
        this.ready.set(true);
        this.timedOut.set(false);
        break;
      case 'height':
        this.height.set(Math.max(1, Math.ceil(event.data.height)));
        break;
      case 'changed': {
        void this.settings.refresh();
        const again = setTimeout(() => void this.settings.refresh(), SECOND_READ_MS);
        this.destroyRef.onDestroy(() => clearTimeout(again));
        break;
      }
    }
  }
}

function httpUrl(value: string | null): URL | null {
  if (value === null) return null;

  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}
