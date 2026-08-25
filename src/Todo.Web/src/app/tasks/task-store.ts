import { ApplicationRef, Injectable, computed, inject, signal } from '@angular/core';
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
import { ReducedMotion } from '../layout/reduced-motion';
import { WideScreen } from '../layout/wide-screen';

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

/** The status lists, in the order they stand below the deadline sections on screen. */
const statusOrder: readonly TodoStatus[] = [
  TodoStatus.WaitingFor,
  TodoStatus.Done,
  TodoStatus.Someday,
];

/**
 * One place a task can sit. A discriminated union rather than two nullable fields, so a reader that
 * only wants the deadline sections narrows to a non-null `bucket` without an assertion - `strict`
 * is on, and `TaskSection.bucket` is not optional.
 */
export type PlacedGroup =
  | { kind: 'bucket'; bucket: DeadlineBucket; tasks: TodoTask[] }
  | { kind: 'status'; status: TodoStatus; tasks: TodoTask[] };

/**
 * Where every task sits, as the one rule its readers share.
 *
 * It takes a list rather than reading a signal, and that is the whole reason it exists: a caller
 * has to be able to ask where the tasks in an *incoming* list would sit, before that list is the
 * one on screen. A signal cannot answer that - it only knows the list it already holds.
 *
 * The server buckets by deadline whatever the status, so a task that is done, waiting or parked
 * would otherwise linger in the deadline section it had before; that is what the first filter is
 * for. Beyond it the server's order survives untouched, and the only thing that reorders inside a
 * group is the in-progress rule.
 *
 * Empty groups are dropped, in both halves. A section with no tasks would render a heading over
 * nothing, and a placement key for a group nobody can see would be a difference nobody can follow.
 */
export function placeTasks(tasks: TodoTask[]): PlacedGroup[] {
  const scheduled = tasks.filter(
    (t) => t.status === TodoStatus.Open || t.status === TodoStatus.InProgress,
  );

  const groups: PlacedGroup[] = bucketOrder.map((bucket) => ({
    kind: 'bucket' as const,
    bucket,
    // filter already handed us a fresh array, so the in-place sort cannot reach `tasks`.
    tasks: inProgressFirst(scheduled.filter((t) => t.bucket === bucket)),
  }));

  for (const status of statusOrder) {
    groups.push({ kind: 'status', status, tasks: tasks.filter((t) => t.status === status) });
  }

  return groups.filter((group) => group.tasks.length > 0);
}

/**
 * Where every task sits, keyed by id, so two lists can be compared across a reload.
 *
 * The key carries the kind as well as the group, which costs nothing and closes a trap: no bucket
 * name and status name collide today, but a future pair that did would silently merge two groups
 * and make a real move look like no move at all.
 *
 * The index is part of it on purpose. Without it a task lifted to the top of its own section - what
 * `inProgressFirst` does when a status goes to in progress - would compare equal, and the row would
 * jump without a transition.
 */
function placements(tasks: TodoTask[]): Map<number, string> {
  const places = new Map<number, string>();

  for (const group of placeTasks(tasks)) {
    const where = group.kind === 'bucket' ? group.bucket : group.status;
    group.tasks.forEach((task, index) => places.set(task.id, `${group.kind}:${where}#${index}`));
  }

  return places;
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
  private readonly appRef = inject(ApplicationRef);
  private readonly wide = inject(WideScreen);
  private readonly reducedMotion = inject(ReducedMotion);

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

  readonly completedTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.Done),
  );

  readonly waitingTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.WaitingFor),
  );

  readonly somedayTasks = computed(() =>
    this.matching().filter((t) => t.status === TodoStatus.Someday),
  );

  /**
   * The deadline sections, read off the shared placement rule rather than grouping a second time.
   *
   * `flatMap` rather than `filter` plus `map`: a `filter` on `kind` does not narrow the union, so
   * the map afterwards would need a non-null assertion on `bucket`. Returning nothing for a status
   * group narrows without one.
   */
  readonly sections = computed<TaskSection[]>(() =>
    placeTasks(this.matching()).flatMap((group) =>
      group.kind === 'bucket' ? [{ bucket: group.bucket, tasks: group.tasks }] : [],
    ),
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

    await this.apply(response.items);
  }

  /**
   * Puts the new list on screen, animating the rows that changed place.
   *
   * The HTTP round trip is over before `startViewTransition` is called, and that order is
   * load-bearing: the browser freezes the old snapshot for the length of the callback, so starting
   * the transition first would hold a still picture of the app for as long as the server took.
   *
   * `t.ready` is awaited nowhere. Measured in Chromium 148: it *rejects* every time the transition
   * is skipped - a hidden document, another transition already running, two elements claiming the
   * same name - and an uncaught rejection would land in `provideBrowserGlobalErrorListeners`.
   * `t.finished` resolves in the same case, so it is the safe one to wait for, and the DOM-update
   * callback runs either way: the list cannot be lost to a skipped animation.
   */
  private async apply(items: TodoTask[]): Promise<void> {
    if (!this.animates(items)) {
      this.tasks.set(items);
      return;
    }

    const transition = document.startViewTransition(() => {
      this.tasks.set(items);
      // Zoneless, so nothing else would have run change detection before the browser takes the new
      // snapshot. Synchronous by design: `whenStable` resolves a microtask later, and the snapshot
      // would be of the old DOM.
      this.appRef.tick();
    });

    await transition.finished;
  }

  /**
   * Whether this list is worth animating, asked before it is set - which is the whole reason
   * `placements` takes a list rather than reading a signal.
   *
   * Four terms, and three of them buy their own behaviour: no `startViewTransition` at all is jsdom
   * (28.1.0, measured), less motion is the user's own setting, and side by side is the measured
   * defect in section 8 of the design - the transition tree lives in the top layer, so a row escapes
   * the scrolling column and paints over the health line.
   *
   * `prev.has(id)` is what makes the rest quiet. A first load has an empty list, so no id is in
   * both; a new task lands at the end of its section and shifts nobody; and a note or a subtask
   * changes no place at all. None of the three needs a branch of its own.
   *
   * The cost, said plainly: this measures the unfiltered list while the screen shows the searched
   * one, so a move hidden behind an active search runs a transition that animates nothing. The
   * alternative was to let the gate know the query and both switches - three more sources to drift.
   */
  private animates(items: TodoTask[]): boolean {
    if (typeof document.startViewTransition !== 'function') {
      return false;
    }

    if (this.reducedMotion.reduce() || this.wide.wide()) {
      return false;
    }

    const next = placements(items);
    const prev = placements(this.tasks());

    return [...next].some(([id, place]) => prev.has(id) && prev.get(id) !== place);
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
