import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ShortcutStore {
  readonly altHeld = signal(false);

  private readonly targets = new Map<string, () => void>();

  register(key: string, activate: () => void): void {
    this.targets.set(key.toLowerCase(), activate);
  }

  /**
   * Registering the same key twice is still last-writer-wins, deliberately. What the second
   * argument buys is that the loser's cleanup cannot delete the winner's entry: a row's number
   * changes while the app runs, and the order between two effects' cleanups is not guaranteed.
   */
  unregister(key: string, activate?: () => void): void {
    const lowered = key.toLowerCase();
    if (activate && this.targets.get(lowered) !== activate) {
      return;
    }

    this.targets.delete(lowered);
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
