import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AiStore } from './ai/ai-store';
import { HealthClient, HealthResponse } from './api/todo-client';
import { ReviewStore } from './review/review-store';
import { Shortcut } from './shortcuts/shortcut';
import { ShortcutModifier, shortcutKey } from './shortcuts/shortcut-key';
import { ShortcutStore } from './shortcuts/shortcut-store';
import { SystemStore } from './system/system-store';
import { TagStore } from './tags/tag-store';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, Shortcut, TranslocoPipe],
  templateUrl: './app.html',
  host: {
    '(document:keydown)': 'onKeyDown($event)',
    '(document:keyup)': 'onKeyUp($event)',
    '(window:blur)': 'shortcuts.setAltHeld(false)',
  },
})
export class App {
  private readonly health = inject(HealthClient);

  private readonly system = inject(SystemStore);

  private readonly ai = inject(AiStore);

  private readonly tags = inject(TagStore);

  protected readonly shortcuts = inject(ShortcutStore);

  /**
   * Read here rather than only on the review screen, because the marker has to be visible from all
   * six: the whole point of putting it on the nav link instead of on the task list is that it does
   * not depend on where you happen to be standing.
   */
  protected readonly review = inject(ReviewStore);

  protected readonly status = signal<HealthResponse | undefined>(undefined);
  protected readonly failed = signal(false);

  constructor() {
    this.health.getHealth().subscribe({
      next: (r) => this.status.set(r),
      error: () => this.failed.set(true),
    });

    this.review.load().catch(() => {});

    // At startup, because three screens read them: settings, the panel and the list's filter.
    this.tags.load().catch(() => {});

    // Once, here: the AI features hide themselves unless the model answered at startup.
    this.ai.load().catch(() => {});
  }

  /**
   * Built from the app's own origin, because the app does not know its host: a fixed port on this
   * machine under Todo.cmd, the NAS's name once it is deployed, a random port under the tests.
   */
  protected openApiDocs(): void {
    this.system.openLink(`${location.origin}/scalar/`);
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(true);
      return;
    }

    // Ctrl and Meta are still excluded: Ctrl+Alt is AltGr on a Danish keyboard, and swallowing it
    // would break typing @, £ and $. Shift, in contrast, is a layer rather than an exclusion.
    if (event.altKey && !event.ctrlKey && !event.metaKey) {
      const modifier: ShortcutModifier = event.shiftKey ? 'alt-shift' : 'alt';

      if (this.shortcuts.activate(shortcutKey(modifier, event.key))) {
        event.preventDefault();
      }
    }
  }

  protected onKeyUp(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(false);
    }
  }
}
