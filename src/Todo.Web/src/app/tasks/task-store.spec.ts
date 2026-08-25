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
import { ReducedMotion } from '../layout/reduced-motion';
import { WideScreen } from '../layout/wide-screen';
import { TaskStore, subTaskProgress } from './task-store';

// Ids er tal, så bucket-navnet kan ikke være en del af dem. Tælleren giver hvert kald sit eget
// id, fordi `track task.id` og storens filtre regner med, at to opgaver ikke deler id.
let nextId = 1;

function taskIn(bucket: DeadlineBucket): TodoTask {
  return new TodoTask({
    id: nextId++,
    sourceId: 'manual',
    title: `Task in ${bucket}`,
    status: TodoStatus.Open,
    bucket,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  });
}

// Ad hoc-id'er i de tests der ikke går gennem `taskIn`: tresserne er opgaver, halvfemserne
// underopgaver, så det kan læses hvad et id i en URL peger på.
const someTaskId = 61;
const someSubTaskId = 91;

function subTask(title: string): TodoSubTask {
  return new TodoSubTask({ id: someSubTaskId, title, isDone: false });
}

/**
 * A task with a title and note the search can be pointed at. Separate from `taskIn`, which names
 * itself after its bucket: a search test needs to control both strings, and reusing that one would
 * have every task matching the word "task".
 */
function taskWith(title: string, note?: string, status = TodoStatus.Open): TodoTask {
  return new TodoTask({
    id: nextId++,
    sourceId: 'manual',
    title,
    note,
    status,
    bucket: DeadlineBucket.Today,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  });
}
/**
 * The same identity twice, so a spec can move one task between two lists. `taskIn` numbers its own
 * ids, which is the opposite of what a move needs.
 */
function taskAt(
  id: number,
  bucket: DeadlineBucket,
  status = TodoStatus.Open,
  note?: string,
): TodoTask {
  return new TodoTask({
    id,
    sourceId: 'manual',
    title: `Task ${id}`,
    note,
    status,
    bucket,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  });
}

/**
 * Records every view transition the store starts, and runs the DOM update synchronously inside the
 * call the way the browser does, so the list is set by the time `finished` resolves.
 *
 * jsdom 28.1.0 has no `startViewTransition` at all, so this is both the stub and the only reason the
 * animated path can be reached from a spec.
 */
