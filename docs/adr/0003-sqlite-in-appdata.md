# 3. SQLite in %APPDATA%, migrated at startup, with a backup that can refuse

Date: 2026-08-13
Status: Accepted

## Context

The data is one person's real tasks on one machine. There is no server, no replica and no
operator — so a failed migration is **permanent loss**, and nobody is on call to notice.

## Decision

SQLite through EF Core, code-first, with migrations applied at startup by
`TodoDatabase.PrepareAsync`. The database lives at `%APPDATA%\TodoApp\todo.db`, never beside
the executable. The journal is set to **WAL**, because background sync writes while the UI
reads.

When migrations are pending and the file already exists, the app takes a backup first — and
the backup is allowed to **stop the migration**. It folds the write-ahead log back into the
file first, and if another connection holds that off, it throws rather than copying: a backup
missing the newest writes is worse than no backup, because it looks like a safeguard. After
copying it compares sizes and refuses a copy that came out smaller.

## Alternatives rejected

**A JSON or LiteDB file.** Queries like "deadline, then start date, then in-progress first"
are what the app is for, and they want a query language and indexes.

**Migrations run by a separate tool or command.** There is no operator to run it. The app
that opens the file is the only thing that can migrate it.

**A server database.** Single user, single machine, and it has to work offline.

## Consequences

- A migration that cannot be applied leaves the app unable to open. That is the accepted
  trade: refusing to start is recoverable, a half-migrated database is not.
- In WAL mode the database is three files rather than one, so anything that copies or moves
  it has to move all of them.
- Nothing may run against the real database by accident, which is why tests get their own
  file and the data path is a command-line setting. See ADR 5.
- The `.bak-*` files a migration leaves behind are full copies, and from ADR 17 onward that
  makes them as secret as the database itself.
