# Consolidation, delivery 1: one language — implementation plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task.

**Goal:** Every code comment in the repository is English, and a guard keeps it that way.

**Architecture:** A ratchet. `SourceLanguageTests` classifies each comment block as Danish or English
by scoring function words, and carries a backlog of the 25 files that are still Danish. Nothing on
the backlog fails; anything off it does. Each commit translates a group of files and removes them
from the backlog, so **every commit is green** and the guard can never go backwards. A second
assertion requires every backlogged file to still contain Danish, so a translated file cannot be
left on the list.

**Tech stack:** xUnit in `Todo.Api.Tests`, `ExternalCommand.RunAsync` + `git ls-files` for the file
listing (the same shape as `LineEndingTests`), no new dependencies.

**Design document:** `docs/plans/2026-09-08-consolidation-design.md`, sections 6, 8 and 9.

---

## What was measured before this plan was written

All on 2026-09-08 against `580d6d8`.

- **25 files hold 140 Danish comment blocks.** The exact list is Task 1's backlog.
- **A bare `æøå` test does not work.** Three files — `src/Todo.Host/Jira/JiraTaskSource.cs`,
  `tests/Todo.Api.Tests/AdoTaskSourceTests.cs`, `tests/Todo.Api.Tests/JiraTaskSourceTests.cs` — carry
  **English** comments whose subject is Danish collation, so they must spell the letters out. A
  character test flags all three. **Scoring the function words does not**, and that is why the guard
  is built the way it is. If you simplify it to a character match, you will "fix" three comments that
  are already correct.
- **Five spec files trip a character test the same way** and are also correctly English:
  `ado-import.spec.ts`, `app.spec.ts`, `jira-import.spec.ts`, `settings.spec.ts`,
  `settings-store.spec.ts`. They score en=13–67 against da=1–13.
- **There are no Danish identifiers anywhere** — not one method, class, variable or `data-testid`.
  The Danish in C# is string literals asserting on the app's Danish surface. **They stay.**
- **Nine comments in seven files name Tailwind utility classes** — `min-h-0`, `xl:overflow-y-auto`,
  `dark:bg-gray-800`, `outline-2`, `outline-offset-2`, `max-w-2xl`, `divide-y`, `gap-6`, `px-2`.
  Tailwind extracts candidates from raw text, comments included, so **a rewritten comment can change
  the CSS bundle.** `min-h-0` is generated today *only* because two comments name it. Task 8 checks.
- **Prettier is `printWidth: 100`** and `FrontendFormattingTests` runs it inside `dotnet test`. A
  translated comment that reflows past 100 characters turns the suite red.

---

## Task 1: The guard, with the backlog full

**Files:**
- Create: `tests/Todo.Api.Tests/SourceLanguageTests.cs`

**Step 1: Write the guard**

