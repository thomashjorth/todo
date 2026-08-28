import { describe, expect, it } from 'vitest';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { isoPlusDays, reviewGroups } from './review-groups';

const today = '2026-08-26';

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

describe('reviewGroups', () => {
  it('should keep the five groups in review order, empty ones included', () => {
    expect(reviewGroups([], today).map((g) => g.name)).toEqual([
      'overdue',
      'waiting',
      'startingThisWeek',
      'noDeadline',
      'someday',
    ]);
  });

  // Unlike placeTasks, which drops empty groups: there a heading over nothing would be noise, here
  // it is the answer. "Nothing is overdue" is a thing the review is asking, and a group that
  // vanished would leave the reader unable to tell it from a group that was never walked.
  it('should keep an empty group rather than dropping it', () => {
    const groups = reviewGroups([task({ bucket: DeadlineBucket.NoDeadline })], today);

    expect(groups).toHaveLength(5);
    expect(groups.find((g) => g.name === 'overdue')!.tasks).toEqual([]);
  });

  it('should put an overdue task in the first group and nowhere else', () => {
    const overdue = task({ bucket: DeadlineBucket.Overdue, deadline: '2026-08-20' });

    const groups = reviewGroups([overdue], today);

    expect(groups.find((g) => g.name === 'overdue')!.tasks).toEqual([overdue]);
    expect(groups.filter((g) => g.tasks.length > 0)).toHaveLength(1);
  });

  it('should list the tasks somebody else owes oldest first', () => {
    const recent = task({
      status: TodoStatus.WaitingFor,
      waitingSince: '2026-08-24T09:00:00Z',
      title: 'Recent',
    });
    const ancient = task({
      status: TodoStatus.WaitingFor,
      waitingSince: '2026-07-01T09:00:00Z',
      title: 'Ancient',
    });

    const waiting = reviewGroups([recent, ancient], today).find((g) => g.name === 'waiting')!;

    expect(waiting.tasks.map((t) => t.title)).toEqual(['Ancient', 'Recent']);
  });

  // The tickler shows something as it approaches rather than all the time. A task three weeks out
  // gets looked at on the review where it has come closer - and if the window were widened to the
  // whole bucket, the group would be a second Someday list.
  it('should leave a task that starts in three weeks out of this week', () => {
    const soon = task({ bucket: DeadlineBucket.Deferred, deferUntil: '2026-08-30' });
    const later = task({ bucket: DeadlineBucket.Deferred, deferUntil: '2026-09-16' });

    const group = reviewGroups([soon, later], today).find((g) => g.name === 'startingThisWeek')!;

    expect(group.tasks).toEqual([soon]);
  });

  it('should include a task starting exactly seven days out', () => {
    const edge = task({ bucket: DeadlineBucket.Deferred, deferUntil: '2026-09-02' });

    expect(reviewGroups([edge], today).find((g) => g.name === 'startingThisWeek')!.tasks).toEqual([
      edge,
    ]);
  });

  it('should put a parked task in the last group', () => {
    const parked = task({ status: TodoStatus.Someday, bucket: DeadlineBucket.Today });

    const groups = reviewGroups([parked], today);

    expect(groups.find((g) => g.name === 'someday')!.tasks).toEqual([parked]);
    // The server buckets by deadline whatever the status, so without the status filter this one
    // would show up under a deadline group as well.
    expect(groups.find((g) => g.name === 'noDeadline')!.tasks).toEqual([]);
  });

  it('should leave a finished task out of every group', () => {
    const done = task({ status: TodoStatus.Done, bucket: DeadlineBucket.Overdue });

    expect(reviewGroups([done], today).every((g) => g.tasks.length === 0)).toBe(true);
  });
});

describe('isoPlusDays', () => {
  it('should carry across a month end', () => {
    expect(isoPlusDays('2026-08-26', 7)).toBe('2026-09-02');
  });

  // Built from numbers rather than parsed from the string: `new Date('2026-08-26')` is UTC
  // midnight, which in a negative offset is the day before - the trap the deadline field has
  // carried a warning about since slice 1.
  it('should carry across a year end', () => {
    expect(isoPlusDays('2026-12-30', 7)).toBe('2027-01-06');
  });

  it('should carry across a leap day', () => {
    expect(isoPlusDays('2028-02-26', 7)).toBe('2028-03-04');
  });
});
