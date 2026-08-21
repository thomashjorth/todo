# Todo

Personal todo app. Design: `docs/plans/2026-08-13-todo-app-design.md`.

## Getting started

```
Todo.cmd
```

Builds Angular if the sources are newer than `wwwroot`, then opens the window. Pass `--headless`
to run without one. To do it by hand:

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet run --project src\Todo.Host
```

## Development with hot reload

Terminal 1:

```
dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199
```

Terminal 2:

```
npm.cmd start --prefix src\Todo.Web
```

Open http://localhost:4200. Use `npm.cmd`, not `npm` - the PowerShell shim is broken.

## Retro import

The screen is called **Retro-import** in the top menu and lives on the `/import` route. Paste the
CSV export from the retro board, press **Analysér**, pick the rows to bring in, and press
**Importér**. Rating cards - rows whose content is just a number like `8` or `9/10` - never become
tasks, and the screen says how many it skipped.

Your aliases decide what is yours: a row is yours when `Action Owner` matches one of the names under
"Hvem er du på boardet?". Those are preselected, and a leading `"NAME - "` is stripped from the
title. Add an alias and the list is re-analysed straight away - you do not have to paste the export
again. If you did not take part in the retro, none of the rows are yours, and the screen says so
rather than just sitting empty.

Importing the same board again is safe. Every row is recognised by `Content` + `Zone` + `Author` +
`Created`, so a row you have already imported shows as "imported before" and cannot be picked again.

## Notes in markdown

A task's note is written in markdown and shown rendered - bold, bullet lists, links, code blocks and
tables. Click the note to edit it; the rendered text swaps places with a text box holding the raw
markdown. **Esc** saves and closes, and so does a click outside the box. There is no save button. An
empty note shows a line instead, and that line can be clicked exactly like a note with content.

Clicking a link in a note opens it in the system browser rather than inside the window. The app
window has no address bar, so a link followed inside it could not be navigated back from. Only
`http` and `https` are opened - a link to anything else would let a note start a program on the
machine - and if one is refused, the reason appears under the note.

## Waiting for and Someday

Two statuses take a task out of the deadline sections without losing it.

**Venter på** is for what you have handed over and somebody else has to answer. Pick that status and
a field appears for who you are waiting on, and the task moves down into its own **Venter på**
section. It keeps showing up - that is the whole point - and the line under the title counts how
long: `0 dage` on the day you set the status, `12 dage` twelve days later. The counter starts when
you set the status, not when the task was created, and it does not move because you edited something
else on the task meanwhile. Set the task back to Open and both the name and the date are forgotten;
if it starts waiting again, the count starts over from zero.

**Måske** is the opposite: it hides. A parked task disappears from the list and comes back only
under **Måske**, when you turn **Vis måske** on. That keeps the list short without deleting
anything.

## Language and settings

The screen is called **Indstillinger** in the top menu and lives on the `/settings` route. It holds
five groups, folded so that at most one is open at a time: general, delegating, Jira import, Azure
DevOps import and retro import.

The language has three choices: **Følg systemet**, **Dansk** and **Engelsk**. "Follow the system" is
the default - the language is then read from the browser's `navigator.language`, and anything that
is not Danish becomes English. A choice takes effect immediately, without the page reloading.

The choice is stored in the database rather than in the browser, so it survives a restart of the
app. Pick "Follow the system" again and the setting is deleted rather than stored as a value.

Deadlines are written in the active language - `14. aug. 2026` against `Aug 14, 2026`. The date is
built from the three numbers in `yyyy-MM-dd` and never by letting a `Date` interpret the string:
`new Date("2026-08-14")` is midnight UTC and would read as the 13th west of Greenwich.

**Start with Windows** is in the general group. It writes the app's own path under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, per user rather than per machine, and the
switch reads that key back rather than anything the app stored - so removing the entry with another
tool turns the switch off. There is no tray icon yet, so the app opens a window at sign-in.

## Tests

Everything, in the order that matters:

```
Check.cmd
```

The order is load-bearing: the E2E suite has no build step of its own, so Angular has to be built
first or Playwright tests the previous frontend without anything looking wrong. The steps on their
own:

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test Todo.sln
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

The first E2E run downloads Chromium and therefore takes a few minutes.

Test data is arranged with the builders in `tests\Todo.TestSupport\Builders`:

```csharp
await host.AddAndSaveChangesAsync(
    new TaskItemBuilder().Titled("Buy coffee").DueToday().RequestedBy("Anna").Build(),
    UserAliases.Named("Thomas Hjorth Hansen"));
```

They write straight to the database and so bypass the API's validation. Use them to arrange a state
- never to perform the action a test is meant to verify.

E2E tests navigate through `TodoApp` in `tests\Todo.E2E`, which waits for the screen to be drawn
before handing the test a screen object. Each screen object owns its own `data-testid` selectors, so
renaming something in markup hits one file.

## Publishing

```
Publish.cmd
```

Publishes a self-contained exe to `publish\` - two files, `Todo.Host.exe` and `icon.ico` - and then
probes it: starts it headless on a free port against a throwaway database and requires the frontend,
the health route and the documentation page to answer. `publish\` is gitignored. Pass
`-OutputPath <folder>` to install somewhere permanent.

The frontend is embedded in the assembly, so Angular has to be built before the host and the host
rebuilt after; the script does both. The published app needs the WebView2 runtime, which ships with
Edge on Windows 11.

## The database

The database lives in `%APPDATA%\TodoApp\todo.db`. Migrations run at startup; if any are pending, a
copy is taken first as `todo.db.bak-<timestamp>`. To reset everything, delete the file.

A new migration:

```
dotnet tool run dotnet-ef migrations add <Name> --project src\Todo.Core --startup-project src\Todo.Host
```

Use `dotnet tool run dotnet-ef`, not `dotnet ef` - the globally installed `dotnet-ef` 7.0.16 cannot
read an EF Core 10 model.

## The contract

`contracts/openapi.yaml` owns the API. Change it and run `scripts\generate-api.ps1`, then commit the
generated code - otherwise `GeneratedCodeFreshnessTests` fails.

## Styling

All styling is standard Tailwind utility classes. No CSS or SCSS is written in this project. The app
is used in a column of about 480 px, which is below Tailwind's `sm` breakpoint - the unprefixed
classes are the narrow version, and `sm:`/`md:` are only used to widen.

## Language of the source

Everything in this repository is written in English: code, comments, scripts and this file. The
exception is the app's own user-facing text, which lives in `src\Todo.Web\public\i18n\da.json` and
`en.json` - Danish is the source there and English the translation. Danish words appearing above are
screen names and UI strings quoted from those files.
