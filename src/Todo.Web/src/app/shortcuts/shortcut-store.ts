import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ShortcutStore {
  readonly altHeld = signal(false);

  private readonly targets = new Map<string, () => void>();

  register(key: string, activate: () => void): void {
    this.targets.set(key.toLowerCase(), activate);
  }

  unregister(key: string): void {
    this.targets.delete(key.toLowerCase());
  }

  /** True when the key was handled, so the caller knows whether to swallow the event. */
  activate(key: string): boolean {
    const target = this.targets.get(key.toLowerCase());
    target?.();

    return target !== undefined;
  }

  setAltHeld(held: boolean): void {
    this.altHeld.set(held);
  }
}
