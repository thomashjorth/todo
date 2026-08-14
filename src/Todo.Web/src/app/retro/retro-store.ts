import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
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
      this.error.set(apiErrorMessage(this.transloco, error));
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
      this.error.set(apiErrorMessage(this.transloco, error));
      return undefined;
    }
  }

  async loadAliases(): Promise<void> {
    this.error.set(null);
    try {
      const response = await firstValueFrom(this.client.listRetroAliases());
      this.aliases.set(response.aliases);
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
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
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }
}
