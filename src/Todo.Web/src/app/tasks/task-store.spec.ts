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
});
