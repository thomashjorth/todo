import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoSubTask, TodoTask } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { renderMarkdown } from '../markdown/render-markdown';
import { TaskChanges, TaskStore, subTaskProgress } from './task-store';

const statusOptions: readonly TodoStatus[] = [
  TodoStatus.Open,
  TodoStatus.InProgress,
  TodoStatus.Done,
];

@Component({
  selector: 'app-task-list',
  imports: [DeadlineDate, TranslocoPipe],
  templateUrl: './task-list.html',
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly statusOptions = statusOptions;
  protected readonly done = TodoStatus.Done;
  protected readonly progress = subTaskProgress;
  protected readonly expandedId = signal<string | null>(null);
  protected readonly completed = computed(() =>
    this.store.showCompleted() ? this.store.completedTasks() : [],
  );

  constructor() {
    // A failed load needs no message of its own: the health line already reports the API down.
    this.store.load().catch(() => {});
  }

  // The API's enum values are the leaves of the key, so a new bucket needs no mapping here.
  protected sectionKey(bucket: DeadlineBucket): string {
    return `tasks.sections.${bucket}`;
  }

  protected statusKey(status: TodoStatus): string {
    return `tasks.statuses.${status}`;
  }

  protected rendered(task: TodoTask): string {
    return renderMarkdown(task.note);
  }

  protected create(input: HTMLInputElement): void {
    const title = input.value;
    if (!title.trim()) {
      return;
    }

    input.value = '';
    this.store.add(title).catch(() => {});
  }

  protected toggle(task: TodoTask): void {
    this.expandedId.update((id) => (id === task.id ? null : task.id));
  }

  protected save(task: TodoTask, changes: TaskChanges): void {
    this.store.update(task, changes).catch(() => {});
  }

  protected saveStatus(task: TodoTask, status: string): void {
    this.save(task, { status: status as TodoStatus });
  }

  protected setDone(task: TodoTask, isDone: boolean): void {
    this.save(task, { status: isDone ? TodoStatus.Done : TodoStatus.Open });
  }

  protected setShowCompleted(value: boolean): void {
    this.store.setShowCompleted(value).catch(() => {});
  }

  protected createSubTask(task: TodoTask, input: HTMLInputElement): void {
    const title = input.value;
    if (!title.trim()) {
      return;
    }

    input.value = '';
    this.store.addSubTask(task.id, title).catch(() => {});
  }

  protected setSubTaskDone(task: TodoTask, subTask: TodoSubTask, isDone: boolean): void {
    this.store.setSubTaskDone(task.id, subTask, isDone).catch(() => {});
  }

  protected removeSubTask(task: TodoTask, subTask: TodoSubTask): void {
    this.store.removeSubTask(task.id, subTask.id).catch(() => {});
  }

  protected remove(task: TodoTask): void {
    this.expandedId.set(null);
    this.store.remove(task.id).catch(() => {});
  }

  protected text(value: string): string | undefined {
    return value.trim() || undefined;
  }
}
