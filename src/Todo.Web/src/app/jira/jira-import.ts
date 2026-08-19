import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { JiraPreviewRow } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { SettingsStore } from '../settings/settings-store';
import { SystemStore } from '../system/system-store';
import { JiraStore } from './jira-store';

@Component({
  selector: 'app-jira-import',
  imports: [DeadlineDate, RouterLink, TranslocoPipe],
  templateUrl: './jira-import.html',
})
export class JiraImport {
  protected readonly store = inject(JiraStore);
  protected readonly settings = inject(SettingsStore);
  protected readonly system = inject(SystemStore);

  private readonly transloco = inject(TranslocoService);

  protected readonly pluralKey = pluralKey;

  /**
   * True once a preview came back without an error. It is what tells "Jira has nothing assigned to
   * you" apart from "nobody has asked yet" — both are an empty list, and only one of them is worth
   * saying out loud.
   */
  protected readonly previewed = signal(false);
  protected readonly receipt = signal<string | null>(null);
  protected readonly selectedKeys = signal<ReadonlySet<string>>(new Set());

  /**
   * Which row's open failed, and what to say about it. SystemStore.error is a single screen-level
   * signal and cannot say which row it belongs to — and the message has to sit next to the button
   * that was pressed, because a notice at the top of a twenty-row list in a 480 px column is out of
   * sight, which is the silence this closes.
   */
  protected readonly openError = signal<{ key: string; message: string } | null>(null);

  /**
   * Read from the settings rather than by letting the call fail: an empty list with an error over
   * it teaches the user less than a link to the page that fixes it. The project key is in here
   * even though the server's own <c>IsConfigured</c> only wants a base URL and a token, because a
   * preview without a project key is refused all the same — with a different code.
   */
  protected readonly configured = computed(
    () =>
      filled(this.settings.jiraBaseUrl()) &&
      filled(this.settings.jiraProjectKey()) &&
      this.settings.hasJiraToken(),
  );

  /**
   * One rule for what may be ticked, and it is the store's. A checkbox disabled by a rule of its
   * own would keep looking right while the store handed import a row it had excluded.
   */
  private readonly selectableKeys = computed(
    () => new Set(this.store.selectable().map((row) => row.key)),
  );

  /**
   * Filtered through the store's own list, so a checkbox forced on from outside the UI still
   * cannot put an excluded row on the wire.
   */
  protected readonly selectedRows = computed(() =>
    this.store.selectable().filter((row) => this.selectedKeys().has(row.key)),
  );

  /** An empty answer from Jira is an answer, not a failure. */
  protected readonly noneAssigned = computed(
    () => this.previewed() && this.store.rows().length === 0,
  );

  /**
   * Counted as two disjoint sets: a row with an <c>excluded</c> code counts as excluded even when
   * it was imported before, so the two numbers add up to the rows on screen rather than
   * double-counting one row into both sentences.
   */
  protected readonly excludedCount = computed(
    () => this.store.rows().filter((row) => row.excluded).length,
  );

  protected readonly alreadyImportedCount = computed(
    () => this.store.rows().filter((row) => !row.excluded && row.alreadyImported).length,
  );

  protected readonly nothingToSelect = computed(
    () => this.store.rows().length > 0 && this.store.selectable().length === 0,
  );

  protected preview(): void {
    this.receipt.set(null);
    void this.reload();
  }

  /**
   * Opens the issue in the system's browser, so a row can be read before it is imported. Failing to
   * open used to be silent: <c>openLink</c> catches on its own and sets its error signal, so the
   * empty catch this replaces threw the only sign away. The message is copied onto this row, because
   * the store's signal is screen-level and the row is where the user is looking.
   */
  protected async openIssue(row: JiraPreviewRow): Promise<void> {
    this.openError.set(null);

    await this.system.openLink(row.url);

    const message = this.system.error();

    if (message) {
      this.openError.set({ key: row.key, message });
    }
  }

  protected isSelected(row: JiraPreviewRow): boolean {
    return this.selectedKeys().has(row.key);
  }

  protected isBlocked(row: JiraPreviewRow): boolean {
    return !this.selectableKeys().has(row.key);
  }

  /**
   * <c>excluded</c> is an error code, and <c>errors.&lt;code&gt;</c> is the very path
   * apiErrorMessage takes — the same key serves the reason on a row and the message on a refusal.
   */
  protected excludedReason(row: JiraPreviewRow): string {
    return this.transloco.translate(`errors.${row.excluded}`);
  }

  protected select(row: JiraPreviewRow, selected: boolean): void {
    this.selectedKeys.update((keys) => {
      const next = new Set(keys);
      if (selected && !this.isBlocked(row)) {
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
      this.transloco.translate('jira.receipt', {
        imported: result.imported,
        skipped: result.skipped,
      }),
    );
    await this.reload();
  }

  private async reload(): Promise<void> {
    await this.store.preview();
    this.previewed.set(this.store.error() === null);
    this.selectedKeys.set(new Set(this.store.selectable().map((row) => row.key)));
  }
}

function filled(value: string | null): boolean {
  return value !== null && value.trim() !== '';
}
