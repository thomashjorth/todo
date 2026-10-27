import {
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  OnInit,
  afterNextRender,
  inject,
  viewChild,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';

import { ConnectionsStore, SourceName } from './connections-store';
import { ParentChannel } from './parent-channel';

/** How often the page asks the helper how the tunnel stands. A token, so a spec can turn it off. */
export const CONNECTIONS_POLL_MS = new InjectionToken<number | null>('CONNECTIONS_POLL_MS', {
  factory: () => 2000,
});

/**
 * The helper's Connections page: the second root in the app's bundle, started by main.ts on
 * /connections and served by the helper on the PC, never by the server. The selector is app-root
 * because that is the element index.html has - the two roots share one page.
 */
@Component({
  selector: 'app-root',
  imports: [TranslocoPipe],
  templateUrl: './connections-root.html',
})
export class ConnectionsRoot implements OnInit {
  protected readonly store = inject(ConnectionsStore);
  protected readonly parent = inject(ParentChannel);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pollMs = inject(CONNECTIONS_POLL_MS);

  protected readonly sources: SourceName[] = ['jira', 'ado'];

  private readonly page = viewChild.required<ElementRef<HTMLElement>>('page');

  constructor() {
    this.transloco.setActiveLang(this.parent.language);
    document.documentElement.lang = this.parent.language;

    // The app sizes the frame from this, so the page never scrolls inside the settings page. The
    // content's own height rather than the document's: a document is never shorter than the frame
    // showing it, so the frame could grow and never shrink back.
    afterNextRender(() => {
      const page = this.page().nativeElement;
      const post = () =>
        this.parent.post({ type: 'height', height: page.getBoundingClientRect().height });
      post();

      // jsdom has no ResizeObserver; the app keeps the frame's own height there.
      if (typeof ResizeObserver !== 'undefined') {
        const observer = new ResizeObserver(post);
        observer.observe(page);
        this.destroyRef.onDestroy(() => observer.disconnect());
      }
    });
  }

  async ngOnInit(): Promise<void> {
    await this.store.load();
    this.parent.post({ type: 'ready' });

    if (this.pollMs !== null) {
      const timer = setInterval(() => void this.store.load(), this.pollMs);
      this.destroyRef.onDestroy(() => clearInterval(timer));
    }
  }

  protected saveServer(url: string): void {
    void this.store.saveServer(url);
  }

  protected pair(): void {
    void this.store.pair();
  }

  /**
   * Empties the token field once the helper has it: the field is write-only, and a token left in it
   * would sit in the DOM. Not before - a refused save would otherwise lose what the user typed - and
   * only when the field still holds what was sent: a token typed while the address was being saved
   * was never in this save.
   */
  protected async saveSource(
    source: SourceName,
    baseUrl: string,
    token: HTMLInputElement,
  ): Promise<void> {
    const sent = token.value;
    if ((await this.store.saveSource(source, baseUrl, sent)) && token.value === sent) {
      token.value = '';
    }
  }

  protected clearToken(source: SourceName): void {
    void this.store.clearToken(source);
  }
}
