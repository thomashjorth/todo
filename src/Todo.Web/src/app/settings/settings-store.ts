import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { SettingsClient, SettingsRequest } from '../api/todo-client';
import { resolveLanguage } from '../i18n/resolve-language';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';

@Injectable({ providedIn: 'root' })
export class SettingsStore {
  private readonly client = inject(SettingsClient);
  private readonly transloco = inject(TranslocoService);
  private readonly system = inject(SYSTEM_LANGUAGE);

  /** The stored choice, where null means "follow the system" rather than "not read yet". */
  readonly language = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  async start(): Promise<void> {
    try {
      const response = await firstValueFrom(this.client.getSettings());
      this.language.set(response.language ?? null);
    } catch {
      // An app that cannot read its settings still has to open, in the system language.
    }

    await this.apply();
  }

  async choose(language: string | null): Promise<void> {
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.client.updateSettings(new SettingsRequest({ language: language ?? undefined })),
      );
      this.language.set(response.language ?? null);
      await this.apply();
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }

  private async apply(): Promise<void> {
    const language = resolveLanguage(this.language(), this.system);

    this.transloco.setActiveLang(language);
    // A screen reader picks its voice from the document, not from Transloco.
    document.documentElement.lang = language;

    await firstValueFrom(this.transloco.load(language));
  }
}
