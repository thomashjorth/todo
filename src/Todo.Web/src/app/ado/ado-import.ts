import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AdoPreviewRow } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { SettingsStore } from '../settings/settings-store';
import { SystemStore } from '../system/system-store';
import { AdoStore } from './ado-store';

@Component({
  selector: 'app-ado-import',
  imports: [DeadlineDate, RouterLink, TranslocoPipe],
  templateUrl: './ado-import.html',
  // Skallens loft er hævet på xl, så to spalter har plads på opgavelisten. Den her skærm er
  // ikke to spalter, og en formular strakt over 1440 px er ulæselig — så den sætter loftet igen.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class AdoImport {
  protected readonly store = inject(AdoStore);
  protected readonly settings = inject(SettingsStore);
  protected readonly system = inject(SystemStore);

  private readonly transloco = inject(TranslocoService);

  protected readonly pluralKey = pluralKey;

  /**
   * True once a preview came back without an error. It is what tells "Azure DevOps has nothing
   * assigned to you" apart from "nobody has asked yet" - both are an empty list, and only one of
   * them is worth saying out loud.
   */
  protected readonly previewed = signal(false);
  protected readonly receipt = signal<string | null>(null);
  protected readonly selectedKeys = signal<ReadonlySet<string>>(new Set());

  /**
   * Which row's open failed, and what to say about it. SystemStore.error is a single screen-level
   * signal and cannot say which row it belongs to - and the message has to sit next to the button
   * that was pressed, because a notice at the top of a twenty-row list in a 480 px column is out of
   * sight.
   */
  protected readonly openError = signal<{ key: string; message: string } | null>(null);

  /**
   * Read from the settings rather than by letting the call fail: an empty list with an error over it
   * teaches the user less than a link to the page that fixes it. The project is in here even though
   * the server's own <c>IsConfigured</c> only wants a collection URL and a token, because Azure
   * DevOps scopes a query by URL path - so a preview without a project is refused all the same, with
   * a different code.
   */
  protected readonly configured = computed(
    () =>
      filled(this.settings.adoBaseUrl()) &&
      filled(this.settings.adoProject()) &&
      this.settings.hasAdoToken(),
  );

  /**
   * One rule for what may be ticked, and it is the store's. A checkbox disabled by a rule of its own
   * would keep looking right while the store handed import a row it had excluded.
   */
  private readonly selectableKeys = computed(
    () => new Set(this.store.selectable().map((row) => row.key)),
  );

  /**
   * Filtered through the store's own list, so a checkbox forced on from outside the UI still cannot
   * put an excluded row on the wire.
   */
  protected readonly selectedRows = computed(() =>
    this.store.selectable().filter((row) => this.selectedKeys().has(row.key)),
  );

  /** The same guard for the closures: the store decides which rows may be closed, not the checkbox. */
  private readonly closableKeys = computed(
    () => new Set(this.store.closable().map((row) => row.key)),
  );

  protected readonly selectedClosures = computed(() =>
    this.store.closable().filter((row) => this.selectedKeys().has(row.key)),
  );

  /** What the button acts on, which is both lists: one control, one press, two kinds of work. */
  protected readonly chosenCount = computed(
    () => this.selectedRows().length + this.selectedClosures().length,
  );

  /** An empty answer from Azure DevOps is an answer, not a failure. */
  protected readonly noneAssigned = computed(
    () => this.previewed() && this.store.rows().length === 0,
  );

  /**
   * Counted as two disjoint sets: a row with an <c>excluded</c> code counts as excluded even when it
   * was imported before, so the two numbers add up to the rows on screen rather than double-counting
   * one row into both sentences.
   */
  protected readonly excludedCount = computed(
    () => this.store.rows().filter((row) => row.excluded).length,
  );

  /**
   * A row that offers a closure is deliberately not counted here. It was imported before, but it is
   * not one of the dead rows this sentence is about - it has something to do, and saying otherwise
   * would tell the user the list is idle while a checkbox on it is ticked.
   */
  protected readonly alreadyImportedCount = computed(
    () =>
      this.store
        .rows()
        .filter((row) => !row.excluded && row.alreadyImported && !row.suggestsClosing).length,
  );

  protected readonly nothingToSelect = computed(
    () =>
      this.store.rows().length > 0 &&
      this.store.selectable().length === 0 &&
      this.store.closable().length === 0,
  );

  protected preview(): void {
    this.receipt.set(null);
    void this.reload();
  }

  /**
   * Opens the work item in the system's browser, so a row can be read before it is imported.
   * <c>openLink</c> catches on its own and sets its error signal, so an empty catch here would throw
   * the only sign away; the message is copied onto this row, because the store's signal is
   * screen-level and the row is where the user is looking.
   */
  protected async openItem(row: AdoPreviewRow): Promise<void> {
    this.openError.set(null);

    await this.system.openLink(row.url);

    const message = this.system.error();

    if (message) {
      this.openError.set({ key: row.key, message });
    }
  }

  protected isSelected(row: AdoPreviewRow): boolean {
    return this.selectedKeys().has(row.key);
  }

  protected isBlocked(row: AdoPreviewRow): boolean {
    return !this.selectableKeys().has(row.key) && !this.closableKeys().has(row.key);
  }

  /**
   * <c>excluded</c> is an error code, and <c>errors.&lt;code&gt;</c> is the very path apiErrorMessage
   * takes - the same key serves the reason on a row and the message on a refusal.
   */
  protected excludedReason(row: AdoPreviewRow): string {
    return this.transloco.translate(`errors.${row.excluded}`);
  }

  /**
   * The day a work item entered its current state, from a timestamp rather than from a date-only
   * string. It therefore goes through <c>new Date</c> on purpose, which is the opposite of the rule
   * for a deadline: a deadline has no time zone and reading it as UTC midnight slides it a day, while
   * this is an instant and the local day is exactly the question. That is why it does not share
   * formatDeadline, whose regex would silently answer with an empty string for a timestamp.
   */
  protected waitingSince(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    }).format(new Date(value));
  }

  protected select(row: AdoPreviewRow, selected: boolean): void {
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
    const result = await this.store.import(this.selectedRows(), this.selectedClosures());
    if (!result) {
      return;
    }

    // Two sentences rather than one with a third number in it: the closing half only happened if
    // something was closed, and a receipt that always said "0 lukket" would train the eye to skip
    // the line that matters on the day it is not zero.
    const closed =
      result.closed > 0
        ? '. ' +
          this.transloco.translate(pluralKey(result.closed, 'ado.receiptClosed'), {
            count: result.closed,
          })
        : '';

    this.receipt.set(
      this.transloco.translate('ado.receipt', {
        imported: result.imported,
        skipped: result.skipped,
      }) + closed,
    );
    await this.reload();
  }

  private async reload(): Promise<void> {
    await this.store.preview();
    this.previewed.set(this.store.error() === null);
    // The union, so a suggested closure arrives ticked like everything else: it is a suggestion,
    // and the user unticks what should not happen.
    this.selectedKeys.set(
      new Set([...this.store.selectable(), ...this.store.closable()].map((row) => row.key)),
    );
  }
}

function filled(value: string | null): boolean {
  return value !== null && value.trim() !== '';
}
