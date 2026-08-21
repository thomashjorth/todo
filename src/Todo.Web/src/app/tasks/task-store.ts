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
  DeadlineBucket.Deferred,
];

export interface TaskSection {
  bucket: DeadlineBucket;
  tasks: TodoTask[];
}

/**
 * What is under way comes first inside its section, and everything else keeps the order the server
 * sent - which is by deadline and then by start date, and the reason a section is worth reading top
 * to bottom at all.
 *
 * A rank plus a stable sort rather than a comparison that also looks at the dates: sorting is stable
 * per spec since ES2019, so equal ranks come out in their original order and the server's ordering
 * survives untouched. Writing the dates into the comparison would mean maintaining the rule in two
 * places, and the two would drift.
 *
 * Only the deadline sections need it. The waiting, done and someday lists are grouped by status, so
 * none of them can hold a task that is in progress.
 */
function inProgressFirst(tasks: TodoTask[]): TodoTask[] {
  // filter already handed us a fresh array, so this sorts in place rather than copying again.
  return tasks.sort(
    (a, b) =>
      Number(b.status === TodoStatus.InProgress) - Number(a.status === TodoStatus.InProgress),
  );
}

export function subTaskProgress(task: TodoTask): string {
  return `${task.subTasks.filter((s) => s.isDone).length}/${task.subTasks.length}`;
}

export interface TaskChanges {
  title?: string;
  note?: string;
  deadline?: string;
  deferUntil?: string;
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

  /**
   * The task whose who field should take the focus, set when a status change hands the task over
   * and cleared once the field has it. It lives here rather than on the row, because the row that
   * asks is not the row that answers: measured, the reload moves the task out of its deadline
   * section and into the waiting one, so that &lt;li&gt; — and the component instance with it — is
   * destroyed, and a fresh one renders the field. A flag held in the row was therefore always
   * false by the time the field existed. Nothing reads this from a template, so writing it from an
   * effect cannot fight change detection.
   */
  readonly askingWho = signal<number | null>(null);

  /**
   * What the search box holds. Filtering happens here rather than on the server: the list is one
   * person's tasks and is already in memory, so a query costs nothing and the result is instant.
   *
   * The cost of that choice, said plainly: it can only find what has been loaded. Done and someday
   * tasks are fetched only when their switch is on, so a search with both switches off does not
   * reach them - the list removes what does not match, and it cannot remove what was never there.
   */
  readonly query = signal('');

  /** Only the newest load may write the list; see the check in `load`. */
  private loadSequence = 0;

  /**
   * The tasks the search leaves in. Every list below reads this rather than `tasks`, so one filter
   * covers the deadline sections, the waiting list, the done list and someday alike - applied per
   * section it would be five places to forget it.
   *
   * Title and note, matched as a plain case-insensitive substring. The note is raw markdown, so a
   * search hits what was typed rather than what is rendered: `**deploy**` is found by `deploy` and
   * also by `*`. Honest either way, and the alternative - rendering every note to text on every
   * keystroke - buys nothing here.
   */
  private readonly matching = computed(() => {
    const query = this.query().trim().toLowerCase();

    if (query.length === 0) {
      return this.tasks();
    }

    return this.tasks().filter(
      (t) =>
        t.title.toLowerCase().includes(query) || (t.note?.toLowerCase().includes(query) ?? false),
    );
  });

  /** Whether a search is narrowing the list, so an empty screen can say which kind of empty. */
  readonly searching = computed(() => this.query().trim().length > 0);

  // The server buckets by deadline whatever the status, so a task that is done, waiting or
  // parked would otherwise linger in the deadline section it had before.
  private readonly scheduledTasks = computed(() =>
    this.matching().filter(
      (t) => t.status === TodoStatus.Open || t.status === TodoStatus.InProgress,
    ),
  );

  readonly completedTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.Done),
  );

  readonly waitingTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.WaitingFor),
  );

  readonly somedayTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.Someday),
  );

  // The server orders the list and assigns the buckets; grouping preserves that order, and the
  // only thing that reorders inside a section is the in-progress rule below.
  readonly sections = computed<TaskSection[]>(() =>
    bucketOrder
      .map((bucket) => ({
        bucket,
        tasks: inProgressFirst(this.scheduledTasks().filter((t) => t.bucket === bucket)),
      }))
      .filter((section) => section.tasks.length > 0),
  );

  async load(): Promise<void> {
    const sequence = ++this.loadSequence;
    const response = await firstValueFrom(
      this.client.listTasks(this.showCompleted(), this.showSomeday()),
    );

    // Two loads can be in flight at once — flipping both switches quickly does it — and nothing
    // orders their responses. Without this the older answer can land last and wipe the newer
    // list, and it stays wrong until something else triggers a reload.
    if (sequence !== this.loadSequence) {
      return;
    }

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
      deferUntil: task.deferUntil,
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

  async remove(id: number): Promise<void> {
    await firstValueFrom(this.client.deleteTask(id));
    await this.load();
  }

  // The subtasks arrive inside their parent task, so every change reloads the list.
  async addSubTask(taskId: number, title: string): Promise<void> {
    const trimmed = title.trim();
    if (!trimmed) {
      return;
    }

    await firstValueFrom(
      this.client.createSubTask(taskId, new CreateSubTaskRequest({ title: trimmed })),
    );
    await this.load();
  }

  async setSubTaskDone(taskId: number, subTask: TodoSubTask, isDone: boolean): Promise<void> {
    await firstValueFrom(
      this.client.updateSubTask(
        taskId,
        subTask.id,
        new UpdateSubTaskRequest({ title: subTask.title, isDone }),
      ),
    );
    await this.load();
  }

  async removeSubTask(taskId: number, subTaskId: number): Promise<void> {
    await firstValueFrom(this.client.deleteSubTask(taskId, subTaskId));
    await this.load();
  }
}
