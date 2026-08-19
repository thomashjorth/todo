import { Component, ElementRef, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoSubTask, TodoTask } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { renderMarkdown } from '../markdown/render-markdown';
import { SystemStore } from '../system/system-store';
import { TaskChanges, TaskStore, subTaskProgress } from './task-store';

const statusOptions: readonly TodoStatus[] = [
  TodoStatus.Open,
  TodoStatus.InProgress,
  TodoStatus.WaitingFor,
  TodoStatus.Someday,
  TodoStatus.Done,
];

@Component({
  // Attributvælger, ikke et eget element: en <app-task-row> mellem <ul> og <li> ville skubbe
  // et fremmed element ind i listen, så divide-y ikke længere rammer søskende-rækker.
  // data-testid er derfor en host-binding — TaskListScreen.Rows finder rækken på den.
  selector: 'li[appTaskRow]',
  imports: [DeadlineDate, TranslocoPipe],
  templateUrl: './task-row.html',
  host: {
    'data-testid': 'task-row',
    class: 'py-2',
  },
})
export class TaskRow {
  readonly task = input.required<TodoTask>();
  readonly expanded = input(false);
  readonly editingNote = input(false);

  readonly toggled = output<void>();
  readonly noteEditStarted = output<void>();
  readonly noteEditStopped = output<void>();
  readonly removed = output<void>();

  protected readonly system = inject(SystemStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly statusOptions = statusOptions;
  protected readonly progress = subTaskProgress;

  private readonly store = inject(TaskStore);
  private readonly noteEditor = viewChild<ElementRef<HTMLTextAreaElement>>('note');

  constructor() {
    // Uden denne lader klikket, der åbnede editoren, caret'en stå uden for den, og brugeren
    // skal klikke en gang mere for at skrive.
    effect(() => this.noteEditor()?.nativeElement.focus());
  }

  protected statusKey(status: TodoStatus): string {
    return `tasks.statuses.${status}`;
  }

  protected waitingDaysKey(days: number): string {
    return pluralKey(days, 'tasks.waitingDays');
  }

  protected rendered(task: TodoTask): string {
    return renderMarkdown(task.note);
  }

  protected save(changes: TaskChanges): void {
    this.store.update(this.task(), changes).catch(() => {});
  }

  protected saveStatus(status: string): void {
    this.save({ status: status as TodoStatus });
  }

  protected setDone(isDone: boolean): void {
    this.save({ status: isDone ? TodoStatus.Done : TodoStatus.Open });
  }

  protected stopEditingNote(note: string): void {
    this.save({ note: this.text(note) });
    this.noteEditStopped.emit();
  }

  /** Same one-way street as a note's link: the window has no address bar to come back from. */
  protected openIssue(url: string): void {
    this.system.openLink(url).catch(() => {});
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
