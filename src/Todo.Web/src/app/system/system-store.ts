import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { OpenLinkRequest, SystemClient } from '../api/todo-client';

@Injectable({ providedIn: 'root' })
export class SystemStore {
  private readonly client = inject(SystemClient);
  private readonly transloco = inject(TranslocoService);

  readonly error = signal<string | null>(null);

  /** Opens a link outside the app: the window has no address bar to come back from. */
  async openLink(url: string): Promise<void> {
    this.error.set(null);
    try {
      await firstValueFrom(this.client.openLink(new OpenLinkRequest({ url })));
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }

  clearError(): void {
    this.error.set(null);
  }
}
