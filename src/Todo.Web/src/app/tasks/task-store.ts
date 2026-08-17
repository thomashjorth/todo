import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CreateSubTaskRequest,
  CreateTodoTaskRequest,
  DeadlineBucket,
  TasksClient,
  TodoStatus,
  TodoSubTask,
  TodoTask,
  UpdateSubTaskRequest,
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

export function subTaskProgress(task: TodoTask): string {
  return `${task.subTasks.filter((s) => s.isDone).length}/${task.subTasks.length}`;
}

export interface TaskChanges {
  title?: string;
  note?: string;
  deadline?: string;
  requester?: string;
  status?: TodoStatus;
  waitingOn?: string;
}

@Injectable({ providedIn: 'root' })
export class TaskStore {
  private readonly client = inject(TasksClient);

  readonly tasks = signal<TodoTask[]>([]);
  readonly showCompleted = signal(false);
  readonly showSomeday = signal(false);

  // The server buckets by deadline whatever the status, so a task that is done, waiting or
  // parked would otherwise linger in the deadline section it had before.
  private readonly scheduledTasks = computed(() =>
    this.tasks().filter((t) => t.status === TodoStatus.Open || t.status === TodoStatus.InProgress),
  );

  readonly completedTasks = computed(() =>
    this.tasks().filter((t) => t.status === TodoStatus.Done),
  );

  readonly waitingTasks = computed(() =>
    this.tasks().filter((t) => t.status === TodoStatus.WaitingFor),
  );

  readonly somedayTasks = computed(() =>
    this.tasks().filter((t) => t.status === TodoStatus.Someday),
  );

  // The server orders the list and assigns the buckets; grouping preserves that order.
  readonly sections = computed<TaskSection[]>(() =>
    bucketOrder
      .map((bucket) => ({
        bucket,
        tasks: this.scheduledTasks().filter((t) => t.bucket === bucket),
      }))
      .filter((section) => section.tasks.length > 0),
  );

  async load(): Promise<void> {
    const response = await firstValueFrom(
      this.client.listTasks(this.showCompleted(), this.showSomeday()),
    );
    this.tasks.set(response.items);
  }

  async setShowCompleted(value: boolean): Promise<void> {
    this.showCompleted.set(value);
    await this.load();
  }

  async setShowSomeday(value: boolean): Promise<void> {
    this.showSomeday.set(value);
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
      waitingOn: task.waitingOn,
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

  // The subtasks arrive inside their parent task, so every change reloads the list.
  async addSubTask(taskId: string, title: string): Promise<void> {
    const trimmed = title.trim();
    if (!trimmed) {
      return;
    }

    await firstValueFrom(
      this.client.createSubTask(taskId, new CreateSubTaskRequest({ title: trimmed })),
    );
    await this.load();
  }

  async setSubTaskDone(taskId: string, subTask: TodoSubTask, isDone: boolean): Promise<void> {
    await firstValueFrom(
      this.client.updateSubTask(
        taskId,
        subTask.id,
        new UpdateSubTaskRequest({ title: subTask.title, isDone }),
      ),
    );
    await this.load();
  }

  async removeSubTask(taskId: string, subTaskId: string): Promise<void> {
    await firstValueFrom(this.client.deleteSubTask(taskId, subTaskId));
    await this.load();
  }
}
