import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
  AiClient,
  AiDuplicateCandidate,
  AiDuplicatesRequest,
  AiParseTaskRequest,
  AiRetroActionsRequest,
  AiRetroNote,
  AiSearchRequest,
  AiTaskSuggestion,
  RetroPreviewRow,
  TodoTask,
} from '../api/todo-client';
import { TagStore } from '../tags/tag-store';
import { TaskStore } from '../tasks/task-store';

/** A suggestion or an error that belongs to one task's panel and must not show in another's. */
export interface ForTask<T> {
  taskId: number;
  value: T;
}

export interface SubTaskChoice {
  title: string;
  chosen: boolean;
}

/**
 * The local model: whether it can be asked, and what it has suggested.
 *
 * Read once, at startup (the user's choice 2026-10-22): a model that finishes downloading afterwards
 * needs a restart before the AI features appear, and an Ollama that dies mid-session keeps looking
 * ready until a call fails. See the AI foundation design.
 *
 * The suggestions live here rather than in the components for the same reason as
 * `TaskStore.askingWho`: accepting one reloads the list, and state held by a row can be destroyed
 * with it. Nothing here saves anything - accepting goes through `TaskStore`, so a model's answer
 * never changes data without a click.
 */
@Injectable({ providedIn: 'root' })
export class AiStore {
  private readonly client = inject(AiClient);
  private readonly tasks = inject(TaskStore);
  private readonly tagStore = inject(TagStore);
  private readonly transloco = inject(TranslocoService);

  /** Starts false, so a feature never flashes on and off again while the first answer is on its way. */
  readonly ready = signal(false);

  /**
   * Whether the embedding model is there too: search by meaning and duplicate warnings need it, and
   * it is installed apart from the answering model, so one can be ready while the other downloads.
   */
  readonly searchReady = signal(false);

  readonly taskSuggestion = signal<AiTaskSuggestion | null>(null);
  readonly parsing = signal(false);
  readonly parseError = signal<string | null>(null);

  readonly subTaskSuggestions = signal<ForTask<SubTaskChoice[]> | null>(null);
  readonly splitting = signal<number | null>(null);
  readonly splitError = signal<ForTask<string> | null>(null);

  readonly summary = signal<ForTask<string> | null>(null);
  readonly summarizing = signal<number | null>(null);
  readonly summaryError = signal<ForTask<string> | null>(null);

  async load(): Promise<void> {
    try {
      const status = await firstValueFrom(this.client.getAiStatus());
      this.ready.set(status.ready);
      this.searchReady.set(status.searchReady);
    } catch {
      // The AI features are an addition, not the app. A status that cannot be read hides them and
      // says nothing.
      return;
    }

    if (this.ready()) {
      // Not awaited: loading the model took about 13 s on the user's CPU, and that is the wait the
      // first click would otherwise pay. The host never fails it, and nothing here waits on it.
      firstValueFrom(this.client.warmAi()).catch(() => {});
    }
  }

  async parse(text: string): Promise<void> {
    this.parsing.set(true);
    this.parseError.set(null);
    this.taskSuggestion.set(null);
    try {
      this.taskSuggestion.set(
        await firstValueFrom(this.client.parseTask(new AiParseTaskRequest({ text }))),
      );
    } catch (error) {
      this.parseError.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.parsing.set(false);
    }
  }

  async acceptTask(): Promise<void> {
    const suggestion = this.taskSuggestion();
    if (!suggestion) {
      return;
    }

    const created = await this.tasks.addSuggested(suggestion);
    this.taskSuggestion.set(null);
    void this.suggestTags(created);
  }

  discardTask(): void {
    this.taskSuggestion.set(null);
    this.parseError.set(null);
  }

  async split(taskId: number): Promise<void> {
    this.splitting.set(taskId);
    this.splitError.set(null);
    this.subTaskSuggestions.set(null);
    try {
      const { titles } = await firstValueFrom(this.client.suggestSubTasks(taskId));
      // All ticked: the user asked for subtasks, so the expected answer to each is yes.
      this.subTaskSuggestions.set({
        taskId,
        value: titles.map((title) => ({ title, chosen: true })),
      });
    } catch (error) {
      this.splitError.set({ taskId, value: apiErrorMessage(this.transloco, error) });
    } finally {
      this.splitting.set(null);
    }
  }

