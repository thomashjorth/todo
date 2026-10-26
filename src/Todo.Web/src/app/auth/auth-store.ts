import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { AuthClient, LoginRequest } from '../api/todo-client';
import { RELOAD } from './reload';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly client = inject(AuthClient);
  private readonly transloco = inject(TranslocoService);
  private readonly reload = inject(RELOAD);

  readonly error = signal<string | null>(null);

  async login(password: string): Promise<void> {
    this.error.set(null);

    try {
      await firstValueFrom(this.client.login(new LoginRequest({ password })));
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
      return;
    }

    // A whole reload rather than a navigation: the shell asked for the settings, the review, the
    // tags and the model before anyone had logged in, got 401s, and does not ask again on its own.
    this.reload('/');
  }
}
