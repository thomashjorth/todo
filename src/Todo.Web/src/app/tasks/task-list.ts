import { Component, inject, signal } from '@angular/core';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { TaskChanges, TaskStore } from './task-store';

const bucketLabels: Record<DeadlineBucket, string> = {
  [DeadlineBucket.Overdue]: 'Overskredet',
  [DeadlineBucket.Today]: 'I dag',
  [DeadlineBucket.ThisWeek]: 'Denne uge',
  [DeadlineBucket.Later]: 'Senere',
  [DeadlineBucket.NoDeadline]: 'Uden deadline',
};

const statusOptions: readonly { value: TodoStatus; label: string }[] = [
  { value: TodoStatus.Open, label: 'Åben' },
  { value: TodoStatus.InProgress, label: 'I gang' },
  { value: TodoStatus.Done, label: 'Færdig' },
];

@Component({
  selector: 'app-task-list',
  templateUrl: './task-list.html',
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly statusOptions = statusOptions;
  protected readonly expandedId = signal<string | null>(null);

  constructor() {
    // A failed load needs no message of its own: the health line already reports the API down.
    this.store.load().catch(() => {});
  }

  protected label(bucket: DeadlineBucket): string {
    return bucketLabels[bucket];
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

  protected remove(task: TodoTask): void {
    this.expandedId.set(null);
    this.store.remove(task.id).catch(() => {});
  }

  protected text(value: string): string | undefined {
    return value.trim() || undefined;
  }
}
