# 4. Identifiers are `long`, assigned by SQLite

Date: 2026-08-18
Status: Accepted, replaces the `Guid` identifiers used in slices 0–9

## Context

The app started with `Guid` primary keys. In use, the cost showed up in conversation rather
than in code: you cannot say a `Guid` out loud, and "task 42" is a thing the author wanted to
be able to say — to himself, in a log line, in a note.

The industry argument against random keys is index fragmentation. That argument is real, but
its answer is time-ordered UUIDv7, not `long`. It is therefore explicitly *not* the reason
here, and saying so keeps anyone from rediscovering the wrong rationale.

## Decision

`Id` on `TaskItem`, `SubTask` and `UserAlias` is `long`, declared as
`INTEGER PRIMARY KEY AUTOINCREMENT` — an alias for SQLite's rowid. The database assigns it.
Entities never set an id and the client never sends one.

The `LongIds` migration is **hand-written**, not scaffolded. A `CAST` from a GUID string to an
integer reads a leading numeric prefix and otherwise yields `0`, which collapses distinct rows
onto colliding keys; EF's own scaffold produced exactly that and warned about data loss. The
migration rebuilds each table with a mapping table and `ROW_NUMBER()` instead.

## Alternatives rejected

**Keep `Guid` v4.** Unspeakable, and the original complaint.

**Move to UUIDv7.** Solves fragmentation, which was never the problem here, and leaves the
identifier just as unsayable.

**`int` rather than `long`.** No meaningful saving in SQLite, where the column is a 64-bit
rowid either way.

## Consequences

- Identifiers are small, ordered and quotable, which is what the change was for.
- They are also guessable and dense. That is acceptable because the API is bound to loopback
  and serves one user; it would not be on a shared server.
- A hand-written type migration cannot lean on the scaffold or the model snapshot to prove it
  is correct, so it carries its own guards comparing the database against the model. See
  ADR 20.
- The identifier is written in two places that do not check each other — the C# type and the
  route template's constraint — so it is changed in both or not at all.
