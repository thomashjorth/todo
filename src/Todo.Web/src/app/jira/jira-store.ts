import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
  ClosureReason,
  JiraClient,
  JiraClosureRow,
  JiraImportRequest,
  JiraImportResponse,
  JiraImportRow,
  JiraPreviewRow,
  JiraReopeningRow,
} from '../api/todo-client';

/**
 * A preview row the source actually answered about, so its status is there to send on. The
 * counterpart of AdoStore's guard, and it asks about one field rather than two only because a Jira
 * row has one such field - see that one for why this is a filter rather than a `!`, and for why the
 * comparison is `!= null` rather than `!== undefined`: an absent status arrives as a serialised
 * null, so strict equality would pass the very row this rejects.
 */
type AnsweredJiraRow = JiraPreviewRow & { status: string };

const wasAnsweredFor = (row: JiraPreviewRow): row is AnsweredJiraRow => row.status != null;

@Injectable({ providedIn: 'root' })
export class JiraStore {
  private readonly client = inject(JiraClient);
  private readonly transloco = inject(TranslocoService);

  readonly rows = signal<JiraPreviewRow[]>([]);

  /**
   * What Jira reported as the total, so a truncated page is visible. The source's number for the
   * <em>fetch</em>, which has never counted a reconciled row - see AdoStore's twin for why that
   * makes it the wrong number to pair with the length of `rows`.
   */
  readonly total = signal(0);
  readonly statuses = signal<string[]>([]);

  /**
   * Why the reconciliation had nothing to say, as the error code the wire carries, or null in the
   * ordinary case. See AdoStore's twin for why it is a code rather than a sentence, and why it is
   * deliberately not folded into `error`.
   */
  readonly reconciliationNotice = signal<string | null>(null);

  /** The name Jira reports for the token's owner, or null until the connection was tested. */
  readonly connection = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  /**
   * True while a call is running, so every button that starts one can be disabled. That is not
   * decoration: it is what makes the missing sequence counter safe. TaskStore.load needs one
   * because two loads really can be in flight at once — flipping both list switches quickly does
   * it — and an older answer landing last would wipe the newer list. Here nothing in the UI can
   * start a second call before the first returns, because the button that would start it is
   * disabled. Remove the disabling and the counter stops being unnecessary.
   */
  readonly busy = signal(false);

  /**
   * The rows import will actually write. An excluded row stays in `rows` so the screen can say
   * why it is being skipped; only the selectable ones are offered.
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
   * The rows finished here while Jira is not done with them - the mirror image of `closable`, and a
   * third set rather than a flag on the second. See AdoStore's twin for why the three are disjoint
   * by construction, which is what lets the screen add their sizes up.
   */
  readonly reopenable = computed(() => this.rows().filter((row) => row.suggestsReopening));

  /**
   * The reconciliation notice as a sentence, through `errors.&lt;code&gt;` - the path
   * `excludedReason` and `apiErrorMessage` take. A method rather than a `computed` because
   * `translate` reads the active language, which is not a signal; see AdoStore's twin.
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
      const response = await firstValueFrom(this.client.testJiraConnection());
      this.connection.set(response.displayName);
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.busy.set(false);
    }
  }

  async loadStatuses(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.client.listJiraStatuses());
      this.statuses.set(response.names);
    } catch (error) {
      this.statuses.set([]);
      this.error.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.busy.set(false);
    }
  }

  async preview(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.client.previewJira());
      this.rows.set(response.rows);
      this.total.set(response.total);
      // Written every time rather than only when set: a notice left over from the previous preview
      // would explain a reconciliation that has since succeeded.
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
   * there is nowhere else for the title, note, deadline and requester to come from. `isWaiting` is
   * deliberately not sent: the server decides that by looking the status up in the user's waiting
   * list, which is a setting that lives on the server.
   */
  async import(
    rows: readonly JiraPreviewRow[],
    closures: readonly JiraPreviewRow[] = [],
    reopenings: readonly JiraPreviewRow[] = [],
  ): Promise<JiraImportResponse | undefined> {
    if (rows.length === 0 && closures.length === 0 && reopenings.length === 0) {
      return undefined;
    }

    this.error.set(null);
    this.busy.set(true);
    try {
      return await firstValueFrom(
        this.client.importJira(
          new JiraImportRequest({
            rows: rows.filter(wasAnsweredFor).map(
              (row) =>
                new JiraImportRow({
                  key: row.key,
                  title: row.title,
                  note: row.note,
                  deadline: row.deadline,
                  requester: row.requester,
                  status: row.status,
                  waitingSince: row.waitingSince,
                }),
            ),
            // The status travels for the same reason it does on an import row: the server looks it
            // up in the done list and takes the decision again. doneAt is the fact the client saw.
            // Omitted when there is nothing to close, so an import-only request is
            // byte for byte the one this client has always sent - which is what the
            // contract means by calling the field optional.
            closures:
              closures.length === 0
                ? undefined
                : closures.filter(wasAnsweredFor).map(
                    (row) =>
                      new JiraClosureRow({
                        key: row.key,
                        status: row.status,
                        // See AdoStore for why an absent reason reads as resolved, why the same
                        // fallback must not be written on the receiving side, and why task 6 is
                        // where this line becomes a hard failure or goes away.
                        //
                        // This branch used to be unmeasured while the ADO twin was measured, and
                        // the note saying so outlived the hole: e01c6f6 gave jira-store.spec.ts
                        // `should drop a closable row whose status the source never answered with`,
                        // which asserts on this body. Both stores now measure it, which is the
                        // point - they are twins, so a divergence in either reads as an adaptation.
                        reason: row.closureReason ?? ClosureReason.Resolved,
                        doneAt: row.doneAt,
                      }),
                  ),
            // The status travels so the server maps it to a status again rather than letting the
            // client name the one it wants, which is what keeps the suggestion and the import from
            // disagreeing. `reopenTo` deliberately does not go - see AdoStore's twin.
            reopenings:
              reopenings.length === 0
                ? undefined
                : reopenings.filter(wasAnsweredFor).map(
                    (row) =>
                      new JiraReopeningRow({
                        key: row.key,
                        status: row.status,
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
