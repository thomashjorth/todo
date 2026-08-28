import { Component, computed, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * One foldable group of the weekly review: a heading that is a button and carries the group's
 * count, and a panel that exists in the DOM only while the group is open.
 *
 * A near-copy of SettingsSection rather than a reuse of it, and the difference is the count: the
 * review's heading answers "is there anything here" before you open it, which is most of what the
 * walk is for. Sharing one component would mean an optional count nobody on the settings page ever
 * passes, and a heading that reads `Sprog (0)` the day somebody passed it by mistake.
 *
 * An attribute selector on &lt;section&gt;, following `li[appTaskRow]`: the host <em>is</em> the
 * section, so the page keeps its own `data-testid` on it and the divider between two groups falls
 * between siblings rather than between wrappers.
 */
@Component({
  selector: 'section[appReviewSection]',
  imports: [TranslocoPipe],
  templateUrl: './review-section.html',
  host: {
    class: 'border-b border-gray-300 dark:border-gray-700',
  },
})
export class ReviewSection {
  /** The transloco key of the group's heading — `review.groups.overdue` and its four siblings. */
  readonly heading = input.required<string>();

  /** The group's own short name, which the panel's id and the button's are both built from. */
  readonly name = input.required<string>();

  /** How many tasks the group holds. Rendered even at zero — that is the answer, not the absence. */
  readonly count = input.required<number>();

  readonly open = input.required<boolean>();

  readonly toggled = output<void>();

  protected readonly panelId = computed(() => `${this.name()}-review-panel`);

  /**
   * The button's id, used twice: `aria-labelledby` on the panel points at it, and the panel's
   * `aria-controls` points back. Derived rather than passed, so the two halves cannot drift.
   */
  protected readonly toggleId = computed(() => `${this.name()}-review-toggle`);
}
