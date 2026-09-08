import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AdoPreviewRow, ClosureReason, ReopenTarget } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { SettingsStore } from '../settings/settings-store';
import { SystemStore } from '../system/system-store';
import { AdoStore } from './ado-store';

@Component({
  selector: 'app-ado-import',
  imports: [DeadlineDate, RouterLink, TranslocoPipe],
  templateUrl: './ado-import.html',
  // The shell's ceiling is raised on xl so the task list can be two columns. This screen is not two
  // columns, and a form stretched over 1440 px is unreadable - so it puts the ceiling back.
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
   * The one enum member each template branch compares against, exposed the way task-detail.ts exposes
   * TodoStatus.WaitingFor. A string enum cannot be compared to a string literal under
   * strictTemplates - the two types have no overlap - so the alternative is an untyped comparison,
   * which is what these two lines buy their way out of.
   */
  protected readonly unassigned = ClosureReason.Unassigned;
  protected readonly waitingFor = ReopenTarget.WaitingFor;

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

  /** And the third: the store decides which rows may be reopened, not the checkbox. */
  private readonly reopenableKeys = computed(
    () => new Set(this.store.reopenable().map((row) => row.key)),
  );

  protected readonly selectedReopenings = computed(() =>
    this.store.reopenable().filter((row) => this.selectedKeys().has(row.key)),
  );

  /** What the button acts on, which is all three lists: one control, one press, three kinds of work. */
  protected readonly chosenCount = computed(
    () =>
      this.selectedRows().length +
      this.selectedClosures().length +
      this.selectedReopenings().length,
  );

  /**
   * What the button says it will do, as one clause per set it acts on. Three sets make seven
   * combinations, and a chain of seven branches is the kind nobody dares touch - a fourth set would
   * make it fifteen. Joining the clauses scales for free, and a single set renders byte for byte
   * what the old chain rendered, which is what keeps the assertions that predate this one honest
   * rather than accidentally still true.
   *
   * Joined with a middle dot rather than a comma, and that is deliberate: Danish does not
   * capitalise mid-list, so "Importér 1 sag, Luk 1 opgave" would be wrong - and lowercasing the
   * clause in code is a locale hack that breaks on the first language that disagrees. Each clause
   * stands as its own fragment instead. Do not "fix" this to a comma.
   *
   * The counts come from the three sets and never from `store.total()`, which is the source's
   * number for the fetch: it does not count a reconciliation row, so it cannot say what the button
   * is about to do.
   */
  protected readonly chosenLabel = computed(() => {
    const clauses: string[] = [];
    const say = (count: number, base: string): void => {
      if (count > 0) {
        clauses.push(this.transloco.translate(pluralKey(count, base), { count }));
      }
    };

    say(this.selectedRows().length, 'ado.import');
    say(this.selectedClosures().length, 'ado.close');
    say(this.selectedReopenings().length, 'ado.reopen');

    // Nothing ticked still needs a sentence, and it is the importing one: the button is disabled
    // here, and "Importér 0 sager" says which of the three the empty selection is about.
    return clauses.length === 0
      ? this.transloco.translate(pluralKey(0, 'ado.import'), { count: 0 })
      : clauses.join(' · ');
  });

  /**
   * How many of the rows on screen the fetch actually answered with. `store.total()` is the source's
   * number for the fetch alone, so pairing it with the length of `rows` said "Viser 4 af 3 sager."
   * the moment anything was reconciled - measured with a probe before the row carried the flag.
   */
  protected readonly shown = computed(
    () => this.store.rows().filter((row) => !row.reconciled).length,
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
   * A row that offers a closure or a reopening is deliberately not counted here. It was imported
   * before, but it is not one of the dead rows this sentence is about - it has something to do, and
   * saying otherwise would tell the user the list is idle while a checkbox on it is ticked.
   */
  protected readonly alreadyImportedCount = computed(
    () =>
      this.store
        .rows()
        .filter(
          (row) =>
            !row.excluded && row.alreadyImported && !row.suggestsClosing && !row.suggestsReopening,
        ).length,
  );

  protected readonly nothingToSelect = computed(
    () =>
      this.store.rows().length > 0 &&
      this.store.selectable().length === 0 &&
      this.store.closable().length === 0 &&
      this.store.reopenable().length === 0,
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
    return (
      !this.selectableKeys().has(row.key) &&
      !this.closableKeys().has(row.key) &&
      !this.reopenableKeys().has(row.key)
    );
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
    const result = await this.store.import(
      this.selectedRows(),
      this.selectedClosures(),
      this.selectedReopenings(),
    );
    if (!result) {
      return;
    }

    // A sentence per thing that happened rather than one with three numbers in it: a receipt that
    // always said "0 lukket. 0 genåbnet." would train the eye to skip the line that matters on the
    // day it is not zero.
    //
    // The full stop goes in front of the first extra sentence and nowhere else, because `receipt`
    // ends without one while each extra ends with its own. Measured: appending ". " before every
    // extra gave "1 opgave lukket.. 1 opgave genåbnet.".
    const extras = (
      [
        ['ado.receiptClosed', result.closed],
        ['ado.receiptReopened', result.reopened],
      ] as const
    )
      .filter(([, count]) => count > 0)
      .map(([base, count]) => this.transloco.translate(pluralKey(count, base), { count }));

    this.receipt.set(
      this.transloco.translate('ado.receipt', {
        imported: result.imported,
        skipped: result.skipped,
      }) + (extras.length === 0 ? '' : '. ' + extras.join(' ')),
    );
    await this.reload();
  }

  private async reload(): Promise<void> {
    await this.store.preview();
    this.previewed.set(this.store.error() === null);
    // The union of all three, so a suggested closure or reopening arrives ticked like everything
    // else: they are suggestions, and the user unticks what should not happen.
    this.selectedKeys.set(
      new Set(
        [...this.store.selectable(), ...this.store.closable(), ...this.store.reopenable()].map(
          (row) => row.key,
        ),
      ),
    );
  }
}

function filled(value: string | null): boolean {
  return value !== null && value.trim() !== '';
}
