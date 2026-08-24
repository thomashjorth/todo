import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Shortcut } from './shortcut';
import { ShortcutStore } from './shortcut-store';
import { ShortcutModifier } from './shortcut-key';

@Component({
  imports: [Shortcut],
  template: `<button [appShortcut]="key()" [appShortcutModifier]="modifier()">x</button>`,
})
class Host {
  readonly key = signal('3');
  readonly modifier = signal<ShortcutModifier>('alt');
}

describe('Shortcut', () => {
  it('should announce the layer in aria-keyshortcuts', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;

    expect(button.getAttribute('aria-keyshortcuts')).toBe('Alt+3');

    fixture.componentInstance.modifier.set('alt-shift');
    fixture.detectChanges();

    expect(button.getAttribute('aria-keyshortcuts')).toBe('Alt+Shift+3');
  });

  // Række ti og frem har intet nummer. Uden guarden registreres den tomme streng, og den første
  // uhåndterede tast rammer den.
  it('should register nothing for an empty key', () => {
    const fixture = TestBed.createComponent(Host);
    const store = TestBed.inject(ShortcutStore);
    fixture.componentInstance.key.set('');
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;

    expect(store.activate('alt+')).toBe(false);
    expect(button.hasAttribute('aria-keyshortcuts')).toBe(false);
  });

  // @for sporer på task.id, så en søgning der omfordeler 1–9 giver SAMME instans et nyt nummer.
  it('should follow the key when it changes, and let the old one go', () => {
    const fixture = TestBed.createComponent(Host);
    const store = TestBed.inject(ShortcutStore);
    fixture.detectChanges();

    expect(store.activate('alt+3')).toBe(true);

    fixture.componentInstance.key.set('5');
    fixture.detectChanges();

    expect(store.activate('alt+3')).toBe(false);
    expect(store.activate('alt+5')).toBe(true);
  });
});
