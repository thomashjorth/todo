import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';

/**
 * The five questions the weekly review asks, in the order it asks them: what have I broken, what do
 * others owe me, what is coming back at me, what is sitting in the wide pile, and what have I still
 * not decided about.
 *
 * A decision order rather than a time axis, which is the whole reason the screen exists next to the
 * task list — see the design's section 1.
 */
export type ReviewGroupName = 'overdue' | 'waiting' | 'startingThisWeek' | 'noDeadline' | 'someday';

export interface ReviewGroup {
  readonly name: ReviewGroupName;
  readonly tasks: readonly TodoTask[];
}

/** How far ahead the tickler looks. A week, because the review runs weekly. */
const windowDays = 7;

/**
 * A day count added to an ISO date, without ever parsing a date string.
 *
 * `new Date('2026-08-26')` is UTC midnight, so west of Greenwich it is already the day before — the
 * trap the deadline field has carried a warning about since slice 1. Splitting into numbers and
 * going through `Date.UTC` keeps the arithmetic on integers, and the result is read back out in UTC
 * too, so nothing ever crosses a zone.
 */
export function isoPlusDays(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number);

  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

/** Today where the user is, as `YYYY-MM-DD`. Local parts, so no zone conversion happens. */
export function localToday(now = new Date()): string {
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}

/**
 * The review's five groups, always all five.
 *
 * Unlike `placeTasks`, which drops the empty ones: there a heading over nothing is noise, here it is
 * an answer. "Nothing is overdue" is one of the things the review is asking, and a group that
 * vanished would be indistinguishable from a group that was never walked.
 *
 * Reads `bucket` — the server's `DeadlineBuckets.For` answer — rather than deriving placement again,
 * so the rule lives in one place. The status filter is the same one `placeTasks` applies and for the
 * same reason: the server buckets by deadline whatever the status, so a parked or waiting task would
 * otherwise show up in a deadline group as well as its own.
 */
export function reviewGroups(tasks: readonly TodoTask[], today: string): ReviewGroup[] {
  const scheduled = tasks.filter(
    (t) => t.status === TodoStatus.Open || t.status === TodoStatus.InProgress,
  );
  const cutoff = isoPlusDays(today, windowDays);

  return [
    { name: 'overdue', tasks: scheduled.filter((t) => t.bucket === DeadlineBucket.Overdue) },
    {
      name: 'waiting',
      // Oldest first: the question this group asks is what has gone stale, so the answer belongs at
      // the top. A missing waitingSince sorts last rather than first - it has not been waiting for
      // a measurable time, so it cannot be the most rotten thing on the list.
      tasks: tasks
        .filter((t) => t.status === TodoStatus.WaitingFor)
        .sort((a, b) => (a.waitingSince ?? '9999').localeCompare(b.waitingSince ?? '9999')),
    },
    {
      name: 'startingThisWeek',
      // ISO dates compare lexicographically the same way they compare chronologically, so the
      // window is a string comparison and no date is ever parsed.
      tasks: scheduled.filter(
        (t) =>
          t.bucket === DeadlineBucket.Deferred &&
          t.deferUntil !== undefined &&
          t.deferUntil <= cutoff,
      ),
    },
    { name: 'noDeadline', tasks: scheduled.filter((t) => t.bucket === DeadlineBucket.NoDeadline) },
    { name: 'someday', tasks: tasks.filter((t) => t.status === TodoStatus.Someday) },
  ];
}
