import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { SystemStore } from '../system/system-store';
import { TaskDetail } from './task-detail';
import { TaskStore, subTaskProgress } from './task-store';

@Component({
  // An attribute selector rather than an element of its own: an <app-task-row> between the <ul>
  // and the <li> would push a foreign element into the list, so `divide-y` would stop hitting
  // sibling rows. That is why data-testid is a host binding - TaskListScreen.Rows finds the row
  // on it.
  selector: 'li[appTaskRow]',
  imports: [DeadlineDate, Shortcut, TaskDetail, TranslocoPipe],
  templateUrl: './task-row.html',
  host: {
    'data-testid': 'task-row',
    class: 'py-2',
    // The name the browser morphs the row by when it changes section. An inline style rather
    // than a Tailwind class, because the value is *data*: a utility class is static, and the
    // arbitrary property `[view-transition-name:task-42]` cannot take a runtime value. The
    // exception to the styling convention is therefore exactly as narrow as it looks, and was
    // approved 2026-08-25. The prefix is not decoration: a view-transition-name is a
    // <custom-ident> and must not begin with a digit. Covers the three @for loops over
    // li[appTaskRow]; the completed section's plain <li> has a binding of its own in
    // task-list.html.
    '[style.view-transition-name]': '"task-" + task().id',
    // Hangs the row's group under the column's, so `::view-transition-group-children(task-column)`
    // in styles.css can clip it. Without it a row whose destination is outside the scrolling
    // column paints over the health line - the reasoning and the numbers sit at the rule itself.
    '[style.view-transition-group]': '"task-column"',
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

  /** The row's place among the nine numbered ones, or undefined from the tenth row on. */
  readonly number = input<number | undefined>();

  readonly toggled = output<void>();
  readonly noteEditStarted = output<void>();
  readonly noteEditStopped = output<void>();
  readonly removed = output<void>();

  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly inProgress = TodoStatus.InProgress;
  protected readonly progress = subTaskProgress;
  // For the badge alone: it shows while Alt is held, like the eight others on screen.
  protected readonly shortcuts = inject(ShortcutStore);

  // The directive takes a string, and the empty string is its "no shortcut" - from row ten on.
  protected readonly shortcut = computed(() => this.number()?.toString() ?? '');

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
