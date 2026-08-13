import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CreateTodoTaskRequest,
  DeadlineBucket,
  TasksClient,
  TodoStatus,
  TodoTask,
  UpdateTodoTaskRequest,
} from '../api/todo-client';

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

export interface TaskChanges {
  title?: string;
  note?: string;
  deadline?: string;
  requester?: string;
  status?: TodoStatus;
}

@Injectable({ providedIn: 'root' })
export class TaskStore {
  private readonly client = inject(TasksClient);

  readonly tasks = signal<TodoTask[]>([]);
  readonly showCompleted = signal(false);

  // The server buckets by deadline only, so a completed task would otherwise linger
  // in the deadline section it had before it was finished.
  private readonly openTasks = computed(() =>
    this.tasks().filter((t) => t.status !== TodoStatus.Done),
  );

  readonly completedTasks = computed(() =>
    this.tasks().filter((t) => t.status === TodoStatus.Done),
  );

  // The server orders the list and assigns the buckets; grouping preserves that order.
  readonly sections = computed<TaskSection[]>(() =>
    bucketOrder
      .map((bucket) => ({ bucket, tasks: this.openTasks().filter((t) => t.bucket === bucket) }))
      .filter((section) => section.tasks.length > 0),
  );

  async load(): Promise<void> {
    const response = await firstValueFrom(this.client.listTasks(this.showCompleted()));
    this.tasks.set(response.items);
  }

  async setShowCompleted(value: boolean): Promise<void> {
    this.showCompleted.set(value);
    await this.load();
  }

  async add(title: string): Promise<void> {
    const trimmed = title.trim();
    if (!trimmed) {
      return;
    }

    await firstValueFrom(this.client.createTask(new CreateTodoTaskRequest({ title: trimmed })));
    await this.load();
  }

  async update(task: TodoTask, changes: TaskChanges): Promise<void> {
    const current = {
      title: task.title,
      note: task.note,
      deadline: task.deadline,
      requester: task.requester,
      status: task.status,
    };
    // A spread rather than ?? so that an explicit undefined clears the field.
    const next = { ...current, ...changes };

    const keys = Object.keys(current) as (keyof typeof current)[];
    if (keys.every((key) => next[key] === current[key])) {
      return;
    }

    await firstValueFrom(this.client.updateTask(task.id, new UpdateTodoTaskRequest(next)));
    await this.load();
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.client.deleteTask(id));
    await this.load();
  }
}
