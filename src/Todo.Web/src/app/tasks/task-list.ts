import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { SettingsStore } from '../settings/settings-store';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { SystemStore } from '../system/system-store';
import { TaskRow } from './task-row';
import { TaskStore } from './task-store';

@Component({
  selector: 'app-task-list',
  imports: [Shortcut, TaskRow, TranslocoPipe],
  templateUrl: './task-list.html',
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly shortcuts = inject(ShortcutStore);
  // Kun for den delte <datalist> nederst i skabelonen: signalet er aldrig null, og listen
  // læses her frem for i hver række, fordi der er én liste og mange rækker.
  protected readonly settings = inject(SettingsStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly expandedId = signal<number | null>(null);
  protected readonly editingNote = signal<number | null>(null);
  protected readonly completed = computed(() =>
    this.store.showCompleted() ? this.store.completedTasks() : [],
  );
  protected readonly someday = computed(() =>
    this.store.showSomeday() ? this.store.somedayTasks() : [],
  );

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

  protected toggle(task: TodoTask): void {
    this.system.clearError();
    this.editingNote.set(null);
    this.expandedId.update((id) => (id === task.id ? null : task.id));
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

  protected remove(task: TodoTask): void {
    this.editingNote.set(null);
    this.expandedId.set(null);
    this.store.remove(task.id).catch(() => {});
  }
}
