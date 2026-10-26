import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { CreateTagRequest, TagRef, TagsClient, UpdateTagRequest } from '../api/todo-client';

/**
 * The user's own tags. Loaded at startup, because three screens read them: the settings group that
 * defines them, the panel that puts them on a task, and the filter above the list.
 */
@Injectable({ providedIn: 'root' })
export class TagStore {
  private readonly client = inject(TagsClient);
  private readonly transloco = inject(TranslocoService);

  readonly tags = signal<readonly TagRef[]>([]);
  readonly error = signal<string | null>(null);

  async load(): Promise<void> {
    try {
      this.tags.set((await firstValueFrom(this.client.listTags())).items);
    } catch {
      // Without its tags the app still works; the settings group shows none and nothing filters.
    }
  }

  /** The reply is reloaded rather than trusted, so the list stays in the server's order. */
  async add(name: string): Promise<boolean> {
    this.error.set(null);
    try {
      await firstValueFrom(this.client.createTag(new CreateTagRequest({ name })));
      await this.load();
      return true;
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
      return false;
    }
  }

  /** A rename, a new colour or both: the server takes the two together (the editing design, 2). */
  async update(id: number, name: string, color: number): Promise<boolean> {
    this.error.set(null);
    try {
      await firstValueFrom(this.client.updateTag(id, new UpdateTagRequest({ name, color })));
      await this.load();
      return true;
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
      return false;
    }
  }

  async remove(id: number): Promise<void> {
    this.error.set(null);
    try {
      await firstValueFrom(this.client.deleteTag(id));
      await this.load();
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }
}
