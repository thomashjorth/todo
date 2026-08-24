import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { WideScreen } from '../layout/wide-screen';
import { SettingsStore } from '../settings/settings-store';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { SystemStore } from '../system/system-store';
import { TaskDetail } from './task-detail';
import { TaskRow } from './task-row';
import { TaskStore } from './task-store';

@Component({
  selector: 'app-task-list',
  imports: [Shortcut, TaskDetail, TaskRow, TranslocoPipe],
  templateUrl: './task-list.html',
  // `block`, because a custom element is inline by default and an inline box has no height to give
  // its children - and side by side the grid inside needs `h-full` to have something to be full of.
  host: {
    class: 'block xl:h-full',
  },
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly shortcuts = inject(ShortcutStore);
  protected readonly wide = inject(WideScreen);
  // Kun for den delte <datalist> nederst i skabelonen: signalet er aldrig null, og listen
  // læses her frem for i hver række, fordi der er én liste og mange rækker.
  protected readonly settings = inject(SettingsStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly selectedId = signal<number | null>(null);
  protected readonly editingNote = signal<number | null>(null);
  protected readonly completed = computed(() =>
    this.store.showCompleted() ? this.store.completedTasks() : [],
  );
  protected readonly someday = computed(() =>
    this.store.showSomeday() ? this.store.somedayTasks() : [],
  );

  /**
   * The tasks a detail panel can be opened for, in the order they are on screen.
   *
   * Completed tasks are not among them, and that is a consequence rather than a rule of its own:
   * their rows are a plain `<li>` with no panel behind them, so there is nothing to select. It also
   * means the empty state is unavoidable - with only completed tasks in view the right-hand column
   * has nothing to show - so auto-selection does not remove the need for the prompt.
   */
  protected readonly selectableTasks = computed<TodoTask[]>(() => [
    ...this.store.sections().flatMap((section) => section.tasks),
    ...this.store.waitingTasks(),
    ...this.someday(),
  ]);

  /**
   * The first nine selectable tasks, numbered as they appear on screen.
   *
   * Nine because there are nine digits worth having: Alt+0 is not a tenth row, it is a key nobody
   * would guess. The completed section has no numbers and is skipped, which is a consequence of
   * `selectableTasks` rather than a rule of its own - a completed row has no panel to select.
   */
  protected readonly numbers = computed(
    () =>
      new Map(
        this.selectableTasks()
          .slice(0, 9)
          .map((task, i) => [task.id, i + 1]),
      ),
  );

  /**
   * The task the detail panel is showing.
   *
   * A derivation rather than an effect, because the three rules it has to obey are the same rule
   * once written this way. Auto-selection on load is "no valid id, take the first". That the
   * selection follows along when the selected task is searched away, deleted, or moved to done with
   * done hidden, is the same line: the list changes, `find` fails, `[0]` answers. And that
   * auto-selection is side by side only is the `wide()` term.
   *
   * An effect would have written to a signal it reads itself, and would have to be called from
   * `load`, `remove`, `searchFor`, `setShowCompleted`, `setShowSomeday` and the status change - six
   * call sites that can drift apart. This has none.
   */
  protected readonly selected = computed<TodoTask | undefined>(() => {
    const id = this.selectedId();
    const selectable = this.selectableTasks();

    return selectable.find((t) => t.id === id) ?? (this.wide.wide() ? selectable[0] : undefined);
  });

  private readonly system = inject(SystemStore);

  constructor() {
    // Et fejlet load behøver ingen besked af sig selv: health-linjen melder allerede API'et nede.
    this.store.load().catch(() => {});
  }

  // API'ets enum-værdier er nøglens blade, så en ny bucket kræver ingen mapning her.
  protected sectionKey(bucket: DeadlineBucket): string {
    return `tasks.sections.${bucket}`;
  }

  protected create(field: HTMLInputElement): void {
    const title = field.value;
    if (!title.trim()) {
      return;
    }

    field.value = '';
    this.store.add(title).catch(() => {});
  }

  /**
   * Side by side the selection stays: the panel is a column of its own, so deselecting would leave
   * half the window empty for nothing. In one column a second click is the only way to fold the
   * panel away again, so there the click still toggles.
   */
  protected toggle(task: TodoTask): void {
    this.system.clearError();
    this.editingNote.set(null);
    this.selectedId.update((id) => (id === task.id && !this.wide.wide() ? null : task.id));
  }

  protected editNote(task: TodoTask): void {
    this.editingNote.set(task.id);
  }

  protected stopEditingNote(): void {
    this.editingNote.set(null);
  }

  protected setDone(task: TodoTask, isDone: boolean): void {
    this.store.update(task, { status: isDone ? TodoStatus.Done : TodoStatus.Open }).catch(() => {});
  }

  protected setShowCompleted(value: boolean): void {
    this.store.setShowCompleted(value).catch(() => {});
  }

  protected setShowSomeday(value: boolean): void {
    this.store.setShowSomeday(value).catch(() => {});
  }

  /**
   * No server call, so no `catch` and no await: the filter runs over the list already in memory.
   * That is also why it is bound to `input` rather than `change` - the list narrows as you type.
   *
   * Nothing clears the selected id here, and that is the point of `selected` being a derivation: if
   * the search removes the selected task, `find` fails on its own - side by side the panel moves to
   * the first task still in view, and in one column the row simply folds away, as it did when this
   * method reset the id by hand.
   */
  protected searchFor(query: string): void {
    this.editingNote.set(null);
    this.store.query.set(query);
  }

  /** Same as the search: the deleted task leaves the list, so the derivation moves the panel on. */
  protected remove(task: TodoTask): void {
    this.editingNote.set(null);
    this.store.remove(task.id).catch(() => {});
  }
}
