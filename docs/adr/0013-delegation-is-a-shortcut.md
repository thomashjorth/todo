# 13. Delegation is a shortcut, not a state

Date: 2026-08-20
Status: Accepted

## Context

Handing a task to someone else looks like it wants a status of its own. It already has one: a
task with status `WaitingFor` and a name in `WaitingOn` **is** a delegated task. Adding
`Delegated` beside it would mean two ways to say the same thing, and a rule somewhere
deciding which one counts.

## Decision

There is no `Delegated` status, no field on `TaskItem` and no migration. Delegation is three
things, none of them new data:

- a `delegates` setting holding the names you delegate to, stored as JSON in `Setting`
- a shared `<datalist>` those names feed, offered on the "waiting on" field
- the status selector moving focus to that field when a task becomes `WaitingFor`

The list is **suggestion, not constraint**. `waitingOn` stays a free text field, because
"waiting on nobody" and "waiting on someone not on the list" are both valid states today.

Delegation is bookkeeping only. Nobody is notified, and a delegated Jira issue does not
change assignee in Jira. See ADR 14.

## Alternatives rejected

**A `Delegated` status.** A second spelling of a state the model already has.

**A required delegate, chosen from the list.** Would break two states that work today, and
turn a note to yourself into a form to fill in.

**Notifying the other person.** Outside what this app is; see ADR 14.

## Consequences

- Delegating needs no schema change, so it cost a setting and a focus move rather than a
  migration.
- Anyone looking for a delegation status will not find one. That is the intended answer, and
  it is written down so it is not read as an omission.
- The intent to ask "who?" cannot live on the task row, because saving moves the task into a
  different section and destroys that row. It lives in the store instead.
