import { Component, ElementRef, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
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
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly statusOptions = statusOptions;

  private readonly store = inject(TaskStore);
  private readonly noteEditor = viewChild<ElementRef<HTMLTextAreaElement>>('note');
  private readonly whoField = viewChild<ElementRef<HTMLInputElement>>('waitingOn');

  constructor() {
    // Uden denne lader klikket, der åbnede editoren, caret'en stå uden for den, og brugeren
    // skal klikke en gang mere for at skrive.
    effect(() => this.noteEditor()?.nativeElement.focus());

    // Uddelegering er en genvej til Venter på + hvem: vælgeren spørger, feltet svarer.
    // Feltet læses FØRST, så effekten sporer det og kører igen når det dukker op — og det gør
    // det senere end valget: @if hænger på opgavens status, som først skifter når PUT'en er
    // svaret og listen genindlæst. Målt: lige efter (change) findes feltet ikke i DOM'en.
    // Betingelsen er ikke pynt: uden den ville en almindelig udvidelse af en ventende række også
    // rive fokus fra rækkeknappen. Målt — en ubetinget focus() består alle andre tests.
    // Kun focus(), ikke click(): et felt har ingen aktiveringshandling, og fokusringen er hele
    // pointen. Samme konvention som Alt-genvejene i skive 8.
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
      // At følge linket i stedet ville erstatte hele appen: vinduet har ingen vej tilbage.
      event.preventDefault();
      this.system.openLink(link.href).catch(() => {});
      return;
    }

    this.noteEditStarted.emit();
  }

  // Parameteren hedder ikke `input`: navnet er optaget af input() fra @angular/core, og
  // skyggen ville først bide den dag metoden får brug for den.
  protected createSubTask(field: HTMLInputElement): void {
    const title = field.value;
    if (!title.trim()) {
      return;
    }

    field.value = '';
    this.store.addSubTask(this.task().id, title).catch(() => {});
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
