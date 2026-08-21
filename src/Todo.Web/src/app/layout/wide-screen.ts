import { Injectable, signal } from '@angular/core';

/**
 * Tailwind's own `xl`, written as the rem value the framework uses, so the breakpoint lives in one
 * place and cannot drift from the `xl:` classes that lay the two columns out.
 */
const wideEnough = '(min-width: 80rem)';

/**
 * Whether the window is wide enough for the task list and the detail panel to stand side by side.
 *
 * A signal rather than a CSS class, and that is not a preference. The panel has to render in one of
 * two places in the DOM - inside its row when narrow, in the right-hand column when wide - because
 * `hidden xl:block` would keep both in the document, and `data-testid="task-detail"` would then
 * match twice on a narrow screen with Playwright silently picking the first. Only an `@if` can
 * guarantee exactly one, and an `@if` needs a signal.
 *
 * The window, not the screen: half a window on a Full HD display would otherwise be forced into two
 * columns of ~460px, where the detail panel's date fields are too narrow to use.
 */
@Injectable({ providedIn: 'root' })
export class WideScreen {
  readonly wide = signal(false);

  constructor() {
    // jsdom has no matchMedia, so a Vitest gets the narrow layout - which is today's behaviour -
    // and a spec that wants the wide one sets the signal itself. Without the guard every spec that
    // renders the task list would throw on a function that is not there.
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const media = window.matchMedia(wideEnough);
    this.wide.set(media.matches);
    media.addEventListener('change', (event) => this.wide.set(event.matches));
  }
}
