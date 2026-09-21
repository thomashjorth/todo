# 12. Derived state is computed, never stored

Date: 2026-08-17
Status: Accepted

## Context

There is no background process. The app is a window the user opens; when it is shut, nothing
of it is running. Anything that depends on a moment arriving — midnight, a deadline passing,
a deferral expiring — would have to be re-derived on the next start anyway, so storing it
buys a second copy that can disagree with the first.

## Decision

State that follows from data plus the clock is computed at read time.

- **Deadline sections** come from `DeadlineBuckets.For(deadline, deferUntil, today)`, a pure
  function taking an injected clock.
- **Deferral is not a status.** There is no `Deferred` value on `TodoStatus` and no stored
  flag. A task whose start date lies strictly after today falls in the deferred section
  because the clock says so, and returns tomorrow because the clock says something else.
- **Selection is a `computed`, not an effect.** `TaskList.selected` is
  `selectable.find(id) ?? (wide ? selectable[0] : undefined)` — auto-selection on load, the
  selection following when its task is filtered away or deleted, and auto-selection applying
  only side by side are all one expression rather than six call sites.

Where two computed answers contradict each other, the branch order is the decision.
`Overdue` beats `Deferred`: a task can have both a future start date and a deadline that has
already passed, and hiding a commitment already missed is worse than showing something
earlier than planned.

## Alternatives rejected

**A stored bucket, kept fresh by a job.** Requires something to be running, which nothing is.

**A `Deferred` status on the task.** Makes the model carry a fact the calendar already knows,
and puts the app one missed run away from showing the wrong section.

## Consequences

- Nothing has to run at midnight, and the app survives being closed for three weeks.
- The clock is a dependency rather than ambient, so every one of these rules is testable
  without waiting.
- Branch order in these functions is load-bearing, and a reordering compiles and reads
  correctly. It is held by tests naming the specific pair, not by the type system.
