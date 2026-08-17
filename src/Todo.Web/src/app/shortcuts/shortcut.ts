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
  readonly appShortcutAction = input<'focus' | 'click'>('focus');

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngOnInit(): void {
    this.store.register(this.appShortcut(), () => {
      const element = this.host.nativeElement;

      // A checkbox has to be clicked, not focused: focusing it moves the ring and changes
      // nothing, while the keystroke is swallowed anyway. click() also fires the change
      // event the template is listening for.
      if (this.appShortcutAction() === 'click') {
        element.click();
        return;
      }

      element.focus();
    });
  }

  ngOnDestroy(): void {
    this.store.unregister(this.appShortcut());
  }
}
