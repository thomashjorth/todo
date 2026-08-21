import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { RetroPreviewRow } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { RetroStore } from './retro-store';

@Component({
  selector: 'app-retro-import',
  imports: [DeadlineDate, RouterLink, TranslocoPipe],
  templateUrl: './retro-import.html',
  // Skallens loft er hævet på xl, så to spalter har plads på opgavelisten. Den her skærm er
  // ikke to spalter, og en formular strakt over 1440 px er ulæselig — så den sætter loftet igen.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class RetroImport {
  protected readonly store = inject(RetroStore);
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
    const result = await this.store.import(this.selectedRows());
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

  private async reanalyse(): Promise<void> {
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
