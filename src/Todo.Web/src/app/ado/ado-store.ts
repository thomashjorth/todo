import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
  AdoClient,
  AdoClosureRow,
  AdoImportRequest,
  AdoImportResponse,
  AdoImportRow,
  AdoPreviewRow,
  AdoReopeningRow,
  ClosureReason,
} from '../api/todo-client';

/**
 * A preview row the source actually answered about, so its state and type are there to send on.
 *
 * The two fields became nullable with reconciliation, because a key Azure DevOps does not know has
 * neither - no answer came back to take them from. Such a row arrives carrying `excluded`, so it is
 * in neither `selectable` nor `closable` and cannot reach the mappings below; the compiler has no
 * way to know that, and this is what answers it.
 *
 * A filter rather than a `!`, and the reason is what happens when the invariant does break rather
 * than an appeal to it holding. A `!` would put nothing on the wire, the server would refuse the
 * whole request with `ado.rowStateRequired`, and every other row in it would be lost with the one
 * that was wrong. Dropping keeps the rest of the request valid and the guard honest.
 *
 * `!= null` rather than `!== undefined`, and that is load-bearing rather than sloppy: the host
 * serialises a null instead of omitting the property, which ReviewEndpointsTests asserts on the raw
 * body - `"lastCompletedAt":null` in Wire_format_uses_the_names_the_contract_declares - and
 * Todo.Contracts sets no DefaultIgnoreCondition. (TaskEndpointsTests says the same thing in a
 * comment about `"externalUrl":null`, but its assertion only looks for a value that <em>is</em>
 * there, so it would pass either way; the review one is the measured leg to stand on.)
 * The generated client assigns straight across in `init`, so an absent state
 * reaches this predicate as `null`, not `undefined`, even though the generated type only admits
 * `string | undefined`. Strict equality would therefore answer true for exactly the row this exists
 * to catch, and the narrowing below would be a lie. Do not tidy it back.
 */
type AnsweredAdoRow = AdoPreviewRow & { state: string; workItemType: string };

const wasAnsweredFor = (row: AdoPreviewRow): row is AnsweredAdoRow =>
  row.state != null && row.workItemType != null;

/**
 * A closable row that names why it is closable. The contract keeps `closureReason` nullable because
 * it is meaningless on a row that suggests nothing, so the compiler demands an answer here even
 * though a closing row always carries one.
 *
 * Measured rather than assumed, because this predicate replaced a `?? ClosureReason.Resolved`
 * fallback that would have hidden the very fault the field exists to prevent - an unassigned closure
 * silently relabelled as resolved, skipped by the server's done-list guard, and counted in `skipped`.
 * Every preview row the app can receive is built at one of four sites, two per source, and at all
 * four the two fields come off one expression: on the fetch path
 * `SuggestsClosing = suggestsClosing` beside `ClosureReason = suggestsClosing ? Resolved : null`, and
 * on the reconciliation path `SuggestsClosing = reason is not null` beside `ClosureReason = reason`.
 * So a reason-less closing row is unreachable from the server.
 *
 * It is reachable from a *fixture*, which is a different thing and the reason this drops rather than
 * throws: a row with nothing to say about itself is left out, the rest of the request stays valid,
 * and the server never sees a decision the client invented.
 *
 * The `!= null` is <em>unmeasured</em>, and it does not inherit `wasAnsweredFor`'s argument however
 * much it looks like it should. That one is earned: the host really does send `"state": null`, and
 * ReviewEndpointsTests pins the shape. This one is not. The rows this predicate was written to catch
 * are the five hand-written fixtures that <em>omitted</em> the field, so they arrive as `undefined`
 * and strict equality would drop them just as well - measured by writing `!== undefined` here, which
 * leaves all 358 Vitest green. The only input the two spellings disagree on is an explicit
 * `"closureReason": null` beside `"suggestsClosing": true`, which is exactly the body the paragraph
 * above argues the server cannot produce.
 *
 * So it is kept as the safer of two spellings rather than as a measured choice, and that distinction
 * is the point: this is the one file whose whole subject is a fallback that hid a fault through two
 * slices, and it must not lean on a leg it has not stood on.
 *
 * One asymmetry worth knowing before touching either end: for the very same unreachable state, the
 * template picks the opposite way out. `ado-import.html`'s label chain shows the <em>resolved</em>
 * sentence for a reason-less closing row, while this drops it from the wire - so the button would
 * promise "Luk 2 opgaver" while the request carried one. That `closable()` and the wire path differ
 * at all is right and has precedent in `wasAnsweredFor`; what is worth a second thought is that they
 * differ in <em>direction</em>. Tightening it belongs where the fixtures gain the field and the
 * branch loses its reason for existing, not here.
 */
