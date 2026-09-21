# 14. Read external, enrich locally — nothing is written back

Date: 2026-08-13
Status: Accepted

## Context

Jira and Azure DevOps hold the organisation's work. This app is one person's private view
over it, built for that person alone.

Three things point the same way, and the third is the one that settles it.

**Ownership.** Jira and ADO are the systems of record. If this app could write, the truth
about an issue would live in two places, and something would have to decide which one wins.

**Blast radius.** A read that goes wrong is wrong on one machine, in one window, for one
person. A write that goes wrong lands in a colleague's issue, in a shared board, in someone
else's notification feed.

**How the code was written.** Much of this app is vibe-coded — generated at speed, reviewed
by one person, with no second reviewer and no staging environment between the keyboard and
the live instance. That is a perfectly reasonable way to build a personal tool, and an
unreasonable way to acquire write access to a shared work tracker. The architecture is
therefore built so that the worst case is contained to the author's own machine.

## Decision

Every call to Jira and Azure DevOps is a read. External fields on an imported task are
read-only locally: the app enriches them with its own deadline, note, subtasks and status,
and never sends any of it back.

The one `POST` in either source is a WIQL query — a read expressed as a POST because the
query does not fit in a URL. There is no request anywhere that creates, updates, transitions
or reassigns anything in an external system.

This is why a delegated Jira issue does not change assignee in Jira (ADR 13), and why an
import that closes a task closes the **local** task only.

## Alternatives rejected

**Two-way sync.** Conflict resolution, permissions, audit trails and a bug class that reaches
other people.

**Write just one thing — completing an issue when the task is completed.** The smallest
version of the same exposure, and it would set the precedent that the boundary is negotiable.

## Consequences

- The access token only ever needs read scope. That matters, because it is stored in clear
  text. See ADR 17.
- Closing the loop is manual: an issue resolved in Jira becomes a *suggestion* to close the
  local task, which the user accepts.
- The app can be wrong about external state without consequence for anyone else, which is
  what makes it safe to iterate on quickly.
- An external system that changes under the app degrades the view rather than corrupting
  data.
