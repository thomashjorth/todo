import { Component, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { SystemStore } from '../system/system-store';
import { TaskDetail } from './task-detail';
import { TaskStore, subTaskProgress } from './task-store';

@Component({
  // Attributvælger, ikke et eget element: en <app-task-row> mellem <ul> og <li> ville skubbe
  // et fremmed element ind i listen, så divide-y ikke længere rammer søskende-rækker.
  // data-testid er derfor en host-binding — TaskListScreen.Rows finder rækken på den.
  selector: 'li[appTaskRow]',
  imports: [DeadlineDate, TaskDetail, TranslocoPipe],
  templateUrl: './task-row.html',
  host: {
    'data-testid': 'task-row',
    class: 'py-2',
  },
})
export class TaskRow {
  readonly task = input.required<TodoTask>();

  /**
   * Whether the detail panel belongs inside this row. Only ever true in one column: side by side
   * the panel lives in its own column, and rendering it in both places would put two elements
   * carrying `data-testid="task-detail"` in the document.
   */
  readonly expanded = input(false);

  /**
   * Whether this is the row the detail panel is showing, which side by side is the only way to see
   * which task the right-hand column belongs to. False in one column, where the unfolded panel says
   * it already.
   */
  readonly selected = input(false);

  readonly editingNote = input(false);

  readonly toggled = output<void>();
  readonly noteEditStarted = output<void>();
  readonly noteEditStopped = output<void>();
  readonly removed = output<void>();

  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly inProgress = TodoStatus.InProgress;
  protected readonly progress = subTaskProgress;

  private readonly store = inject(TaskStore);
  private readonly system = inject(SystemStore);

  protected waitingDaysKey(days: number): string {
    return pluralKey(days, 'tasks.waitingDays');
  }

  protected setDone(isDone: boolean): void {
    this.store
      .update(this.task(), { status: isDone ? TodoStatus.Done : TodoStatus.Open })
      .catch(() => {});
  }

  /** Same one-way street as a note's link: the window has no address bar to come back from. */
  protected openIssue(url: string): void {
    this.system.openLink(url).catch(() => {});
  }
}
