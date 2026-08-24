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
} from '../api/todo-client';

@Injectable({ providedIn: 'root' })
export class AdoStore {
  private readonly client = inject(AdoClient);
  private readonly transloco = inject(TranslocoService);

  readonly rows = signal<AdoPreviewRow[]>([]);

  /** What Azure DevOps reported as the total, so a truncated page is visible. */
  readonly total = signal(0);

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
    } catch (error) {
      // The old rows would still be selectable, and importing them would write what a failed
      // preview no longer stands behind.
      this.rows.set([]);
      this.total.set(0);
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
  ): Promise<AdoImportResponse | undefined> {
    if (rows.length === 0 && closures.length === 0) {
      return undefined;
    }

    this.error.set(null);
    this.busy.set(true);
    try {
      return await firstValueFrom(
        this.client.importAdo(
          new AdoImportRequest({
            rows: rows.map(
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
                : closures.map(
                    (row) =>
                      new AdoClosureRow({
                        key: row.key,
                        state: row.state,
                        doneAt: row.doneAt,
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
