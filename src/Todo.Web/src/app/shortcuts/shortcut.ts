import { computed, Directive, effect, ElementRef, inject, input } from '@angular/core';
import { ShortcutModifier, shortcutKey, shortcutLabel } from './shortcut-key';
import { ShortcutStore } from './shortcut-store';

@Directive({
  selector: '[appShortcut]',
  host: {
    '[attr.aria-keyshortcuts]': 'label()',
  },
})
export class Shortcut {
  readonly appShortcut = input.required<string>();
  readonly appShortcutModifier = input<ShortcutModifier>('alt');
  readonly appShortcutAction = input<'focus' | 'activate'>('focus');

  // null rather than an empty string: the attribute has to be gone, not empty, because the E2E
  // guard asserts that row ten has none at all.
  protected readonly label = computed(() =>
    this.appShortcut() ? shortcutLabel(this.appShortcutModifier(), this.appShortcut()) : null,
  );

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    // An effect rather than ngOnInit/ngOnDestroy, because the key changes while the app runs: a
    // row keeps its component instance (@for tracks on id) and gets a new number when the list
    // does. onCleanup unregisters the old key, and the callback goes with it, so the cleanup
    // cannot delete a registration another row has taken over in the meantime.
    effect((onCleanup) => {
      const key = this.appShortcut();
      if (!key) {
        return;
      }

      const registryKey = shortcutKey(this.appShortcutModifier(), key);
      const activate = () => this.trigger();

      this.store.register(registryKey, activate);
      onCleanup(() => this.store.unregister(registryKey, activate));
    });
  }

  private trigger(): void {
    const element = this.host.nativeElement;
    element.focus();

    // The Windows convention: a shortcut performs the element's activation action rather than
    // merely marking it. A checkbox toggles, a link is followed. Only a text field has no
    // activation beyond taking focus. The focus moves either way, because a programmatic
    // click() does not move it on its own.
    if (this.appShortcutAction() === 'activate') {
      element.click();
    }
  }
}
