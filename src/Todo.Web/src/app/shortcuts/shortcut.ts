import { Directive, ElementRef, inject, input, OnDestroy, OnInit } from '@angular/core';
import { ShortcutStore } from './shortcut-store';

@Directive({
  selector: '[appShortcut]',
  host: {
    '[attr.aria-keyshortcuts]': '"Alt+" + appShortcut().toUpperCase()',
  },
})
export class Shortcut implements OnInit, OnDestroy {
  readonly appShortcut = input.required<string>();
  readonly appShortcutAction = input<'focus' | 'activate'>('focus');

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngOnInit(): void {
    this.store.register(this.appShortcut(), () => {
      const element = this.host.nativeElement;
      element.focus();

      // Windows-konventionen: en genvej udfører elementets aktiveringshandling, ikke bare
      // markerer det. Et afkrydsningsfelt skifter, et link følges. Kun et tekstfelt har
      // ingen aktivering ud over at få fokus. Fokus flytter i begge tilfælde, fordi et
      // programmatisk click() ikke selv flytter det.
      if (this.appShortcutAction() === 'activate') {
        element.click();
      }
    });
  }

  ngOnDestroy(): void {
    this.store.unregister(this.appShortcut());
  }
}
