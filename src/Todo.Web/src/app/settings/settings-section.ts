import { Component, computed, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * One foldable group on the settings page: a heading that is a button, and a panel that exists in
 * the DOM only while the group is open.
 *
 * A component with typed inputs rather than a shared <ng-template>: a template with `let-`
 * variables has context type `any`, which `strictTemplates` does not check and
 * `[ngTemplateOutletContext]` does not reconcile, so five groups sharing one would share no types
 * either. The body arrives through <ng-content>, which is instantiated and destroyed with the
 * `@if` below.
 *
 * An attribute selector on <section>, following `li[appTaskRow]`: the host <em>is</em> the section,
 * so the page keeps its own `data-testid` on it and the divider between two groups falls between
 * siblings rather than between wrappers.
 */
@Component({
  selector: 'section[appSettingsSection]',
  imports: [TranslocoPipe],
  templateUrl: './settings-section.html',
  host: {
    class: 'border-b border-gray-300 dark:border-gray-700',
  },
})
export class SettingsSection {
  /** The transloco key of the group's heading — `settings.groups.jira` and its four siblings. */
  readonly heading = input.required<string>();

  /** The group's own short name, which the panel's id and the button's are both built from. */
  readonly name = input.required<string>();

  readonly open = input.required<boolean>();

  readonly toggled = output<void>();

  protected readonly panelId = computed(() => `${this.name()}-section-panel`);

  /**
   * The button's id, used twice: `aria-labelledby` on the panel points at it, and the panel's
   * `aria-controls` points back. Derived rather than passed, so the two halves cannot drift.
   */
  protected readonly toggleId = computed(() => `${this.name()}-section-toggle`);
}
