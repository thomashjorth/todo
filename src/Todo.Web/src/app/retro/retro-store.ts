import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  ApiError,
  RetroAliasesRequest,
  RetroClient,
  RetroImportRequest,
  RetroImportResponse,
  RetroImportRow,
  RetroPreviewRequest,
  RetroPreviewRow,
} from '../api/todo-client';

@Injectable({ providedIn: 'root' })
export class RetroStore {
  private readonly client = inject(RetroClient);
  private readonly transloco = inject(TranslocoService);

  readonly rows = signal<RetroPreviewRow[]>([]);
  readonly skippedRatingCards = signal(0);
  readonly aliases = signal<string[]>([]);
  readonly error = signal<string | null>(null);

  async preview(csv: string): Promise<void> {
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.client.previewRetro(new RetroPreviewRequest({ csv })),
      );
      this.rows.set(response.rows);
      this.skippedRatingCards.set(response.skippedRatingCards);
    } catch (error) {
      this.rows.set([]);
      this.skippedRatingCards.set(0);
      this.error.set(this.messageOf(error));
    }
  }

  async import(rows: readonly RetroPreviewRow[]): Promise<RetroImportResponse | undefined> {
    if (rows.length === 0) {
      return undefined;
    }

    this.error.set(null);
    const request = new RetroImportRequest({
      rows: rows.map(
        (row) =>
          new RetroImportRow({
            key: row.key,
            title: row.title,
            requester: row.owner,
            deadline: row.deadline,
          }),
      ),
    });

    try {
      return await firstValueFrom(this.client.importRetro(request));
    } catch (error) {
      this.error.set(this.messageOf(error));
      return undefined;
    }
  }

  async loadAliases(): Promise<void> {
    try {
      const response = await firstValueFrom(this.client.listRetroAliases());
      this.aliases.set(response.aliases);
    } catch (error) {
      this.error.set(this.messageOf(error));
    }
  }

  async saveAliases(list: readonly string[]): Promise<void> {
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.client.replaceRetroAliases(new RetroAliasesRequest({ aliases: [...list] })),
      );
      this.aliases.set(response.aliases);
    } catch (error) {
      this.error.set(this.messageOf(error));
    }
  }

  // A rejected request throws the ApiError body itself, and its code is the translation key.
  // A code with no translation yet still says something: the server's own English message.
  private messageOf(error: unknown): string {
    if (!(error instanceof ApiError)) {
      return this.transloco.translate('errors.generic');
    }

    const key = `errors.${error.code}`;
    const translated = this.transloco.translate(key);

    return translated === key
      ? error.message || this.transloco.translate('errors.generic')
      : translated;
  }
}
