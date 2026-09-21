# 16. The weekly review is owed by calculation, not by a timer

Date: 2026-08-27
Status: Accepted

## Context

The review is a habit: a weekday and a time of day at which you look through everything.
Implementing it as something that *fires* would mean something has to be running at that
moment — and nothing is. The app has no tray icon and no background sync, so a moment that
arrives on Friday at 15:00 hits nothing when the window is shut.

## Decision

`ReviewSchedule.For(weekday, timeOfDay, lastCompletedAt, clock)` computes whether the review
is owed, from the chosen weekday and time, the last completion and the clock. Nothing is
scheduled and nothing runs at midnight. The same rule as ADR 12, for the same reason.

The cadence is a **fixed weekday**, not seven days since the last review. A review taken on
Tuesday satisfies the period that is running, so the marker clears; when Friday 15:00 comes
round, the boundary has moved past Tuesday and it is owed again. A seven-day rule would let
the habit slide a day forward every week until the chosen weekday stopped meaning anything.

The arithmetic runs in **local time** and converts to UTC only to compare. "Friday 15:00" is
a statement about the user's afternoon, while the stored completion is UTC by the repository's
convention — so `IClock` exposes `LocalNow` for exactly this.

## Alternatives rejected

**A timer or scheduled job.** Nothing is running to receive it.

**Seven days since the last review.** Drifts the habit off the weekday the user chose.

**Storing "review is due" as a flag.** Would need something to set it, which is the problem
again.

## Consequences

- The state survives the app being closed for three weeks: the answer is recomputed from the
  calendar on the next start rather than depending on runs that never happened.
- A missing `lastCompletedAt` reads as owed without a branch of its own, because the computed
  boundary is never in the future.
- The default stores no row at all: a weekday and time only exist in `Setting` once the user
  changes them.
