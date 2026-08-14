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
  protected readonly receipt = signal<string | null>(null);
  protected readonly selectedKeys = signal<ReadonlySet<string>>(new Set());

  protected readonly selectedRows = computed(() =>
    this.store.rows().filter((row) => this.selectedKeys().has(row.key)),
  );

  protected readonly noneMine = computed(
    () => this.store.rows().length > 0 && !this.store.rows().some((row) => row.isMine),
  );

  constructor() {
    void this.store.loadAliases();
  }

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

    this.receipt.set(`${result.imported} importeret, ${result.skipped} sprunget over`);
    await this.reanalyse();
  }

  protected addAlias(input: HTMLInputElement): void {
    const alias = input.value.trim();
    if (!alias || this.store.aliases().includes(alias)) {
      return;
    }

    input.value = '';
    void this.saveAliases([...this.store.aliases(), alias]);
  }

  protected removeAlias(alias: string): void {
    void this.saveAliases(this.store.aliases().filter((a) => a !== alias));
  }

  private async saveAliases(aliases: string[]): Promise<void> {
    await this.store.saveAliases(aliases);
    if (this.analysed()) {
      await this.reanalyse();
    }
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
