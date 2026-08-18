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
            // Hverken EF eller SQLite kan konvertere en TEXT-primærnøgle til INTEGER: en CAST af
            // en Guid-streng læser et ledende tal-præfiks og giver ellers 0. Målt på fem rigtige
            // Guid'er blev fem distinkte værdier to, hvoraf tre var 0 — altså sammenfaldende
            // primærnøgler. Derfor ommappes id'erne eksplicit her.
            //
            // Fremmednøgler er slået til hele vejen: PRAGMA foreign_keys er en no-op inde i en
            // transaktion, og EF pakker migreringen i én. Rækkefølgen er derfor bærende —
            // forældre indsættes før børn, børn droppes før forældre, og indeksene oprettes til
            // sidst, fordi et indeks følger sin tabel gennem RENAME og beholder sit navn.
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
            // Spejlbilledet af Up. SQLite har ingen uuid(), så id'erne bygges af randomblob —
            // målt: 200 værdier, 200 distinkte, alle 36 tegn og gyldige version 4-UUID'er.
            // Derfor bevarer en tilbagerulning *rækkerne*, men ikke *identiteterne*: hver
            // opgave, underopgave og alias får et nyt Guid. Et link eller en gemt reference til
            // et gammelt id peger ikke længere på noget.
            //
            // Samme bærende rækkefølge som i Up, af samme grund: fremmednøgler er slået til,
            // så forældre ind før børn, børn ud før forældre, og indeksene til sidst.
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
