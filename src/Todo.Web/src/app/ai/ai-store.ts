import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { AiClient, AiParseTaskRequest, AiTaskSuggestion, TodoTask } from '../api/todo-client';
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
  private readonly transloco = inject(TranslocoService);

  /** Starts false, so a feature never flashes on and off again while the first answer is on its way. */
  readonly ready = signal(false);

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
      this.ready.set((await firstValueFrom(this.client.getAiStatus())).ready);
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

    await this.tasks.addSuggested(suggestion);
    this.taskSuggestion.set(null);
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
}
