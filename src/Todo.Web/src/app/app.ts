import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HealthClient, HealthResponse } from './api/todo-client';
import { Shortcut } from './shortcuts/shortcut';
import { ShortcutStore } from './shortcuts/shortcut-store';

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

  protected readonly shortcuts = inject(ShortcutStore);

  protected readonly status = signal<HealthResponse | undefined>(undefined);
  protected readonly failed = signal(false);

  constructor() {
    this.health.getHealth().subscribe({
      next: (r) => this.status.set(r),
      error: () => this.failed.set(true),
    });
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(true);
      return;
    }

    // Alt+bogstav, og kun når Alt er den eneste modifikator: Ctrl+Alt er AltGr på et dansk
    // tastatur, og at spise den ville ødelægge indtastning af @, £ og $.
    if (event.altKey && !event.ctrlKey && !event.metaKey && this.shortcuts.activate(event.key)) {
      event.preventDefault();
    }
  }

  protected onKeyUp(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(false);
    }
  }
}
