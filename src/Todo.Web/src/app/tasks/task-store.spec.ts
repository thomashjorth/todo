import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  API_BASE_URL,
  DeadlineBucket,
  TodoStatus,
  TodoSubTask,
  TodoTask,
} from '../api/todo-client';
import { TaskStore, subTaskProgress } from './task-store';

function taskIn(bucket: DeadlineBucket): TodoTask {
  return new TodoTask({
    id: `${bucket}-1`,
    sourceId: 'manual',
    title: `Task in ${bucket}`,
    status: TodoStatus.Open,
    bucket,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  });
}

function subTask(title: string): TodoSubTask {
  return new TodoSubTask({ id: 'sub-1', title, isDone: false });
}

describe('TaskStore', () => {
  let store: TaskStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    });
    store = TestBed.inject(TaskStore);
  });

  it('should order sections overdue, today, this week, later, no deadline, deferred', () => {
    store.tasks.set([
      taskIn(DeadlineBucket.Later),
      taskIn(DeadlineBucket.Deferred),
      taskIn(DeadlineBucket.NoDeadline),
      taskIn(DeadlineBucket.Today),
      taskIn(DeadlineBucket.Overdue),
      taskIn(DeadlineBucket.ThisWeek),
    ]);

    expect(store.sections().map((s) => s.bucket)).toEqual([
      DeadlineBucket.Overdue,
      DeadlineBucket.Today,
      DeadlineBucket.ThisWeek,
      DeadlineBucket.Later,
      DeadlineBucket.NoDeadline,
      DeadlineBucket.Deferred,
    ]);
  });

  it('should leave out buckets that have no tasks', () => {
    store.tasks.set([taskIn(DeadlineBucket.Today), taskIn(DeadlineBucket.NoDeadline)]);

    const sections = store.sections();

    expect(sections.map((s) => s.bucket)).toEqual([
      DeadlineBucket.Today,
      DeadlineBucket.NoDeadline,
    ]);
    expect(sections.every((s) => s.tasks.length > 0)).toBe(true);
  });

  it('should keep completed tasks out of the deadline sections', () => {
    const done = new TodoTask({ ...taskIn(DeadlineBucket.Today), status: TodoStatus.Done });
    store.tasks.set([taskIn(DeadlineBucket.Overdue), done]);

    expect(store.sections().map((s) => s.bucket)).toEqual([DeadlineBucket.Overdue]);
    expect(store.completedTasks()).toEqual([done]);
  });

  it('should keep a waiting task out of the deadline sections and list it on its own', () => {
    const waiting = new TodoTask({
      ...taskIn(DeadlineBucket.Later),
      status: TodoStatus.WaitingFor,
      waitingOn: 'Bo',
      waitingDays: 0,
    });
    store.tasks.set([taskIn(DeadlineBucket.Today), waiting]);

    expect(store.sections().map((s) => s.bucket)).toEqual([DeadlineBucket.Today]);
    expect(store.sections().flatMap((s) => s.tasks)).not.toContain(waiting);
    expect(store.waitingTasks()).toEqual([waiting]);
  });

  it('should show a parked task in the someday list and nowhere else', () => {
    const parked = new TodoTask({ ...taskIn(DeadlineBucket.Today), status: TodoStatus.Someday });
    store.tasks.set([parked]);

    expect(store.sections()).toEqual([]);
    expect(store.waitingTasks()).toEqual([]);
    expect(store.completedTasks()).toEqual([]);
    expect(store.somedayTasks()).toEqual([parked]);
  });

  it('should ask the API for parked tasks once they are shown', async () => {
    const shown = store.setShowSomeday(true);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=true')
      .flush(new Blob([JSON.stringify({ items: [] })]));
    await shown;

    expect(store.showSomeday()).toBe(true);
  });

  it('should have no completed tasks while every task is open', () => {
    store.tasks.set([taskIn(DeadlineBucket.Today), taskIn(DeadlineBucket.Later)]);

    expect(store.completedTasks()).toEqual([]);
  });

  it('should have no sections before anything is loaded', () => {
    expect(store.sections()).toEqual([]);
  });

  it('should load the tasks the API returns, excluding completed ones by default', async () => {
    const loaded = store.load();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [taskIn(DeadlineBucket.Today).toJSON()] })]));
    await loaded;

    expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
  });

  it('should ask the API for completed tasks once they are shown', async () => {
    const shown = store.setShowCompleted(true);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=true&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [] })]));
    await shown;

    expect(store.showCompleted()).toBe(true);
  });

  // Flipping both switches in quick succession puts two loads in flight, and nothing orders
  // their responses. When the older one straggles in last it used to win, and the list silently
  // lost whatever the newer request had asked for until something else triggered a reload.
  it('should not let a slow earlier load overwrite a newer list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const completed = store.setShowCompleted(true);
    const someday = store.setShowSomeday(true);

    const stale = http.expectOne('/api/tasks?includeCompleted=true&includeSomeday=false');
    const fresh = http.expectOne('/api/tasks?includeCompleted=true&includeSomeday=true');

    // The newer request answers first; the older one arrives after it.
    fresh.flush(new Blob([JSON.stringify({ items: [taskIn(DeadlineBucket.Today).toJSON()] })]));
    stale.flush(new Blob([JSON.stringify({ items: [] })]));

    await Promise.all([completed, someday]);

    expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
  });

  it('should create a task from the trimmed title and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const added = store.add('  Køb mælk  ');

    const created = http.expectOne('/api/tasks');
    expect(created.request.method).toBe('POST');
    expect(JSON.parse(created.request.body).title).toBe('Køb mælk');
    created.flush(new Blob([JSON.stringify(taskIn(DeadlineBucket.Today).toJSON())]));

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [taskIn(DeadlineBucket.Today).toJSON()] })]));
    await added;

    expect(store.tasks()).toHaveLength(1);
  });

  it.each(['', '   '])('should send no request for the title %j', async (title) => {
    await store.add(title);

    TestBed.inject(HttpTestingController).verify();
    expect(store.tasks()).toEqual([]);
  });

  it('should send the whole task on update, with only the changed field replaced', async () => {
    const task = new TodoTask({
      ...taskIn(DeadlineBucket.Today),
      title: 'Betal regningen',
      note: 'Husk kontonummeret',
      deadline: '2026-08-13',
      requester: 'Anna',
    });
    const http = TestBed.inject(HttpTestingController);

    const updated = store.update(task, { requester: 'Bo' });

    const request = http.expectOne(`/api/tasks/${task.id}`);
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({
      title: 'Betal regningen',
      note: 'Husk kontonummeret',
      deadline: '2026-08-13',
      requester: 'Bo',
      status: TodoStatus.Open,
    });
    request.flush(new Blob([JSON.stringify(task.toJSON())]));

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await updated;
  });

  it('should send who the task is waiting on together with the new status', async () => {
    const task = taskIn(DeadlineBucket.Today);

    void store.update(task, { status: TodoStatus.WaitingFor, waitingOn: 'Bo' });

    const body = JSON.parse(
      TestBed.inject(HttpTestingController).expectOne(`/api/tasks/${task.id}`).request.body,
    );
    expect(body.status).toBe(TodoStatus.WaitingFor);
    expect(body.waitingOn).toBe('Bo');
  });

  it('should leave the deadline out of the update when it is cleared', async () => {
    const task = new TodoTask({ ...taskIn(DeadlineBucket.Today), deadline: '2026-08-13' });
    const http = TestBed.inject(HttpTestingController);

    void store.update(task, { deadline: undefined });

    const request = http.expectOne(`/api/tasks/${task.id}`);
    expect(JSON.parse(request.request.body).deadline).toBeUndefined();
  });

  // Every request carries every field, because the API reads an absent field as cleared. While
  // `current` was missing deferUntil, saving anything else silently wiped a stored start date.
  it('should keep the start date when only the requester changes', async () => {
    const task = new TodoTask({
      ...taskIn(DeadlineBucket.Deferred),
      deferUntil: '2026-09-01',
      requester: 'Anna',
    });
    const http = TestBed.inject(HttpTestingController);

    void store.update(task, { requester: 'Bo' });

    const request = http.expectOne(`/api/tasks/${task.id}`);
    expect(JSON.parse(request.request.body).deferUntil).toBe('2026-09-01');
  });

  it('should send no request when the update changes nothing', async () => {
    const task = new TodoTask({ ...taskIn(DeadlineBucket.Today), requester: 'Anna' });

    await store.update(task, { requester: 'Anna', status: TodoStatus.Open });

    TestBed.inject(HttpTestingController).verify();
  });

  it('should add a subtask from the trimmed title and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const added = store.addSubTask('abc', '  Pak køkkenet  ');

    const created = http.expectOne('/api/tasks/abc/subtasks');
    expect(created.request.method).toBe('POST');
    expect(JSON.parse(created.request.body).title).toBe('Pak køkkenet');
    created.flush(new Blob([JSON.stringify(subTask('Pak køkkenet').toJSON())]), {
      status: 201,
      statusText: 'Created',
    });

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await added;
  });

  it.each(['', '   '])('should send no request for the subtask title %j', async (title) => {
    await store.addSubTask('abc', title);

    TestBed.inject(HttpTestingController).verify();
  });

  it('should send the subtask title along when it is ticked off', async () => {
    const http = TestBed.inject(HttpTestingController);

    void store.setSubTaskDone('abc', subTask('Pak køkkenet'), true);

    const request = http.expectOne(`/api/tasks/abc/subtasks/sub-1`);
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ title: 'Pak køkkenet', isDone: true });
  });

  it('should delete a subtask and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const removed = store.removeSubTask('abc', 'sub-1');

    const request = http.expectOne('/api/tasks/abc/subtasks/sub-1');
    expect(request.request.method).toBe('DELETE');
    request.flush(new Blob([]), { status: 204, statusText: 'No Content' });

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await removed;
  });

  it('should count only the ticked subtasks as progress', () => {
    const task = new TodoTask({
      ...taskIn(DeadlineBucket.Today),
      subTasks: [
        new TodoSubTask({ id: 'a', title: 'Et', isDone: true }),
        new TodoSubTask({ id: 'b', title: 'To', isDone: false }),
        new TodoSubTask({ id: 'c', title: 'Tre', isDone: true }),
      ],
    });

    expect(subTaskProgress(task)).toBe('2/3');
  });

  it('should report no progress for a task without subtasks', () => {
    expect(subTaskProgress(taskIn(DeadlineBucket.Today))).toBe('0/0');
  });

  it('should delete a task and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const removed = store.remove('abc');

    const request = http.expectOne('/api/tasks/abc');
    expect(request.request.method).toBe('DELETE');
    request.flush(new Blob([]), { status: 204, statusText: 'No Content' });

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await removed;

    expect(store.tasks()).toEqual([]);
  });
});
