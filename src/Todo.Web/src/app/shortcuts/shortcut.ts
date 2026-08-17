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

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngOnInit(): void {
    this.store.register(this.appShortcut(), () => this.host.nativeElement.focus());
  }

  ngOnDestroy(): void {
    this.store.unregister(this.appShortcut());
  }
}
