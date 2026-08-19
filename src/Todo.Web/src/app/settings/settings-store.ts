import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
  JiraTokenRequest,
  SettingsClient,
  SettingsRequest,
  SettingsResponse,
} from '../api/todo-client';
import { resolveLanguage } from '../i18n/resolve-language';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';

/**
 * The fields a caller wants to change. Everything left out keeps the value the store already
 * holds — it is <em>not</em> left out of the request, because the API is a full replacement.
 */
export interface SettingsChanges {
  language?: string | null;
  jiraBaseUrl?: string | null;
  jiraProjectKey?: string | null;
  jiraWaitingStatuses?: readonly string[];
  jiraIncludeWaiting?: boolean;
}

@Injectable({ providedIn: 'root' })
export class SettingsStore {
  private readonly client = inject(SettingsClient);
  private readonly transloco = inject(TranslocoService);
  private readonly system = inject(SYSTEM_LANGUAGE);

  /** The stored choice, where null means "follow the system" rather than "not read yet". */
  readonly language = signal<string | null>(null);
  readonly jiraBaseUrl = signal<string | null>(null);
  readonly jiraProjectKey = signal<string | null>(null);
  readonly jiraWaitingStatuses = signal<string[]>([]);
  readonly jiraIncludeWaiting = signal(false);

  /**
   * Whether a token is stored. There is deliberately no signal for the token itself: it is
   * write-only, and one held here would sit in every template's scope and outlive the page.
   */
  readonly hasJiraToken = signal(false);

  readonly error = signal<string | null>(null);

  async start(): Promise<void> {
    try {
      this.read(await firstValueFrom(this.client.getSettings()));
    } catch {
      // An app that cannot read its settings still has to open, in the system language.
    }

    await this.apply();
  }

  /** The language select's one path to the server, so it cannot forget the other four fields. */
  async choose(language: string | null): Promise<void> {
    await this.save({ language });
  }

  /**
   * PUT /api/settings is a full replacement and the backend reads an absent field as "clear", so
   * every field goes with every save — the same reason TaskStore.update builds a `current` object.
   * A field whose value <em>is</em> the cleared one (no URL, no statuses, waiting off) is sent as
   * absent, because that is how the wire spells cleared; language especially, which the API
   * rejects as an empty string but accepts as missing.
   */
  async save(changes: SettingsChanges): Promise<void> {
    const next: SettingsChanges = {
      language: this.language(),
      jiraBaseUrl: this.jiraBaseUrl(),
      jiraProjectKey: this.jiraProjectKey(),
      jiraWaitingStatuses: this.jiraWaitingStatuses(),
      jiraIncludeWaiting: this.jiraIncludeWaiting(),
      ...changes,
    };

    const statuses = [...(next.jiraWaitingStatuses ?? [])];
    const request = new SettingsRequest({
      language: blank(next.language),
      jiraBaseUrl: blank(next.jiraBaseUrl),
      jiraProjectKey: blank(next.jiraProjectKey),
      jiraWaitingStatuses: statuses.length === 0 ? undefined : statuses,
      jiraIncludeWaiting: next.jiraIncludeWaiting === true ? true : undefined,
    });

    this.error.set(null);
    try {
      this.read(await firstValueFrom(this.client.updateSettings(request)));
      await this.apply();
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }

  /**
   * Answers whether the token was stored, because the caller has to clear its own field and the
   * store cannot do it: the value never comes in here.
   */
  async setToken(token: string): Promise<boolean> {
    this.error.set(null);
    try {
      this.read(await firstValueFrom(this.client.setJiraToken(new JiraTokenRequest({ token }))));
      return true;
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
      return false;
    }
  }

  async clearToken(): Promise<void> {
    this.error.set(null);
    try {
      this.read(await firstValueFrom(this.client.clearJiraToken()));
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }

  /** All four routes answer with the whole settings shape, so all four are read the same way. */
  private read(response: SettingsResponse): void {
    this.language.set(response.language ?? null);
    this.jiraBaseUrl.set(response.jiraBaseUrl ?? null);
    this.jiraProjectKey.set(response.jiraProjectKey ?? null);
    this.jiraWaitingStatuses.set(response.jiraWaitingStatuses);
    this.jiraIncludeWaiting.set(response.jiraIncludeWaiting);
    this.hasJiraToken.set(response.hasJiraToken);
  }

  private async apply(): Promise<void> {
    const language = resolveLanguage(this.language(), this.system);

    this.transloco.setActiveLang(language);
    // A screen reader picks its voice from the document, not from Transloco.
    document.documentElement.lang = language;

    await firstValueFrom(this.transloco.load(language));
  }
}

function blank(value: string | null | undefined): string | undefined {
  return value === null || value === undefined || value.trim() === '' ? undefined : value;
}
