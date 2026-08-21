using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LongIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Neither EF nor SQLite can convert a TEXT primary key to INTEGER: a CAST of a Guid
            // string reads a leading numeric prefix and otherwise gives 0. Measured on five real
            // Guids, five distinct values became two, of which three were 0 - colliding primary
            // keys. So the ids are remapped explicitly here.
            //
            // Foreign keys are enforced the whole way: PRAGMA foreign_keys is a no-op inside a
            // transaction, and EF wraps the migration in one. The order is therefore load-bearing -
            // parents are inserted before children, children are dropped before parents, and the
            // indexes are created last, because an index follows its table through a RENAME and
            // keeps its name.
            migrationBuilder.Sql("""
                ALTER TABLE Tasks RENAME TO Tasks_old;
                ALTER TABLE SubTasks RENAME TO SubTasks_old;
                ALTER TABLE Aliases RENAME TO Aliases_old;

                CREATE TABLE _TaskIdMap (OldId TEXT NOT NULL PRIMARY KEY, NewId INTEGER NOT NULL);
                INSERT INTO _TaskIdMap (OldId, NewId)
                SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) FROM Tasks_old;

                CREATE TABLE _SubTaskIdMap (OldId TEXT NOT NULL PRIMARY KEY, NewId INTEGER NOT NULL);
                INSERT INTO _SubTaskIdMap (OldId, NewId)
                SELECT s.Id, ROW_NUMBER() OVER (ORDER BY m.NewId, s.SortOrder, s.Id)
                FROM SubTasks_old s JOIN _TaskIdMap m ON m.OldId = s.TaskItemId;

                CREATE TABLE Tasks (
                    Id INTEGER NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY AUTOINCREMENT,
                    SourceId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Note TEXT NULL,
                    Deadline TEXT NULL,
                    Requester TEXT NULL,
                    ExternalKey TEXT NULL,
                    Status TEXT NOT NULL,
                    WaitingOn TEXT NULL,
                    WaitingSince TEXT NULL,
                    DeferUntil TEXT NULL,
                    CompletedAt TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );

                INSERT INTO Tasks (Id, SourceId, Title, Note, Deadline, Requester, ExternalKey,
                                   Status, WaitingOn, WaitingSince, DeferUntil, CompletedAt, CreatedAt)
                SELECT m.NewId, t.SourceId, t.Title, t.Note, t.Deadline, t.Requester, t.ExternalKey,
                       t.Status, t.WaitingOn, t.WaitingSince, t.DeferUntil, t.CompletedAt, t.CreatedAt
                FROM Tasks_old t JOIN _TaskIdMap m ON m.OldId = t.Id;

                CREATE TABLE SubTasks (
                    Id INTEGER NOT NULL CONSTRAINT PK_SubTasks PRIMARY KEY AUTOINCREMENT,
                    TaskItemId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    IsDone INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    CONSTRAINT FK_SubTasks_Tasks_TaskItemId FOREIGN KEY (TaskItemId)
                        REFERENCES Tasks (Id) ON DELETE CASCADE
                );

                INSERT INTO SubTasks (Id, TaskItemId, Title, IsDone, SortOrder)
                SELECT sm.NewId, tm.NewId, s.Title, s.IsDone, s.SortOrder
                FROM SubTasks_old s
                JOIN _SubTaskIdMap sm ON sm.OldId = s.Id
                JOIN _TaskIdMap tm ON tm.OldId = s.TaskItemId;

                CREATE TABLE Aliases (
                    Id INTEGER NOT NULL CONSTRAINT PK_Aliases PRIMARY KEY AUTOINCREMENT,
                    Value TEXT NOT NULL
                );

                INSERT INTO Aliases (Id, Value)
                SELECT ROW_NUMBER() OVER (ORDER BY Value), Value FROM Aliases_old;

                DROP TABLE SubTasks_old;
                DROP TABLE Tasks_old;
                DROP TABLE Aliases_old;
                DROP TABLE _SubTaskIdMap;
                DROP TABLE _TaskIdMap;

                CREATE INDEX IX_Tasks_Deadline ON Tasks (Deadline);
                CREATE INDEX IX_Tasks_SourceId_ExternalKey ON Tasks (SourceId, ExternalKey);
                CREATE INDEX IX_SubTasks_TaskItemId ON SubTasks (TaskItemId);
                CREATE UNIQUE INDEX IX_Aliases_Value ON Aliases (Value);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror image of Up. SQLite has no uuid(), so the ids are built from randomblob -
            // measured: 200 values, 200 distinct, all 36 characters and valid version 4 UUIDs.
            // A rollback therefore preserves the *rows* but not the *identities*: every task,
            // subtask and alias gets a new Guid. A link or a stored reference to an old id no
            // longer points at anything.
            //
            // The same load-bearing order as in Up, for the same reason: foreign keys are enforced,
            // so parents in before children, children out before parents, and the indexes last.
            migrationBuilder.Sql("""
                ALTER TABLE Tasks RENAME TO Tasks_old;
                ALTER TABLE SubTasks RENAME TO SubTasks_old;
                ALTER TABLE Aliases RENAME TO Aliases_old;

                CREATE TABLE _TaskIdMap (OldId INTEGER NOT NULL PRIMARY KEY, NewId TEXT NOT NULL);
                INSERT INTO _TaskIdMap (OldId, NewId)
                SELECT Id,
                       lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                        substr(hex(randomblob(2)), 2) || '-' ||
                        substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
                        '-' || hex(randomblob(6)))
                FROM Tasks_old;

                CREATE TABLE _SubTaskIdMap (OldId INTEGER NOT NULL PRIMARY KEY, NewId TEXT NOT NULL);
                INSERT INTO _SubTaskIdMap (OldId, NewId)
                SELECT Id,
                       lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                        substr(hex(randomblob(2)), 2) || '-' ||
                        substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
                        '-' || hex(randomblob(6)))
                FROM SubTasks_old;

                CREATE TABLE Tasks (
                    Id TEXT NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY,
                    SourceId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Note TEXT NULL,
                    Deadline TEXT NULL,
                    Requester TEXT NULL,
                    ExternalKey TEXT NULL,
                    Status TEXT NOT NULL,
                    WaitingOn TEXT NULL,
                    WaitingSince TEXT NULL,
                    DeferUntil TEXT NULL,
                    CompletedAt TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );

                INSERT INTO Tasks (Id, SourceId, Title, Note, Deadline, Requester, ExternalKey,
                                   Status, WaitingOn, WaitingSince, DeferUntil, CompletedAt, CreatedAt)
                SELECT m.NewId, t.SourceId, t.Title, t.Note, t.Deadline, t.Requester, t.ExternalKey,
                       t.Status, t.WaitingOn, t.WaitingSince, t.DeferUntil, t.CompletedAt, t.CreatedAt
                FROM Tasks_old t JOIN _TaskIdMap m ON m.OldId = t.Id;

                CREATE TABLE SubTasks (
                    Id TEXT NOT NULL CONSTRAINT PK_SubTasks PRIMARY KEY,
                    TaskItemId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    IsDone INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    CONSTRAINT FK_SubTasks_Tasks_TaskItemId FOREIGN KEY (TaskItemId)
                        REFERENCES Tasks (Id) ON DELETE CASCADE
                );

                INSERT INTO SubTasks (Id, TaskItemId, Title, IsDone, SortOrder)
                SELECT sm.NewId, tm.NewId, s.Title, s.IsDone, s.SortOrder
                FROM SubTasks_old s
                JOIN _SubTaskIdMap sm ON sm.OldId = s.Id
                JOIN _TaskIdMap tm ON tm.OldId = s.TaskItemId;

                CREATE TABLE Aliases (
                    Id TEXT NOT NULL CONSTRAINT PK_Aliases PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                INSERT INTO Aliases (Id, Value)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
                        substr(hex(randomblob(2)), 2) || '-' ||
                        substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
                        '-' || hex(randomblob(6))),
                       Value
                FROM Aliases_old;

                DROP TABLE SubTasks_old;
                DROP TABLE Tasks_old;
                DROP TABLE Aliases_old;
                DROP TABLE _SubTaskIdMap;
                DROP TABLE _TaskIdMap;

                CREATE INDEX IX_Tasks_Deadline ON Tasks (Deadline);
                CREATE INDEX IX_Tasks_SourceId_ExternalKey ON Tasks (SourceId, ExternalKey);
                CREATE INDEX IX_SubTasks_TaskItemId ON SubTasks (TaskItemId);
                CREATE UNIQUE INDEX IX_Aliases_Value ON Aliases (Value);
                """);
        }
    }
}
