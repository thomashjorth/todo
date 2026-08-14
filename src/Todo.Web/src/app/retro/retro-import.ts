import { Component, computed, inject, signal } from '@angular/core';
import { RetroPreviewRow } from '../api/todo-client';
import { RetroStore } from './retro-store';

@Component({
  selector: 'app-retro-import',
  templateUrl: './retro-import.html',
})
export class RetroImport {
  protected readonly store = inject(RetroStore);

  protected readonly csv = signal('');
  protected readonly analysed = signal(false);
  protected readonly selectedKeys = signal<ReadonlySet<string>>(new Set());

  protected readonly noneMine = computed(
    () => this.store.rows().length > 0 && !this.store.rows().some((row) => row.isMine),
  );

  protected analyse(): void {
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
