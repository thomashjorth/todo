import { Component, ElementRef, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AiStore } from '../ai/ai-store';
import { TodoStatus, TodoSubTask, TodoTask } from '../api/todo-client';
import { renderMarkdown } from '../markdown/render-markdown';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { SystemStore } from '../system/system-store';
import { TaskChanges, TaskStore } from './task-store';

const statusOptions: readonly TodoStatus[] = [
  TodoStatus.Open,
  TodoStatus.InProgress,
  TodoStatus.WaitingFor,
  TodoStatus.Someday,
  TodoStatus.Done,
];

/**
 * Everything about one task that is not the row: the dates, the requester, the note, the status, the
 * subtasks and the delete button.
 *
 * Its own component rather than a block inside `TaskRow`, because it renders in one of two places -
 * inside the row in one column, in the right-hand column side by side - and a `<ng-template>` shared
 * between the two would have an `any` context that `strictTemplates` cannot check.
 *
 * The opaque background is load-bearing, not decoration: it stops the contrast guard's walk up the
 * ancestors, so the text in here is measured against the colour it actually sits on.
 */
@Component({
  selector: 'app-task-detail',
  imports: [Shortcut, TranslocoPipe],
  templateUrl: './task-detail.html',
  host: {
    'data-testid': 'task-detail',
    class: 'block space-y-2 rounded bg-gray-50 p-2 dark:bg-gray-800',
  },
})
export class TaskDetail {
  readonly task = input.required<TodoTask>();
  readonly editingNote = input(false);

  readonly noteEditStarted = output<void>();
  readonly noteEditStopped = output<void>();
  readonly removed = output<void>();

  protected readonly system = inject(SystemStore);
  protected readonly shortcuts = inject(ShortcutStore);
  protected readonly ai = inject(AiStore);
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly statusOptions = statusOptions;

  private readonly store = inject(TaskStore);
  private readonly noteEditor = viewChild<ElementRef<HTMLTextAreaElement>>('note');
  private readonly whoField = viewChild<ElementRef<HTMLInputElement>>('waitingOn');

  // Rendered only while their suggestion is on screen, so their arrival is what the effects react to.
  private readonly acceptSplit = viewChild<ElementRef<HTMLButtonElement>>('acceptSplit');
  private readonly discardSplit = viewChild<ElementRef<HTMLButtonElement>>('discardSplit');
  private readonly acceptSummaryButton = viewChild<ElementRef<HTMLButtonElement>>('summaryAccept');

  constructor() {
    // Without this the click that opened the editor leaves the caret outside it, and the user
    // has to click once more before they can type.
    effect(() => this.noteEditor()?.nativeElement.focus());

    // An AI suggestion takes the focus when it arrives, on the button Enter should press: the
    // shortcut asked, so the hands are on the keyboard. With no subtasks to add, "Kassér" is all
    // there is to press.
    effect(() => (this.acceptSplit() ?? this.discardSplit())?.nativeElement.focus());
    effect(() => this.acceptSummaryButton()?.nativeElement.focus());

    // Delegating is a shortcut for "Venter på" plus a name: the picker asks, the field answers.
    // The field is read FIRST, so the effect tracks it and runs again when it turns up - and it
    // turns up later than the pick does: the @if hangs on the task's status, which only changes
    // once the PUT has answered and the list has been reloaded. Measured: right after (change)
    // the field is not in the DOM.
    // The condition is not decoration: without it an ordinary expand of a waiting row would tear
    // the focus off the row button too. Measured - an unconditional focus() passes every other
    // test.
    // focus() alone, not click(): a field has no activation action, and the focus ring is the
    // whole point. The same convention as the Alt shortcuts in slice 8.
    effect(() => {
      const field = this.whoField();
      if (!field || this.store.askingWho() !== this.task().id) {
        return;
      }

      this.store.askingWho.set(null);
      field.nativeElement.focus();
    });
  }

  protected statusKey(status: TodoStatus): string {
    return `tasks.statuses.${status}`;
  }

  protected rendered(task: TodoTask): string {
    return renderMarkdown(task.note);
  }

  protected save(changes: TaskChanges): void {
    this.store.update(this.task(), changes).catch(() => {});
  }

  /**
   * The one field the server can refuse, so the one that cannot go through `save`: `text` answers
   * `undefined` on empty, and `update`'s spread lets an explicit `undefined` clear the field, so the
   * server would get `title: undefined`, refuse with 400, and the swallowed catch would hide it -
   * leaving the field blank while the row kept its title.
   *
   * One rule rather than a branch per case: the field always shows what the task holds. Empty rolls
   * the old title back, and surrounding whitespace shows the trimmed form the server gets.
   *
   * The write to `field.value` is load-bearing and is the `[checked]` trap one field over: rolling
   * back does not change the signal, so the `[value]` binding has nothing to do and the field would
   * stay empty. `update` compares against the current task itself, so Enter followed by blur does
   * not save twice.
   */
  protected saveTitle(field: HTMLInputElement): void {
    const trimmed = field.value.trim();
    field.value = trimmed || this.task().title;

    if (!trimmed) {
      return;
    }

    this.save({ title: trimmed });
  }

  protected saveStatus(status: string): void {
    const next = status as TodoStatus;
    const id = this.task().id;
    // Only the move itself asks who, and only when it is a move: re-picking the status the task
    // already has changes nothing, so no field would ever appear to take the focus.
    const asking = next === this.waitingFor && this.task().status !== this.waitingFor;

    this.store
      .update(this.task(), { status: next })
      // Recorded after the round trip, because the field is rendered from the reloaded task. A
      // failed save records nothing, so a later expand does not steal the focus for nothing.
      .then(() => {
        if (asking) {
          this.store.askingWho.set(id);
        }
      })
      .catch(() => {});
  }

  protected stopEditingNote(note: string): void {
    this.save({ note: this.text(note) });
    this.noteEditStopped.emit();
  }

  protected clickNote(event: MouseEvent): void {
    const link = (event.target as HTMLElement).closest('a');

    if (link) {
      // Following the link instead would replace the whole app: the window has no way back.
      event.preventDefault();
      this.system.openLink(link.href).catch(() => {});
      return;
    }

    this.noteEditStarted.emit();
  }

  // The parameter is not called `input`: the name is taken by input() from @angular/core, and the
  // shadowing would only bite the day this method comes to need it.
  protected createSubTask(field: HTMLInputElement): void {
    const title = field.value;
    if (!title.trim()) {
      return;
    }

    field.value = '';
    this.store.addSubTask(this.task().id, title).catch(() => {});
  }

  protected split(): void {
    this.ai.split(this.task().id).catch(() => {});
  }

  protected acceptSubTasks(): void {
    this.ai.acceptSubTasks().catch(() => {});
  }

  protected summarize(): void {
    this.ai.summarize(this.task().id).catch(() => {});
  }

  protected acceptSummary(): void {
    this.ai.acceptSummary(this.task()).catch(() => {});
  }

  protected setSubTaskDone(subTask: TodoSubTask, isDone: boolean): void {
    this.store.setSubTaskDone(this.task().id, subTask, isDone).catch(() => {});
  }

  protected removeSubTask(subTask: TodoSubTask): void {
    this.store.removeSubTask(this.task().id, subTask.id).catch(() => {});
  }

  protected text(value: string): string | undefined {
    return value.trim() || undefined;
  }
}
