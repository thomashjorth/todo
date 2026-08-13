import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { TaskStore } from './task-store';

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

  it('should order sections overdue, today, this week, later, no deadline', () => {
    store.tasks.set([
      taskIn(DeadlineBucket.Later),
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
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items: [taskIn(DeadlineBucket.Today).toJSON()] })]));
    await loaded;

    expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
  });

  it('should ask the API for completed tasks once they are shown', async () => {
    const shown = store.setShowCompleted(true);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=true')
      .flush(new Blob([JSON.stringify({ items: [] })]));
    await shown;

    expect(store.showCompleted()).toBe(true);
  });

  it('should create a task from the trimmed title and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const added = store.add('  Køb mælk  ');

    const created = http.expectOne('/api/tasks');
    expect(created.request.method).toBe('POST');
    expect(JSON.parse(created.request.body).title).toBe('Køb mælk');
    created.flush(new Blob([JSON.stringify(taskIn(DeadlineBucket.Today).toJSON())]));

    const reloaded = await vi.waitFor(() => http.expectOne('/api/tasks?includeCompleted=false'));
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

    const reloaded = await vi.waitFor(() => http.expectOne('/api/tasks?includeCompleted=false'));
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await updated;
  });

  it('should leave the deadline out of the update when it is cleared', async () => {
    const task = new TodoTask({ ...taskIn(DeadlineBucket.Today), deadline: '2026-08-13' });
    const http = TestBed.inject(HttpTestingController);

    void store.update(task, { deadline: undefined });

    const request = http.expectOne(`/api/tasks/${task.id}`);
    expect(JSON.parse(request.request.body).deadline).toBeUndefined();
  });

  it('should send no request when the update changes nothing', async () => {
    const task = new TodoTask({ ...taskIn(DeadlineBucket.Today), requester: 'Anna' });

    await store.update(task, { requester: 'Anna', status: TodoStatus.Open });

    TestBed.inject(HttpTestingController).verify();
  });

  it('should delete a task and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const removed = store.remove('abc');

    const request = http.expectOne('/api/tasks/abc');
    expect(request.request.method).toBe('DELETE');
    request.flush(new Blob([]), { status: 204, statusText: 'No Content' });

    const reloaded = await vi.waitFor(() => http.expectOne('/api/tasks?includeCompleted=false'));
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await removed;

    expect(store.tasks()).toEqual([]);
  });
});
