import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AiStore } from '../ai/ai-store';
import { RetroPreviewRow } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { RetroStore } from './retro-store';

@Component({
  selector: 'app-retro-import',
  imports: [DeadlineDate, RouterLink, Shortcut, TranslocoPipe],
  templateUrl: './retro-import.html',
  // The shell's ceiling is raised on xl so the task list can be two columns. This screen is not two
  // columns, and a form stretched over 1440 px is unreadable - so it puts the ceiling back.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class RetroImport {
  protected readonly store = inject(RetroStore);
  protected readonly ai = inject(AiStore);
  protected readonly shortcuts = inject(ShortcutStore);
  private readonly transloco = inject(TranslocoService);

  protected readonly pluralKey = pluralKey;
  protected readonly csv = signal('');
  protected readonly analysed = signal(false);
  protected readonly receipt = signal<string | null>(null);
  protected readonly selectedKeys = signal<ReadonlySet<string>>(new Set());

  protected readonly selectedRows = computed(() =>
    this.store.rows().filter((row) => this.selectedKeys().has(row.key)),
  );

  protected readonly noneMine = computed(
    () => this.store.rows().length > 0 && !this.store.rows().some((row) => row.isMine),
  );

  protected analyse(): void {
    this.receipt.set(null);
    void this.reanalyse();
  }

  protected isSelected(row: RetroPreviewRow): boolean {
    return this.selectedKeys().has(row.key);
  }

  protected select(row: RetroPreviewRow, selected: boolean): void {
    this.selectedKeys.update((keys) => {
      const next = new Set(keys);
      if (selected && !row.alreadyImported) {
        next.add(row.key);
      } else {
        next.delete(row.key);
      }
      return next;
    });
  }

  protected async importSelected(): Promise<void> {
    // A row with a suggestion in use goes in with the suggested title, and the guessed owner where
    // the board named none; the key is the original's, so deduplication does not move.
    const result = await this.store.import(
      this.selectedRows().map((row) => this.ai.asImported(row)),
    );
    if (!result) {
      return;
    }

    this.receipt.set(
      this.transloco.translate('retro.receipt', {
        imported: result.imported,
        skipped: result.skipped,
      }),
    );
    await this.reanalyse();
  }

  protected suggestActions(): void {
    this.ai.suggestRetroActions(this.selectedRows()).catch(() => {});
  }

  private async reanalyse(): Promise<void> {
    // A new analysis may hold other rows, and a suggestion belongs to the row it was asked for.
    this.ai.clearRetroActions();
    await this.store.preview(this.csv());
    this.analysed.set(this.store.error() === null);
    this.selectedKeys.set(
      new Set(
        this.store
          .rows()
          .filter((row) => row.isMine && !row.alreadyImported)
          .map((row) => row.key),
      ),
    );
  }
}
