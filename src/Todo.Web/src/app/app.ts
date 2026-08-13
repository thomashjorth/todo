import { Component, inject, signal } from '@angular/core';
import { HealthClient, HealthResponse } from './api/todo-client';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  private readonly health = inject(HealthClient);

  protected readonly status = signal<HealthResponse | undefined>(undefined);
  protected readonly failed = signal(false);

  constructor() {
    this.health.getHealth().subscribe({
      next: (r) => this.status.set(r),
      error: () => this.failed.set(true),
    });
  }
}