  choose(index: number, chosen: boolean): void {
    this.subTaskSuggestions.update((current) =>
      current
        ? {
            ...current,
            value: current.value.map((c, i) => (i === index ? { ...c, chosen } : c)),
          }
        : current,
    );
  }

  async acceptSubTasks(): Promise<void> {
    const current = this.subTaskSuggestions();
    if (!current) {
      return;
    }

    const chosen = current.value.filter((c) => c.chosen).map((c) => c.title);
    if (chosen.length > 0) {
      await this.tasks.addSubTasks(current.taskId, chosen);
    }
    this.subTaskSuggestions.set(null);
  }

  discardSubTasks(): void {
    this.subTaskSuggestions.set(null);
    this.splitError.set(null);
  }

  async summarize(taskId: number): Promise<void> {
    this.summarizing.set(taskId);
    this.summaryError.set(null);
    this.summary.set(null);
    try {
      const { summary } = await firstValueFrom(this.client.summarizeNote(taskId));
      this.summary.set({ taskId, value: summary });
    } catch (error) {
      this.summaryError.set({ taskId, value: apiErrorMessage(this.transloco, error) });
    } finally {
      this.summarizing.set(null);
    }
  }

  /** Replaces the note. Only offered on an imported task, where the full text is behind the link. */
  async acceptSummary(task: TodoTask): Promise<void> {
    const current = this.summary();
    if (current?.taskId !== task.id) {
      return;
    }

    await this.tasks.update(task, { note: current.value });
    this.summary.set(null);
  }

  discardSummary(): void {
    this.summary.set(null);
    this.summaryError.set(null);
  }

  // --- Tags for a task just created ---

  /** One at a time: a newer task's suggestion replaces an older one nobody answered. */
  readonly tagSuggestion = signal<{
    taskId: number;
    title: string;
    tagIds: readonly number[];
  } | null>(null);

  /** The suggested tags' names, in the user's own order, for the line under the new-task field. */
  readonly tagSuggestionNames = computed(() => {
    const suggested = new Set(this.tagSuggestion()?.tagIds ?? []);
    return this.tagStore
      .tags()
      .filter((tag) => suggested.has(tag.id))
      .map((tag) => tag.name);
  });

  /**
   * Asked in the background right after a task is created, so the creation never waits on it - the
   * user's own word for it was "uden at blokere". Nothing is asked without tags to choose from, and
   * a failure or an empty answer shows nothing: a suggestion nobody asked for is not worth an error.
   */
  async suggestTags(task: TodoTask): Promise<void> {
    if (!this.ready() || this.tagStore.tags().length === 0) {
      return;
    }

    try {
      const { tagIds } = await firstValueFrom(this.client.suggestTags(task.id));
      if (tagIds.length > 0) {
        this.tagSuggestion.set({ taskId: task.id, title: task.title, tagIds });
      }
    } catch {
      // See above: silence is the answer to a suggestion that could not be made.
    }
  }

  /** Adds the suggested tags to whatever the task carries by now, rather than replacing them. */
  async acceptTags(): Promise<void> {
    const suggestion = this.tagSuggestion();
    if (!suggestion) {
      return;
    }

    const carried = this.tasks.tasks().find((t) => t.id === suggestion.taskId)?.tags ?? [];
    const ids = new Set([...carried.map((t) => t.id), ...suggestion.tagIds]);
    this.tagSuggestion.set(null);
    await this.tasks.setTags(suggestion.taskId, [...ids]);
  }

  dismissTags(): void {
    this.tagSuggestion.set(null);
  }

  // --- The retro import ---

  /** One suggestion per row key, and whether the row uses it. Cleared by a new analysis. */
  readonly retroActions = signal<ReadonlyMap<string, RetroSuggestion>>(new Map());
  readonly suggestingRetro = signal(false);
  readonly retroError = signal<string | null>(null);