type ExplainedAdoRow = AnsweredAdoRow & { closureReason: ClosureReason };

const hasReason = (row: AnsweredAdoRow): row is ExplainedAdoRow => row.closureReason != null;

@Injectable({ providedIn: 'root' })
export class AdoStore {
  private readonly client = inject(AdoClient);
  private readonly transloco = inject(TranslocoService);

  readonly rows = signal<AdoPreviewRow[]>([]);

  /**
   * What Azure DevOps reported as the total, so a truncated page is visible.
   *
   * The source's number for the <em>fetch</em>, and it has never counted a reconciled row - those
   * are keys the app went looking for after the fetch had answered. So this is not the length of
   * `rows`, and the screen's "showing N of total" has to count the un-reconciled ones for N.
   */
  readonly total = signal(0);

  /**
   * Why the reconciliation had nothing to say, as the error code the wire carries, or null in the
   * ordinary case. Two things reach it: `ado.noneRecognized` for a set of keys the source knew none
   * of, and a plain refusal code for a reconciliation that could not be carried out at all.
   *
   * The code rather than a sentence, so this is the pair a row's `excluded` makes with
   * `excludedReason` - the field holds what the server said, and the method below turns it into
   * Danish. It is deliberately not folded into `error`: a notice means the reconciliation was
   * skipped, not that the preview failed, and the fetch's own rows are on screen and worth keeping.
   */
  readonly reconciliationNotice = signal<string | null>(null);

  /**
   * The state names to pick from. Called states rather than statuses because that is Azure DevOps'
   * own word, and slice 12 measured that the two systems do not even agree on the name: the same
   * meaning is `Active` on a Bug and `In Progress` on a Test Suite.
   */
  readonly states = signal<string[]>([]);

  /** The name Azure DevOps reports for the token's owner, or null until the connection was tested. */
  readonly connection = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  /**
   * True while a call is running, so every button that starts one can be disabled. The same
   * reasoning as JiraStore's, and it was checked against this store rather than copied: TaskStore
   * needs a sequence counter because two loads really can be in flight at once - flipping both list
   * switches does it - while nothing here can start a second call before the first returns, because
   * the only buttons that would are disabled on this signal. Undo the disabling and the counter
   * stops being unnecessary.
   */
  readonly busy = signal(false);

  /**
   * The rows import will actually write. An excluded row stays in `rows` so the screen can say why
   * it is being skipped; only the selectable ones are offered.
   */
  readonly selectable = computed(() =>
    this.rows().filter((row) => !row.excluded && !row.alreadyImported),
  );

  /**
   * The rows whose work is finished in the source while the local task is not. The server decides
   * that - all three facts it rests on live there - so this is a filter on one boolean rather than a
   * rule of its own. They are deliberately not part of `selectable`: those get written as new tasks,
   * these close ones that already exist, and the import request keeps them in separate lists.
   */
  readonly closable = computed(() => this.rows().filter((row) => row.suggestsClosing));

  /**
   * The rows finished here while the source is not done with them. The mirror image of `closable`,
   * decided on the server for the same reason, and a third set rather than a flag on the second:
   * the two do opposite things to the same field, and one list would need a discriminator to tell
   * them apart.
   *
   * Disjoint from the other two by construction, which is what lets the screen add the three sizes
   * up: a reopening row was imported before, so it is never `selectable`, and the server never sets
   * both suggestions on one row.
   */
  readonly reopenable = computed(() => this.rows().filter((row) => row.suggestsReopening));

  /**
   * The reconciliation notice as a sentence, through `errors.&lt;code&gt;` - the very path
   * `excludedReason` and `apiErrorMessage` take, so the notice gets no mechanism of its own.
   *
   * A method rather than a `computed`, and that is measured rather than stylistic: `translate`
   * reads the active language, which is not a signal, and `SettingsStore.choose` calls
   * `setActiveLang` without reloading the app. A computed would cache the Danish sentence and keep
   * showing it after a switch to English. `excludedReason` is a method for the same reason.
   */
  reconciliationMessage(): string | null {
    const code = this.reconciliationNotice();
    return code === null ? null : this.transloco.translate(`errors.${code}`);
  }