```csharp
using System.Text.RegularExpressions;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The repository's comments were Danish in the oldest code and English in the newest, and nothing
/// enforced either. This is that rule, written down once: comments are English, and the Danish
/// string literals asserting on the app's Danish surface are not comments and are not touched.
///
/// <b>Why this scores words instead of looking for a letter.</b> Three files carry English comments
/// whose subject is Danish collation - <c>JiraTaskSource</c> and the two task-source test classes
/// explain that ae, oe and aa sort after z - so they must spell the letters out. A character test
/// flags all three and would have "fixed" prose that was already correct. Prose is decided by its
/// function words, so the function words are what get counted.
///
/// <b>Why there is a backlog.</b> Twenty-five files were Danish when this was written. Translating
/// them in one commit would be unreviewable, and committing a red test would break
/// <c>Check.cmd</c> for everyone in between. So the list starts full and shrinks: a file on it may
/// be Danish, a file off it may not. <see cref="Every_backlogged_file_is_still_Danish"/> is what
/// stops the list from rotting - remove a file from the backlog in the same commit that translates
/// it, or the suite says so.
/// </summary>
public class SourceLanguageTests
{
    /// <summary>
    /// Files still to translate. Delete an entry in the same commit that translates it. When this
    /// is empty, delete it and <see cref="Every_backlogged_file_is_still_Danish"/> with it.
    /// </summary>
    private static readonly HashSet<string> Backlog = new(StringComparer.Ordinal)
    {
        "src/Todo.Web/src/app/ado/ado-import.html",
        "src/Todo.Web/src/app/ado/ado-import.ts",
        "src/Todo.Web/src/app/app.html",
        "src/Todo.Web/src/app/jira/jira-import.html",
        "src/Todo.Web/src/app/jira/jira-import.ts",
        "src/Todo.Web/src/app/retro/retro-import.ts",
        "src/Todo.Web/src/app/review/review.html",
        "src/Todo.Web/src/app/settings/settings.html",
        "src/Todo.Web/src/app/shortcuts/shortcut-key.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut-key.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut-store.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut.ts",
        "src/Todo.Web/src/app/tasks/task-detail.html",
        "src/Todo.Web/src/app/tasks/task-detail.spec.ts",
        "src/Todo.Web/src/app/tasks/task-detail.ts",
        "src/Todo.Web/src/app/tasks/task-list.html",
        "src/Todo.Web/src/app/tasks/task-list.spec.ts",
        "src/Todo.Web/src/app/tasks/task-list.ts",
        "src/Todo.Web/src/app/tasks/task-row.html",
        "src/Todo.Web/src/app/tasks/task-row.ts",
        "src/Todo.Web/src/app/tasks/task-store.spec.ts",
        "src/Todo.Web/src/index.html",
        "tests/Todo.Api.Tests/LongIdMigrationTests.cs",
        "tests/Todo.E2E/TaskListScreen.cs",
    };

    /// <summary>
    /// Generated code is not ours to write in either language, and the EF migrations are scaffolded.
    /// </summary>
    private static readonly string[] Generated =
    [
        "src/Todo.Web/src/app/api/todo-client.ts",
        "src/Todo.Contracts/Generated/Contracts.g.cs",
    ];

    private static readonly string[] DanishWords =
    [
        "ikke", "fordi", "derfor", "skal", "kun", "hver", "denne", "dette", "disse", "bliver",
        "blev", "altid", "aldrig", "mens", "hvis", "hvad", "hvor", "samme", "andre", "noget",
        "nogen", "uden", "inden", "siden", "mellem", "gennem", "sammen", "selv", "stadig",
        "ellers", "betyder", "frem", "netop", "og", "den", "det", "som", "der", "men", "til",
        "med", "er", "har", "kan", "vi", "man", "så", "også", "altså", "når", "står", "går",
        "ligger", "findes", "sætter", "læser", "skriver", "hedder", "både", "for",
    ];

    private static readonly string[] EnglishWords =
    [
        "the", "and", "is", "are", "this", "that", "these", "those", "because", "only", "must",
        "should", "when", "each", "with", "without", "have", "has", "can", "could", "would",
        "will", "was", "were", "their", "there", "here", "they", "them", "from", "into", "over",
        "under", "about", "after", "before", "while", "which", "what", "where", "why", "how",
        "does", "did", "not", "but", "for", "its", "an", "of", "to", "be", "on", "at", "as",
        "by", "so", "if", "then", "than",
    ];

    private static readonly Regex Danish = WordsIn(DanishWords);
    private static readonly Regex English = WordsIn(EnglishWords);
    private static readonly Regex DanishLetter = new("[æøåÆØÅ]", RegexOptions.Compiled);

    [Fact]
    public async Task No_comment_outside_the_backlog_is_written_in_Danish()
    {
        var offenders = new List<string>();

        foreach (var file in await SourceFilesAsync())
        {
            if (Backlog.Contains(file))
            {
                continue;
            }

            offenders.AddRange(DanishCommentsIn(file)
                .Select(found => $"{file}:{found.Line}  {found.Text}"));
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} comment block(s) are written in Danish. Comments are English in "
                + "this repository; the Danish string literals that assert on the app's Danish "
                + "surface are not comments and are fine. If a comment is English but discusses "
                + "Danish letters, put the letters in `backticks`, \"quotes\" or <c>tags</c>:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Without this the backlog rots: a file translated but left on the list would go on excusing
    /// itself, and the guard would quietly cover less than the list claims.
    /// </summary>
    [Fact]
    public async Task Every_backlogged_file_is_still_Danish()
    {
        var tracked = (await SourceFilesAsync()).ToHashSet(StringComparer.Ordinal);

        var stale = Backlog
            .Select(file => !tracked.Contains(file)
                ? $"{file} is on the backlog but git does not track it"
                : DanishCommentsIn(file).Count == 0
                    ? $"{file} is on the backlog but holds no Danish comment any more"
                    : null)
            .OfType<string>()
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"{stale.Count} backlog entr(ies) are stale. Remove a file from Backlog in the same "
                + "commit that translates it, so the list keeps meaning what it says:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
    }

    private sealed record Comment(int Line, string Text);

    private static List<Comment> DanishCommentsIn(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, relativePath));

        return CommentsIn(relativePath, text).Where(IsDanish).ToList();
    }

    /// <summary>
    /// Consecutive <c>//</c> lines are one block. A single line is too short to score: "// so far"
    /// is two English words and no Danish ones, and a Danish line can be just as thin.
    /// </summary>
    private static List<Comment> CommentsIn(string path, string text)
    {
        var found = new List<Comment>();

        if (path.EndsWith(".html", StringComparison.Ordinal))
        {
            foreach (Match match in Regex.Matches(text, "<!--(.*?)-->", RegexOptions.Singleline))
            {
                found.Add(new Comment(LineOf(text, match.Index), match.Groups[1].Value));
            }

            return found;
        }

        var code = StripStringLiterals(path, text);
        var lines = code.Split('\n');
        var run = new List<string>();
        var start = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "(?<!:)//(.*)");

            if (match.Success)
            {
                if (run.Count == 0)
                {
                    start = i + 1;
                }

                run.Add(match.Groups[1].Value);
            }
            else if (run.Count > 0)
            {
                found.Add(new Comment(start, string.Join(' ', run)));
                run.Clear();
            }
        }

        if (run.Count > 0)
        {
            found.Add(new Comment(start, string.Join(' ', run)));
        }

        foreach (Match match in Regex.Matches(code, @"/\*(.*?)\*/", RegexOptions.Singleline))
        {
            found.Add(new Comment(LineOf(code, match.Index), match.Groups[1].Value));
        }

        return found;
    }

    /// <summary>
    /// String literals hold the app's Danish surface - <c>"Opgavestiller: Mette Kirkegaard"</c> and
    /// its kin - and scoring them would make the guard forbid the one thing that has to stay.
    /// </summary>
    private static string StripStringLiterals(string path, string text)
    {
        if (path.EndsWith(".cs", StringComparison.Ordinal))
        {
            text = Regex.Replace(text, "@\"(?:[^\"]|\"\")*\"", "\"\"");

            return Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        }

        text = Regex.Replace(text, "`(?:\\\\.|[^`\\\\])*`", "``");
        text = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");

        return Regex.Replace(text, "'(?:\\\\.|[^'\\\\])*'", "''");
    }

    private static bool IsDanish(Comment comment)
    {
        var body = Unquoted(comment.Text);
        var danish = Danish.Matches(body).Count;
        var english = English.Matches(body).Count;

        if (danish == 0 && !DanishLetter.IsMatch(body))
        {
            return false;
        }

        return danish > english;
    }

    /// <summary>
    /// Quoted text inside a comment is the app's own wording being cited, not prose to judge.
    /// </summary>
    private static string Unquoted(string comment)
    {
        comment = Regex.Replace(comment, "<c>.*?</c>", string.Empty, RegexOptions.Singleline);
        comment = Regex.Replace(comment, "`[^`]*`", string.Empty);

        return Regex.Replace(comment, "\"[^\"]*\"", string.Empty);
    }

    private static int LineOf(string text, int index) =>
        text.Take(index).Count(character => character == '\n') + 1;

    private static Regex WordsIn(string[] words) =>
        new(
            @"\b(" + string.Join('|', words.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task<List<string>> SourceFilesAsync()
    {
        var listing = await ExternalCommand.RunAsync(
            "git", RepoPaths.Root, ["ls-files", "*.cs", "*.ts", "*.html"]);

        Assert.True(
            listing.ExitCode == 0,
            $"git ls-files exited with {listing.ExitCode}. It said:"
                + Environment.NewLine
                + listing.Combined);

        var files = listing.Lines
            .Where(path => path.Length > 0)
            .Where(path => !Generated.Contains(path, StringComparer.Ordinal))
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .ToList();

        // A parsing or working-directory problem answers nothing and would pass both assertions.
        Assert.True(
            files.Count > 100,
            $"git ls-files reported only {files.Count} source file(s); this repo has hundreds.");

        return files;
    }
}
```

**Step 2: Run the guard**

```bash
dotnet test Todo.sln --filter FullyQualifiedName~SourceLanguageTests
```

Expected: **2 passed.** The backlog covers every Danish file, and every backlogged file is still
Danish. If `No_comment_outside_the_backlog_is_written_in_Danish` fails here, the backlog list is
wrong — add the file it names rather than translating it now.

**Step 3: Commit**

```bash
git add tests/Todo.Api.Tests/SourceLanguageTests.cs
git commit -m "✅ Comments are English from here, with a backlog that only shrinks"
```

---

## Task 2: See the guard fail, on its own terms

A guard nobody has seen fail proves nothing. This is the cheapest possible demonstration and it
costs one line, twice.

**Step 1: Take one file off the backlog without translating it**

Delete `"src/Todo.Web/src/app/shortcuts/shortcut.ts"` from `Backlog`.

**Step 2: Run**

```bash
dotnet test Todo.sln --filter FullyQualifiedName~SourceLanguageTests
```

Expected: **`No_comment_outside_the_backlog_is_written_in_Danish` FAILS**, naming
`src/Todo.Web/src/app/shortcuts/shortcut.ts` with three line numbers and the Danish text.
`Every_backlogged_file_is_still_Danish` still passes.

**Step 3: Put it back, then break the other assertion**

Restore the entry. Now add a line that is not Danish — `"README.md"` will not do, it is not tracked
as a source file; use `"src/Todo.Web/src/app/review/review-store.ts"`, which is English.

**Step 4: Run**

```bash
dotnet test Todo.sln --filter FullyQualifiedName~SourceLanguageTests
```

Expected: **`Every_backlogged_file_is_still_Danish` FAILS** with
`src/Todo.Web/src/app/review/review-store.ts is on the backlog but holds no Danish comment any more`.

**Step 5: Restore and confirm green**

Remove the added line. Run again. Expected: **2 passed.** Nothing to commit — `git status` must be
clean.

---

## Tasks 3–7: Translate, one group per commit

Each task is the same five steps. The groups are ordered so that the riskiest — the templates that
name Tailwind classes — come after the guard has been exercised on plain TypeScript.

**The rules for every translation, in all five tasks:**

1. **Translate the meaning, not the words.** These comments carry measurements and reasons. If a
   sentence says a mutation was seen to fail, the English must say the same thing about the same
   mutation.
2. **Keep every Tailwind class name spelled exactly as it is.** Tailwind reads comments, so
   `min-h-0` in a comment is why `min-h-0` is in the bundle. Section "What was measured" lists the
   nine.
3. **Keep the Danish string literals.** `"Opgavestiller: Mette Kirkegaard"` is data about the
   product.
4. **Stay under 100 characters per line.** Prettier is a guard and runs inside `dotnet test`.
5. **Do not use `sed -i` or a naive rewrite.** Use `Edit`. This is a CRLF working copy and a tool
   that writes LF leaves `git diff` showing nothing while the file on disk really did change.

### Task 3: `shortcuts/` — 9 blocks in 5 files

**Files:** `shortcut.ts` (3), `shortcut.spec.ts` (2), `shortcut-key.ts` (1), `shortcut-key.spec.ts`
(2), `shortcut-store.spec.ts` (1), all under `src/Todo.Web/src/app/shortcuts/`.

Watch for: the comments here explain why registration happens in an `effect` with `onCleanup` rather
than in lifecycle hooks, and why `unregister` compares on `key.toLowerCase()`. Both are measured
reasons — carry them across intact.

**Step 1:** Translate the nine blocks with `Edit`.
**Step 2:** Remove the five paths from `Backlog`.
**Step 3:** Run `dotnet test Todo.sln --filter FullyQualifiedName~SourceLanguageTests`.
Expected: **2 passed.**
**Step 4:** Run `npm.cmd run test --prefix src\Todo.Web -- --watch=false`.
Expected: **358 passed**, unchanged — comments do not change behaviour, and a different number here
means an edit went into code.
**Step 5:** Commit.

```bash
git add src/Todo.Web/src/app/shortcuts tests/Todo.Api.Tests/SourceLanguageTests.cs
git commit -m "🌐 The shortcut layer explains itself in English"
```

### Task 4: `tasks/` TypeScript — 18 blocks in 5 files

**Files:** `task-list.ts` (2), `task-list.spec.ts` (13), `task-detail.ts` (1),
`task-detail.spec.ts` (1), `task-store.spec.ts` (1), under `src/Todo.Web/src/app/tasks/`.

Watch for: `task-list.spec.ts` is the file that is mixed *inside itself* — 13 Danish blocks beside
English ones. Read the English neighbours first and match their register.

Same five steps. Commit message: `🌐 The task list's specs explain themselves in English`.

### Task 5: The remaining import TypeScript — 3 blocks in 3 files

**Files:** `ado/ado-import.ts` (1), `jira/jira-import.ts` (1), `retro/retro-import.ts` (1).

Same five steps. Commit message: `🌐 The three import screens explain themselves in English`.

### Task 6: The templates — 75 blocks in 8 files

**Files:** `settings/settings.html` (15), `ado/ado-import.html` (17), `jira/jira-import.html` (12),
`tasks/task-row.html` (9), `tasks/task-detail.html` (6), `tasks/task-list.html` (5), `app.html` (3),
`src/index.html` (1).

This is the group that can change the CSS bundle. Seven of these eight name Tailwind classes in
their comments. **Task 8 is the check, and it belongs to this task's commit** — run it before
committing.

Watch for: `task-list.html`'s opening comment is the longest in the repository and explains the
side-by-side grid, why `min-h-0` is absent, and what `xl:-ml-2 + xl:pl-2` buys. Every class name in
it must survive spelled the same.

Steps 1–4 as above, then Task 8's bundle check, then commit:
`🌐 The templates explain themselves in English, with the class names untouched`.

### Task 7: The two C# files — 35 blocks

**Files:** `tests/Todo.Api.Tests/LongIdMigrationTests.cs` (34),
`tests/Todo.E2E/TaskListScreen.cs` (1).

`LongIdMigrationTests.cs` is the largest single job in this delivery: 34 blocks explaining why a
TEXT-to-INTEGER migration must be hand-written, why `PRAGMA foreign_key_check` proves almost nothing
alone, and why parents must be remapped before children. These are the most valuable comments in the
repository — translate them slowly.

**Step 4** differs: run `dotnet test Todo.sln` in full rather than Vitest.
Expected: **186 Core, 398 Api** unchanged.

Commit message: `🌐 The migration guard explains itself in English, and the backlog is empty`.

In the same commit, **delete the now-empty `Backlog` and
`Every_backlogged_file_is_still_Danish` with it**, and rewrite the class summary's "Why there is a
backlog" paragraph into a past-tense sentence recording that there was one.

---

## Task 8: Confirm the CSS bundle did not move

**When:** inside Task 6, before its commit.

**Step 1: Record the bundle before touching the templates**

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Then, from the repo root:

```bash
ls -l src/Todo.Host/wwwroot/styles-*.css
```

Write the byte count down.

**Step 2: Translate the templates, rebuild, and compare**

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
ls -l src/Todo.Host/wwwroot/styles-*.css
```

Expected: **the same byte count and the same hash in the filename.** A different one means a class
name changed spelling or was dropped from a comment — find it with:

```bash
git diff -U0 -- 'src/Todo.Web/src/app/**/*.html' | grep -E '^[-+].*(bg|text|border|min-h|max-w|overflow|divide|outline|gap|px|py|ml|pl|pr)-'
```

Fix the comment rather than the CSS. This is not cosmetic: `min-h-0` exists in the bundle **only**
because two comments name it, and dropping it silently removes a rule the app does not currently use
but whose absence would be invisible until something needed it.

---

## Task 9: The whole suite, and the numbers

**Step 1: Rebuild the frontend before E2E**

The E2E project has no build step and serves whatever is in `wwwroot`. Every task above touched
Angular sources, so without this Playwright tests the previous bundle.

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

**Step 2: Run everything**

```bash
Check.cmd
```

**Step 3: Compare the numbers**

| suite | before | expected after |
|---|---|---|
| `Todo.Core.Tests` | 186 | 186 |
| `Todo.Api.Tests` | 398 | **400** |
| `Todo.E2E` | 83 | 83 |
| Vitest | 358 | 358 |

The only change is **+2 in `Todo.Api.Tests`** for the guard's two facts — and after Task 7 removes
`Every_backlogged_file_is_still_Danish`, it is **+1**, so the final number is **399**. Any other
movement means a test was lost or duplicated, which is the whole reason these numbers are written
down.

**Step 4: Update the numbers**

Update the four counts in `CLAUDE.md`'s "Testtal" section and in `docs/HANDOFF.md`, and say in one
sentence why `Todo.Api.Tests` moved. Both files are translated in delivery 3, so leave them Danish
here — a half-translated file is worse than a Danish one.

**Step 5: Commit**

```bash
git add CLAUDE.md docs/HANDOFF.md
git commit -m "📝 The comments are English, and the one new test is the guard that keeps them so"
```

---

## What this delivery deliberately does not do

- **It does not touch `docs/plans/`.** Thirty delivered plans, 18,120 lines, frozen as a dated
  archive. Delivery 3 adds `docs/plans/README.md` saying so.
- **It does not touch `CLAUDE.md` or `HANDOFF.md` beyond the four numbers.** Delivery 3 translates
  and cuts them, and it goes last because delivery 2 makes several of their sections wrong.
- **It does not rename anything.** No identifier in this repository is Danish, so there is nothing
  to rename. If you find one, it is a discovery worth writing down rather than a task here.
- **It does not touch the three collation comments.** `JiraTaskSource.cs`,
  `AdoTaskSourceTests.cs` and `JiraTaskSourceTests.cs` are already English. They are listed in "What
  was measured" precisely so nobody translates them by mistake.
