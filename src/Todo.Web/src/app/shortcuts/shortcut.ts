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

  // null frem for en tom streng: attributten skal være væk, ikke tom, for guarden i E2E påstår
  // at række ti slet ikke har en.
  protected readonly label = computed(() =>
    this.appShortcut() ? shortcutLabel(this.appShortcutModifier(), this.appShortcut()) : null,
  );

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    // En effekt frem for ngOnInit/ngOnDestroy, fordi nøglen ændrer sig i levende live: en række
    // beholder sin komponentinstans (@for sporer på id) og får et nyt nummer, når listen gør.
    // onCleanup afmelder den gamle nøgle, og callbacket sendes med, så oprydningen ikke kan
    // slette en registrering en anden række har overtaget imens.
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

    // Windows-konventionen: en genvej udfører elementets aktiveringshandling, ikke bare
    // markerer det. Et afkrydsningsfelt skifter, et link følges. Kun et tekstfelt har
    // ingen aktivering ud over at få fokus. Fokus flytter i begge tilfælde, fordi et
    // programmatisk click() ikke selv flytter det.
    if (this.appShortcutAction() === 'activate') {
      element.click();
    }
  }
}
