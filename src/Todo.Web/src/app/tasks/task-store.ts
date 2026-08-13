import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CreateTodoTaskRequest, DeadlineBucket, TasksClient, TodoTask } from '../api/todo-client';

const bucketOrder: readonly DeadlineBucket[] = [
  DeadlineBucket.Overdue,
  DeadlineBucket.Today,
  DeadlineBucket.ThisWeek,
  DeadlineBucket.Later,
  DeadlineBucket.NoDeadline,
];

export interface TaskSection {
  bucket: DeadlineBucket;
  tasks: TodoTask[];
}

@Injectable({ providedIn: 'root' })
export class TaskStore {
  private readonly client = inject(TasksClient);

  readonly tasks = signal<TodoTask[]>([]);
  readonly showCompleted = signal(false);

  // The server orders the list and assigns the buckets; grouping preserves that order.
  readonly sections = computed<TaskSection[]>(() =>
    bucketOrder
      .map((bucket) => ({ bucket, tasks: this.tasks().filter((t) => t.bucket === bucket) }))
      .filter((section) => section.tasks.length > 0),
  );

  async load(): Promise<void> {
    const response = await firstValueFrom(this.client.listTasks(this.showCompleted()));
    this.tasks.set(response.items);
  }

  async add(title: string): Promise<void> {
    const trimmed = title.trim();
    if (!trimmed) {
      return;
    }

    await firstValueFrom(this.client.createTask(new CreateTodoTaskRequest({ title: trimmed })));
    await this.load();
  }
}
