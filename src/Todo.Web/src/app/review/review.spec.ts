import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { API_BASE_URL, DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { TaskStore } from '../tasks/task-store';
import { Review } from './review';

let nextId = 1;

function task(fields: Partial<TodoTask>): TodoTask {
  return new TodoTask({
    id: nextId++,
    sourceId: 'manual',
    title: `Task ${nextId}`,
    status: TodoStatus.Open,
    bucket: DeadlineBucket.NoDeadline,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
    ...fields,
  });
}

const status = { due: true, lastCompletedAt: null, nextDueAt: '2026-08-28T13:00:00Z' };

/**
 * The screen loads both stores in its constructor, so every test has two requests to answer before
 * anything renders. `items` is what the task list answers with.
 *
 * It waits for the rows rather than for a group, and that is not caution: all five groups render
 * whether or not anything is in them, so a wait on a group heading is satisfied by an empty screen
 * and hands the test back before the tasks have arrived. Four assertions were measured passing on
 * nothing that way - the generated client decodes its Blob through FileReader, so a flush only
 * reaches the template a microtask later.
 */
async function open(items: TodoTask[]): Promise<{
  fixture: ComponentFixture<Review>;
  http: HttpTestingController;
}> {
  const fixture = TestBed.createComponent(Review);
  const http = TestBed.inject(HttpTestingController);

  http
    .expectOne('/api/tasks?includeCompleted=false&includeSomeday=true')
    .flush(new Blob([JSON.stringify({ items: items.map((t) => t.toJSON()) })]));
  http.expectOne('/api/review').flush(new Blob([JSON.stringify(status)]));

  // At least one rather than all of them: only the open group renders its rows, so a fixture whose
  // tasks land in different groups will never show them all at once. One row is enough to prove the
  // response arrived, which is the only thing this wait is for.
  if (items.length > 0) {
    await vi.waitFor(() => {
      fixture.detectChanges();
      const rows = (fixture.nativeElement as HTMLElement).querySelectorAll(
        '[data-testid="task-row"]',
      );
      expect(rows.length).toBeGreaterThan(0);
    });
  }

  fixture.detectChanges();

  return { fixture, http };
}

function shown(fixture: ComponentFixture<Review>, selector: string): Promise<HTMLElement> {
  return vi.waitFor(() => {
    fixture.detectChanges();
    const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(selector);
    expect(element).not.toBeNull();
    return element!;
  });
}

function openGroups(fixture: ComponentFixture<Review>): string[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll('[aria-expanded="true"]')].map(
    (el) => el.getAttribute('data-testid') ?? '',
  );
}

describe('Review', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Review, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    });
  });

  // Unlike the settings page, which arrives with nothing open: that one is a reference you go to in
  // order to change one thing, while this is a sequence, and a click to get started is a step with
  // no purpose.
  it('should open the first non-empty group on arrival', async () => {
    const { fixture } = await open([task({ status: TodoStatus.Someday })]);
    await shown(fixture, '[data-testid="someday-review-group"]');

    // Overdue, waiting, starting and no-deadline are all empty, so Someday is the first with
    // anything in it - which also proves the choice is not simply "the first group".
    expect(openGroups(fixture)).toEqual(['someday-review-toggle']);
  });

  it('should have nothing open when every group is empty', async () => {
    const { fixture } = await open([]);
    await shown(fixture, '[data-testid="review-empty"]');

    expect(openGroups(fixture)).toEqual([]);
  });

  it('should close the open group when its heading is clicked again', async () => {
    const { fixture } = await open([task({ bucket: DeadlineBucket.Overdue })]);
    const toggle = await shown(fixture, '[data-testid="overdue-review-toggle"]');

    toggle.click();
    fixture.detectChanges();

    expect(openGroups(fixture)).toEqual([]);
  });

  it('should open only one group at a time', async () => {
    const { fixture } = await open([
      task({ bucket: DeadlineBucket.Overdue }),
      task({ status: TodoStatus.Someday }),
    ]);
    const someday = await shown(fixture, '[data-testid="someday-review-toggle"]');

    someday.click();
    fixture.detectChanges();

    expect(openGroups(fixture)).toEqual(['someday-review-toggle']);
  });

  // The raw list, not `matching()`: a search left running on the task list must not narrow a
  // review, and there is no search field here to undo it with.
  //
  // Worth knowing what actually guards this, because it is not only the test: `matching()` is
  // *private* on TaskStore, so the obvious mistake does not compile - measured, TS2341. This was
  // seen to fail by filtering on `query()` inline in the component instead, which is the shape the
  // bug would have to take now.
  it('should ignore a search left running on the task list', async () => {
    const { fixture } = await open([
      task({ bucket: DeadlineBucket.Overdue, title: 'Ring til banken' }),
    ]);
    await shown(fixture, '[data-testid="overdue-review-group"]');

    TestBed.inject(TaskStore).query.set('harmonika');
    fixture.detectChanges();

    const group = await shown(fixture, '[data-testid="overdue-review-group"]');
    expect(group.querySelectorAll('[data-testid="task-row"]')).toHaveLength(1);
  });

  it('should show every group even when only one has anything in it', async () => {
    const { fixture } = await open([task({ bucket: DeadlineBucket.Overdue })]);
    await shown(fixture, '[data-testid="overdue-review-group"]');

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('[data-testid$="-review-group"]')).toHaveLength(5);
    expect(element.querySelector('[data-testid="review-empty"]')).toBeNull();
  });

  it('should stamp the review when the finish button is pressed', async () => {
    const { fixture, http } = await open([]);
    const done = await shown(fixture, '[data-testid="review-complete"]');

    done.click();

    const request = http.expectOne('/api/review/complete');
    expect(request.request.method).toBe('POST');
    request.flush(
      new Blob([
        JSON.stringify({
          due: false,
          lastCompletedAt: '2026-08-26T09:00:00Z',
          nextDueAt: '2026-09-04T13:00:00Z',
        }),
      ]),
    );

    const last = await shown(fixture, '[data-testid="review-last"]');
    expect(last.textContent).toContain('2026-08-26');
  });
});
