import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HealthClient, HealthResponse } from './api/todo-client';
import { Shortcut } from './shortcuts/shortcut';
import { ShortcutModifier, shortcutKey } from './shortcuts/shortcut-key';
import { ShortcutStore } from './shortcuts/shortcut-store';
import { SystemStore } from './system/system-store';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, Shortcut, TranslocoPipe],
  templateUrl: './app.html',
  host: {
    '(document:keydown)': 'onKeyDown($event)',
    '(document:keyup)': 'onKeyUp($event)',
    '(window:blur)': 'shortcuts.setAltHeld(false)',
  },
})
export class App {
  private readonly health = inject(HealthClient);

  private readonly system = inject(SystemStore);

  protected readonly shortcuts = inject(ShortcutStore);

  protected readonly status = signal<HealthResponse | undefined>(undefined);
  protected readonly failed = signal(false);

  constructor() {
    this.health.getHealth().subscribe({
      next: (r) => this.status.set(r),
      error: () => this.failed.set(true),
    });
  }

  /** Porten er tilfældig, så URL'en bygges fra appens egen origin. */
  protected openApiDocs(): void {
    this.system.openLink(`${location.origin}/scalar/`).catch(() => {});
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(true);
      return;
    }

    // Ctrl og Meta er stadig udenfor: Ctrl+Alt er AltGr på et dansk tastatur, og at spise den
    // ville ødelægge indtastning af @, £ og $. Shift er derimod et lag og ikke en udelukkelse.
    if (event.altKey && !event.ctrlKey && !event.metaKey) {
      const modifier: ShortcutModifier = event.shiftKey ? 'alt-shift' : 'alt';

      if (this.shortcuts.activate(shortcutKey(modifier, event.key))) {
        event.preventDefault();
      }
    }
  }

  protected onKeyUp(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(false);
    }
  }
}