function recordViewTransitions(): { started: number } {
  const record = { started: 0 };

  document.startViewTransition = ((update?: ViewTransitionUpdateCallback) => {
    record.started++;
    void update?.();

    return {
      ready: Promise.resolve(),
      updateCallbackDone: Promise.resolve(),
      finished: Promise.resolve(),
      skipTransition: () => {},
    } as unknown as ViewTransition;
  }) as typeof document.startViewTransition;

  return record;
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

  /**
   * The rule the user asked for: what is under way is at the top of its own section. Both tasks are
   * in the same bucket, and the one in progress is seeded second - so an unsorted section would put
   * it last and this would fail.
   */
  it('should put an in-progress task first inside its section', () => {
    store.tasks.set([
      taskWith('Not started yet'),
      taskWith('Under way', undefined, TodoStatus.InProgress),
    ]);

    expect(store.sections()[0].tasks.map((t) => t.title)).toEqual(['Under way', 'Not started yet']);
  });

  /**
   * And the rest of the order survives it. The server sorts by deadline and then by start date, so
   * a comparison that did more than rank the status would throw that away - this is the assertion
   * that says the sort is stable rather than merely putting the right task on top.
   */
  it('should leave the order the server sent alone apart from lifting what is in progress', () => {
    store.tasks.set([
      taskWith('First'),
      taskWith('Second'),
      taskWith('Under way', undefined, TodoStatus.InProgress),
      taskWith('Third'),
    ]);

    expect(store.sections()[0].tasks.map((t) => t.title)).toEqual([
      'Under way',
      'First',
      'Second',
      'Third',
    ]);
  });

  // Two of them keep their own order too, for the same reason.
  it('should keep two in-progress tasks in the order they arrived', () => {
    store.tasks.set([
      taskWith('Waiting to start'),
      taskWith('Started first', undefined, TodoStatus.InProgress),
      taskWith('Started second', undefined, TodoStatus.InProgress),
    ]);

    expect(store.sections()[0].tasks.map((t) => t.title)).toEqual([
      'Started first',
      'Started second',
      'Waiting to start',
    ]);
  });
  it('should keep only the tasks whose title contains the search term', () => {
    store.tasks.set([taskWith('Deploy the release'), taskWith('Write the retro notes')]);

    store.query.set('retro');

    const section = store.sections()[0];
    expect(section.tasks.map((t) => t.title)).toEqual(['Write the retro notes']);
  });

  // Lowercased on both sides rather than matched as typed: nobody searching for a task types its
  // capitals back.
  it('should match the title whatever the casing', () => {
    store.tasks.set([taskWith('Deploy the Release')]);

    store.query.set('RELEASE');

    expect(store.sections()[0].tasks).toHaveLength(1);
  });

  it('should find a task by its note when the title says nothing', () => {
    store.tasks.set([
      taskWith('Tuesday', 'remember the certificate renewal'),
      taskWith('Wednesday', 'nothing much'),
    ]);

    store.query.set('certificate');

    expect(store.sections()[0].tasks.map((t) => t.title)).toEqual(['Tuesday']);
  });

  // A note is optional on the contract, so the filter has to survive its absence - without the
  // guard on undefined this throws rather than simply not matching.
  it('should not stumble on a task that has no note', () => {
    store.tasks.set([taskWith('Deploy the release')]);

    store.query.set('certificate');

    expect(store.sections()).toHaveLength(0);
  });

  /**
   * The one that matters most. The filter lives in a single computed that every list reads, and this
   * is what says so: a filter applied per section would be five places to forget it, and four of
   * them would keep showing what the search was meant to remove.
   */
  it('should narrow the waiting, done and someday lists too', () => {
    store.tasks.set([
      taskWith('Deploy the release', undefined, TodoStatus.WaitingFor),
      taskWith('Waiting on something else', undefined, TodoStatus.WaitingFor),
      taskWith('Deploy the old release', undefined, TodoStatus.Done),
      taskWith('Done with something else', undefined, TodoStatus.Done),
      taskWith('Deploy someday', undefined, TodoStatus.Someday),
      taskWith('Someday something else', undefined, TodoStatus.Someday),
    ]);

    store.query.set('deploy');

    expect(store.waitingTasks().map((t) => t.title)).toEqual(['Deploy the release']);
    expect(store.completedTasks().map((t) => t.title)).toEqual(['Deploy the old release']);
    expect(store.somedayTasks().map((t) => t.title)).toEqual(['Deploy someday']);
  });

  // Whitespace is not a search. Without the trim, a stray space would empty the whole list and look
  // like the tasks were gone.
  it('should show everything again when the search is cleared or only whitespace', () => {
    store.tasks.set([taskWith('Deploy the release'), taskWith('Write the retro notes')]);

    store.query.set('retro');
    expect(store.sections()[0].tasks).toHaveLength(1);

    store.query.set('   ');
    expect(store.sections()[0].tasks).toHaveLength(2);
    expect(store.searching()).toBe(false);

    store.query.set('');
    expect(store.sections()[0].tasks).toHaveLength(2);
    expect(store.searching()).toBe(false);
  });

  // What the empty screen reads from to tell "no tasks" apart from "nothing matched".
  it('should report that a search is on only while one narrows the list', () => {
    expect(store.searching()).toBe(false);

    store.query.set('retro');

    expect(store.searching()).toBe(true);
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

    const added = store.addSubTask(someTaskId, '  Pak køkkenet  ');

    const created = http.expectOne(`/api/tasks/${someTaskId}/subtasks`);
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
    await store.addSubTask(someTaskId, title);

    TestBed.inject(HttpTestingController).verify();
  });

  it('should send the subtask title along when it is ticked off', async () => {
    const http = TestBed.inject(HttpTestingController);

    void store.setSubTaskDone(someTaskId, subTask('Pak køkkenet'), true);

    const request = http.expectOne(`/api/tasks/${someTaskId}/subtasks/${someSubTaskId}`);
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ title: 'Pak køkkenet', isDone: true });
  });

  it('should delete a subtask and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const removed = store.removeSubTask(someTaskId, someSubTaskId);

    const request = http.expectOne(`/api/tasks/${someTaskId}/subtasks/${someSubTaskId}`);
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
        new TodoSubTask({ id: 92, title: 'Et', isDone: true }),
        new TodoSubTask({ id: 93, title: 'To', isDone: false }),
        new TodoSubTask({ id: 94, title: 'Tre', isDone: true }),
      ],
    });

    expect(subTaskProgress(task)).toBe('2/3');
  });

  it('should report no progress for a task without subtasks', () => {
    expect(subTaskProgress(taskIn(DeadlineBucket.Today))).toBe('0/0');
  });

  it('should delete a task and reload the list', async () => {
    const http = TestBed.inject(HttpTestingController);

    const removed = store.remove(someTaskId);

    const request = http.expectOne(`/api/tasks/${someTaskId}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(new Blob([]), { status: 204, statusText: 'No Content' });

    const reloaded = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reloaded.flush(new Blob([JSON.stringify({ items: [] })]));
    await removed;

    expect(store.tasks()).toEqual([]);
  });
  /**
   * The section transitions. Every assertion here also checks that the list was set, because that is
   * the one property the whole feature rests on: measured in Chromium 148, the DOM-update callback
   * runs even when the browser skips the transition, so an animation must never be able to lose a
   * reload. See docs/plans/2026-08-25-section-transitions-design.md.
   *
   * Five of the seven assert that *no* transition started, and those five cannot fail before the
   * feature exists - they pass on nothing. They are proven by their mutations instead, one each,
   * named at the assertion.
   */
  describe('section transitions', () => {
    afterEach(() => {
      // Assigned onto the instance, so removing it puts jsdom back to having no such function.
      Reflect.deleteProperty(document, 'startViewTransition');
    });

    async function reload(items: TodoTask[]): Promise<void> {
      const loaded = store.load();
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items: items.map((t) => t.toJSON()) })]));
      await loaded;
    }

    it('should start one view transition when a task changes section', async () => {
      const transitions = recordViewTransitions();
      store.tasks.set([taskAt(1, DeadlineBucket.NoDeadline)]);

      await reload([taskAt(1, DeadlineBucket.Today)]);

      expect(transitions.started).toBe(1);
      expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
    });

    /**
     * The index half of the gate. Task 2 goes in progress, so `inProgressFirst` lifts it over task 1
     * - same bucket, a new index for both - and a gate that compared only bucket and status would
     * see nothing move.
     */
    it('should count a lift to the top of its own section as a move', async () => {
      const transitions = recordViewTransitions();
      store.tasks.set([taskAt(1, DeadlineBucket.Today), taskAt(2, DeadlineBucket.Today)]);

      await reload([
        taskAt(1, DeadlineBucket.Today),
        taskAt(2, DeadlineBucket.Today, TodoStatus.InProgress),
      ]);

      expect(transitions.started).toBe(1);
      expect(store.tasks().map((t) => t.status)).toEqual([TodoStatus.Open, TodoStatus.InProgress]);
    });

    /** Mutation: let the gate compare whole tasks rather than their places. */
    it('should start no view transition when only a note changed', async () => {
      const transitions = recordViewTransitions();
      store.tasks.set([taskAt(1, DeadlineBucket.Today)]);

      await reload([taskAt(1, DeadlineBucket.Today, TodoStatus.Open, 'A note that was not there')]);

      expect(transitions.started).toBe(0);
      expect(store.tasks()[0].note).toBe('A note that was not there');
    });

    /**
     * Mutation: drop the `prev.has(id)` term. The new task lands after the one already there, so
     * nothing that was on screen changed place - but an unknown id has no previous place at all, and
     * comparing against `undefined` would read as a move.
     */
    it('should start no view transition when a task is only added', async () => {
      const transitions = recordViewTransitions();
      store.tasks.set([taskAt(1, DeadlineBucket.Today)]);

      await reload([taskAt(1, DeadlineBucket.Today), taskAt(2, DeadlineBucket.Today)]);

      expect(transitions.started).toBe(0);
      expect(store.tasks()).toHaveLength(2);
    });

    /**
     * Mutation: remove the wide guard. Measured 2026-08-25: side by side the row escapes the
     * scrolling column and paints over the health line, because the view transition tree lives in
     * the top layer. Section 8 of the design has the numbers.
     */
    it('should set the list without a transition side by side', async () => {
      const transitions = recordViewTransitions();
      TestBed.inject(WideScreen).wide.set(true);
      store.tasks.set([taskAt(1, DeadlineBucket.NoDeadline)]);

      await reload([taskAt(1, DeadlineBucket.Today)]);

      expect(transitions.started).toBe(0);
      expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
    });

    /** Mutation: remove the reduced-motion branch. */
    it('should set the list without a transition when less motion was asked for', async () => {
      const transitions = recordViewTransitions();
      TestBed.inject(ReducedMotion).reduce.set(true);
      store.tasks.set([taskAt(1, DeadlineBucket.NoDeadline)]);

      await reload([taskAt(1, DeadlineBucket.Today)]);

      expect(transitions.started).toBe(0);
      expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
    });

    /**
     * The guard, and the first assertion is what gives it teeth: without it this would pass because
     * nothing had stubbed the function, which is a different thing from the guard working. Remove
     * the guard and jsdom throws a TypeError instead of loading the list.
     */
    it('should set the list where the environment has no startViewTransition', async () => {
      expect('startViewTransition' in document).toBe(false);
      store.tasks.set([taskAt(1, DeadlineBucket.NoDeadline)]);

      await reload([taskAt(1, DeadlineBucket.Today)]);

      expect(store.tasks().map((t) => t.bucket)).toEqual([DeadlineBucket.Today]);
    });
  });
});
