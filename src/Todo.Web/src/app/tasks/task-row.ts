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
  // Attributvælger, ikke et eget element: en <app-task-row> mellem <ul> og <li> ville skubbe
  // et fremmed element ind i listen, så divide-y ikke længere rammer søskende-rækker.
  // data-testid er derfor en host-binding — TaskListScreen.Rows finder rækken på den.
  selector: 'li[appTaskRow]',
  imports: [DeadlineDate, Shortcut, TaskDetail, TranslocoPipe],
  templateUrl: './task-row.html',
  host: {
    'data-testid': 'task-row',
    class: 'py-2',
    // Navnet browseren morfer rækken efter, når den skifter sektion. En inline-style og ikke en
    // Tailwind-klasse, fordi værdien er *data*: en utility-klasse er statisk, og den arbitrære
    // egenskab `[view-transition-name:task-42]` kan ikke tage en køretidsværdi. Undtagelsen fra
    // stylingkonventionen er derfor lige så snæver som den ser ud, og godkendt 2026-08-25.
    // Præfikset er ikke pynt: en view-transition-name er en <custom-ident> og må ikke begynde
    // med et ciffer. Dækker de tre @for-løkker over li[appTaskRow]; fuldført-sektionens
    // almindelige <li> har sin egen binding i task-list.html.
    '[style.view-transition-name]': '"task-" + task().id',
    // Hænger rækkens gruppe under spaltens, så `::view-transition-group-children(task-column)` i
    // styles.css kan klippe den. Uden det maler en række med en destination uden for den rullende
    // spalte oven på health-linjen — begrundelsen og tallene står ved reglen.
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
  // Kun til mærkaten: den vises mens Alt holdes nede, som de otte andre på skærmen.
  protected readonly shortcuts = inject(ShortcutStore);

  // Direktivet tager en streng, og den tomme streng er dens "ingen genvej" - række ti og frem.
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