  async testConnection(): Promise<void> {
    this.error.set(null);
    this.connection.set(null);
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.client.testAdoConnection());
      this.connection.set(response.displayName);
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.busy.set(false);
    }
  }

  async loadStates(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.client.listAdoStates());
      this.states.set(response.names);
    } catch (error) {
      this.states.set([]);
      this.error.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.busy.set(false);
    }
  }

  async preview(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.client.previewAdo());
      this.rows.set(response.rows);
      this.total.set(response.total);
      // Null in the ordinary case, and written every time rather than only when set: a notice left
      // over from the previous preview would explain a reconciliation that has since succeeded.
      this.reconciliationNotice.set(response.reconciliationNotice ?? null);
    } catch (error) {
      // The old rows would still be selectable, and importing them would write what a failed
      // preview no longer stands behind.
      this.rows.set([]);
      this.total.set(0);
      this.reconciliationNotice.set(null);
      this.error.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Takes rows rather than keys, because the server writes the task from what is on the wire and
   * there is nowhere else for the title, note and requester to come from.
   *
   * Two fields of the preview row are deliberately left behind, and they are left behind for the
   * same reason: a fact can be sent, a decision cannot. `isWaiting` is the server's answer to
   * whether the state is in the user's waiting list, which is a setting that lives on the server;
   * `deadline` is the server's arithmetic on its own clock, so `AdoImportRow` has no field for it
   * at all and the import derives it again. `state` and `workItemType` do go, because they are the
   * facts those two decisions are taken from.
   */
  async import(
    rows: readonly AdoPreviewRow[],
    closures: readonly AdoPreviewRow[] = [],
    reopenings: readonly AdoPreviewRow[] = [],
  ): Promise<AdoImportResponse | undefined> {
    if (rows.length === 0 && closures.length === 0 && reopenings.length === 0) {
      return undefined;
    }

    this.error.set(null);
    this.busy.set(true);
    try {
      return await firstValueFrom(
        this.client.importAdo(
          new AdoImportRequest({
            rows: rows.filter(wasAnsweredFor).map(
              (row) =>
                new AdoImportRow({
                  key: row.key,
                  title: row.title,
                  note: row.note,
                  requester: row.requester,
                  state: row.state,
                  workItemType: row.workItemType,
                  waitingSince: row.waitingSince,
                }),
            ),
            // The state travels for the same reason it does on an import row: the server looks it
            // up in the done list and takes the decision again. doneAt is the fact the client saw -
            // the import deliberately does not call Azure DevOps, so it cannot look it up.
            // Omitted when there is nothing to close, so an import-only request is
            // byte for byte the one this client has always sent - which is what the
            // contract means by calling the field optional.
            closures:
              closures.length === 0
                ? undefined
                : closures
                    .filter(wasAnsweredFor)
                    .filter(hasReason)
                    .map(
                      (row) =>
                        new AdoClosureRow({
                          key: row.key,
                          state: row.state,
                          // The reason travels because the server cannot recompute it: its first
                          // guard asks whether the state stands in the user's done list, and being
                          // handed to somebody else is orthogonal to being finished - so a server
                          // that defaulted to resolved would skip every unassigned closure and count
                          // it in `skipped`. Do not write the same default on the receiving side.
                          //
                          // Nor on this one, which is what `hasReason` above is for. There was a
                          // `?? ClosureReason.Resolved` here until the reason became something the
                          // server always sets; it masked the same fault from the sending end, where
                          // an unassigned row that lost its reason turned silently into a resolved
                          // one. Note what the contract does and does not enforce: `reason` being
                          // required puts the compiler on the argument being *present*, never on its
                          // being right.
                          reason: row.closureReason,
                          doneAt: row.doneAt,
                        }),
                    ),
            // The state travels for the third time and for the third reason: here the server maps
            // it to a status again rather than letting the client name the one it wants, which is
            // what keeps the suggestion and the import from disagreeing. So `reopenTo` deliberately
            // does not go - it is the server's own decision, and it can take it again from facts it
            // already has. Omitted when empty, for the reason the closures are.
            reopenings:
              reopenings.length === 0
                ? undefined
                : reopenings.filter(wasAnsweredFor).map(
                    (row) =>
                      new AdoReopeningRow({
                        key: row.key,
                        state: row.state,
                        waitingSince: row.waitingSince,
                      }),
                  ),
          }),
        ),
      );
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
      return undefined;
    } finally {
      this.busy.set(false);
    }
  }
}
