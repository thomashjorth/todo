import { Injectable, signal } from '@angular/core';

/**
 * The one value that means the user asked for less motion. `no-preference` is a separate query and
 * would answer the opposite, so the string is pinned by a spec rather than left to be retyped.
 */
const asksForLess = '(prefers-reduced-motion: reduce)';

/**
 * Whether the user has asked the system for less motion.
 *
 * Built like `WideScreen` and for the same reason: a media query the app has to follow while it
 * runs, exposed as a signal so a guard can read it without a subscription. The Windows setting can
 * be changed while the app is open, so the listener is not a nicety - reading once at startup would
 * leave the choice stale until the window is reopened.
 *
 * What it is for: section transitions are skipped entirely when this is true. Not dimmed, and not
 * shortened - the duration and easing of a view transition live in the UA stylesheet behind
 * `::view-transition-*`, which is a CSS rule this repo does not allow, so there is no knob between
 * the full animation and none. See section 7 of
 * docs/plans/2026-08-25-section-transitions-design.md.
 */
@Injectable({ providedIn: 'root' })
export class ReducedMotion {
  readonly reduce = signal(false);

  constructor() {
    // jsdom has no matchMedia, so a Vitest gets full motion - which keeps the transition on the
    // path the other specs measure. Without the guard every spec that reaches `TaskStore.load`
    // would throw on a function that is not there. Measured against jsdom 28.1.0.
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const media = window.matchMedia(asksForLess);
    this.reduce.set(media.matches);
    media.addEventListener('change', (event) => this.reduce.set(event.matches));
  }
}