  async suggestRetroActions(rows: readonly RetroPreviewRow[]): Promise<void> {
    this.suggestingRetro.set(true);
    this.retroError.set(null);
    try {
      const answer = await firstValueFrom(
        this.client.suggestRetroActions(
          new AiRetroActionsRequest({
            rows: rows.map(
              (row) =>
                new AiRetroNote({ key: row.key, text: row.title, owner: row.owner ?? undefined }),
            ),
          }),
        ),
      );
      // Used unless switched back: the user asked for the suggestions, and the original is one
      // click away. Merged rather than replaced, so asking again for other rows keeps the first.
      this.retroActions.update((current) => {
        const next = new Map(current);
        for (const action of answer.rows) {
          next.set(action.key, {
            title: action.title,
            owner: action.owner ?? null,
            ownerIsMine: action.ownerIsMine,
            used: true,
          });
        }
        return next;
      });
    } catch (error) {
      this.retroError.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.suggestingRetro.set(false);
    }
  }

  useRetroAction(key: string, used: boolean): void {
    this.retroActions.update((current) => {
      const suggestion = current.get(key);
      if (!suggestion) {
        return current;
      }
      const next = new Map(current);
      next.set(key, { ...suggestion, used });
      return next;
    });
  }

  clearRetroActions(): void {
    this.retroActions.set(new Map());
    this.retroError.set(null);
  }

  /**
   * The row as it will be imported: the suggested title when it is used, and the guessed owner only
   * where the board names none - the same rule the host applied before it answered.
   */
  asImported(row: RetroPreviewRow): RetroPreviewRow {
    const suggestion = this.retroActions().get(row.key);
    if (!suggestion?.used) {
      return row;
    }

    return new RetroPreviewRow({
      ...row,
      title: suggestion.title,
      owner: row.owner ?? suggestion.owner ?? undefined,
    });
  }

  // --- Search by meaning ---

  readonly searchingByMeaning = signal(false);
  readonly searchError = signal<string | null>(null);

  /** Hands the ids to the task list, which adds them to what the words already matched. */
  async searchByMeaning(query: string): Promise<void> {
    const trimmed = query.trim();
    if (!trimmed) {
      return;
    }

    this.searchingByMeaning.set(true);
    this.searchError.set(null);
    try {
      const { taskIds } = await firstValueFrom(
        this.client.searchByMeaning(new AiSearchRequest({ query: trimmed })),
      );
      this.tasks.meaning.set({ query: trimmed.toLowerCase(), ids: new Set(taskIds) });
    } catch (error) {
      this.searchError.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.searchingByMeaning.set(false);
    }
  }

  // --- Duplicates in the import previews ---

  /** Per source, the task each row key resembles. A warning, so a failed check shows nothing. */
  readonly duplicates = signal<ReadonlyMap<string, ReadonlyMap<string, Duplicate>>>(new Map());

  async findDuplicates(
    sourceId: string,
    rows: readonly { key: string; title: string; note?: string | null }[],
  ): Promise<void> {
    if (!this.searchReady() || rows.length === 0) {
      return;
    }

    try {
      const { matches } = await firstValueFrom(
        this.client.findDuplicates(
          new AiDuplicatesRequest({
            sourceId,
            rows: rows.map(
              (row) =>
                new AiDuplicateCandidate({
                  key: row.key,
                  title: row.title,
                  note: row.note ?? undefined,
                }),
            ),
          }),
        ),
      );
      this.duplicates.update((current) => {
        const next = new Map(current);
        next.set(
          sourceId,
          new Map(matches.map((m) => [m.key, { taskId: m.taskId, taskTitle: m.taskTitle }])),
        );
        return next;
      });
    } catch {
      // A warning that could not be computed is no warning; the import works as before.
    }
  }

  duplicateOf(sourceId: string, key: string): Duplicate | undefined {
    return this.duplicates().get(sourceId)?.get(key);
  }

  // --- The weekly review ---

  readonly reviewPoints = signal<readonly string[] | null>(null);
  readonly writingReview = signal(false);
  readonly reviewError = signal<string | null>(null);

  async writeReviewOverview(): Promise<void> {
    this.writingReview.set(true);
    this.reviewError.set(null);
    try {
      this.reviewPoints.set((await firstValueFrom(this.client.writeReviewOverview())).points);
    } catch (error) {
      this.reviewError.set(apiErrorMessage(this.transloco, error));
    } finally {
      this.writingReview.set(false);
    }
  }
}

export interface RetroSuggestion {
  title: string;
  owner: string | null;
  ownerIsMine: boolean;
  used: boolean;
}

export interface Duplicate {
  taskId: number;
  taskTitle: string;
}
