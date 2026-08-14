import { Injectable, inject, signal } from '@angular/core';
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

const genericFailure = 'Noget gik galt. Prøv igen.';

// A rejected request throws the ApiError body itself; anything else is a failure we cannot explain.
function messageOf(error: unknown): string {
  return error instanceof ApiError && error.message ? error.message : genericFailure;
}

@Injectable({ providedIn: 'root' })
export class RetroStore {
  private readonly client = inject(RetroClient);

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
      this.error.set(messageOf(error));
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
      this.error.set(messageOf(error));
      return undefined;
    }
  }

  async loadAliases(): Promise<void> {
    try {
      const response = await firstValueFrom(this.client.listRetroAliases());
      this.aliases.set(response.aliases);
    } catch (error) {
      this.error.set(messageOf(error));
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
      this.error.set(messageOf(error));
    }
  }
}
