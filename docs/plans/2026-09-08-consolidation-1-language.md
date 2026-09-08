# Consolidation, delivery 1: one language — implementation plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task.

**Goal:** Every code comment in the repository is English, and a guard keeps it that way.

**Architecture:** A ratchet. `SourceLanguageTests` classifies each comment block as Danish or English
by scoring function words, and carries a backlog of the 27 files that are still Danish. Nothing on
the backlog fails; anything off it does. Each commit translates a group of files and removes them
from the backlog, so **every commit is green** and the guard can never go backwards. A second
assertion requires every backlogged file to still contain Danish, so a translated file cannot be
left on the list.

**Tech stack:** xUnit in `Todo.Api.Tests`, `ExternalCommand.RunAsync` + `git ls-files` for the file
listing (the same shape as `LineEndingTests`), no new dependencies.

**Design document:** `docs/plans/2026-09-08-consolidation-design.md`, sections 6, 8 and 9.

---

## What was measured before this plan was written

- **27 files hold 162 Danish comment blocks.** Task 1's backlog is the list; the per-file counts are
  in the group tasks below.
- **A bare `æøå` test does not work on its own.** Three files carry **English** comments whose
  subject is Danish collation — `src/Todo.Host/Jira/JiraTaskSource.cs`,
  `tests/Todo.Api.Tests/AdoTaskSourceTests.cs`, `tests/Todo.Api.Tests/JiraTaskSourceTests.cs` — so
  they must spell the letters out. A character test flags all three. Word scoring does not, which is
  why the classifier is built the way it is. **The letter is a tie-breaker rather than a trigger**:
  `danish > english || (letter && danish >= english)`. Measured: the tie-break adds four genuinely
  Danish blocks and **zero** false positives, because the three collation comments are dense enough
  English that their English count exceeds their Danish one.
- **Five spec files trip a character test the same way** and are also correctly English:
  `ado-import.spec.ts`, `app.spec.ts`, `jira-import.spec.ts`, `settings.spec.ts`,
  `settings-store.spec.ts`.
- **There are no Danish identifiers anywhere** — not one method, class, variable or `data-testid`.
  The Danish in C# is string literals asserting on the app's Danish surface. **They stay.**
- **Nine comments in seven files name Tailwind utility classes** — `min-h-0`, `xl:overflow-y-auto`,
  `dark:bg-gray-800`, `outline-2`, `outline-offset-2`, `max-w-2xl`, `divide-y`, `gap-6`, `px-2`.
  Tailwind extracts candidates from raw text, comments included, so **a rewritten comment can change
  the CSS bundle.** `min-h-0` is generated today *only* because two comments name it. Task 8 checks.
- **Prettier is `printWidth: 100`** and `FrontendFormattingTests` runs it inside `dotnet test`. A
  translated comment that reflows past 100 characters turns the suite red.

### Two defects this plan shipped with, and what they cost

Both were found by review after Task 1 was committed, and both are now fixed. They are recorded
because each is a trap the next reader could fall into again.

**The guard's first version could not see 12.4% of the code it claimed to scan.** It stripped string
literals with one regex pass and extracted comments with another. Strings and comments are mutually
exclusive, so two passes desynchronise: an apostrophe in an English possessive or a Danish
contraction — `URL'en`, `the server's` — opened a phantom string literal that ran to the next quote
and deleted every comment in between. Measured: 46 of 212 scanned `.cs`/`.ts` files held phantom
literals swallowing 4,927 lines. The main assertion **passed over three Danish comment blocks** in
`app.ts` and `settings/settings.ts`, neither of which was on the backlog, because the same bug had
been in the prototype that derived the backlog. It also leaked Danish *fixture strings* into comment
bodies — the exact opposite of what the stripper existed to do. The fix is one left-to-right walk
where a `'` or `"` run stops at the first newline; correct line numbers fall out of it.

**The five translation groups covered 23 of the 25 backlogged files.** `review.html` and
`task-row.ts` were on the list and in no group, so the backlog could never have emptied and Task 7's
final step would have been impossible. Plain arithmetic, invisible until the group totals were added
up. **Check that the group counts below sum to 162 across 27 files before trusting them.**

---

## Task 1: the guard — DONE (`63e16b7`, fixed in `986f4f9`)

**File:** `tests/Todo.Api.Tests/SourceLanguageTests.cs`

Read the committed file rather than a code block here; an earlier revision of this plan carried the
broken version inline, and a stale copy of a guard is worse than none.

What it does, and what each part is load-bearing for:

- **`Backlog`** — the 27 files still Danish, built from an array with an assertion that the
  deduplicated count matches, because a `HashSet` collection initializer swallows a duplicate
  silently.
- **`No_comment_outside_the_backlog_is_written_in_Danish`** — the rule.
- **`Every_backlogged_file_is_still_Danish`** — stops the list rotting. Remove a file in the same
  commit that translates it.
- **Per-extension floors** — `.cs >= 1000`, `.ts >= 300`, `.html >= 50` comment blocks actually
  parsed. `files.Count > 100` proves git ran, not that the pathspecs did: `.cs` alone is 150 and
  clears it. The floors are what must survive when the backlog goes, because after the last
  translation the main assertion would otherwise become one that cannot fail.
- **One lexer, not two regex passes** — see the defect above.
- **`TrimEnd('\r')`** on joined comment lines: this is a CRLF working copy, and a bare CR inside a
  failure message makes a console overwrite the line.

Its one documented limitation: the holes in a C# interpolated string (`$"…{a["b"]}…"`) are read as
string content, so a quote inside `{…}` can mis-pair. Bounded to one line by the newline rule, and
no such line in the repository carries a comment. Fixing it needs brace-depth recursion; the class
doc says so rather than leaving it silent.

## Task 2: see the guard fail — DONE

Recorded because a guard nobody has seen fail proves nothing, and because what it was seen to fail
on turned out to be a real bug rather than a staged one.

- **The main assertion, on the real defect.** `app.ts` removed from the backlog →
  `2 comment block(s) are written in Danish … src/Todo.Web/src/app/app.ts:47 * Porten er tilfældig,
  så URL'en bygges …` — **line 47, correct**, which the broken lexer could not have reported.
- **Each floor, one mutation at a time**, with `git diff --stat` between. `*.html` → `*.htmlx` gave
  *"The walk found only 0 comment block(s) in the .html files, under the floor of 50"* while
  `files.Count > 100` stayed green at 212 files — precisely the hole the floors close.
- **The duplicate check** → *"Backlog lists 28 entries of which only 27 are distinct… app.ts (listed
  2 times)"*.
- **The filtered-vs-untracked messages** → three cases, three distinct sentences.
- **A tracked file moved aside on disk** → *"git tracks … but there is no file at …"* rather than a
  `FileNotFoundException`.

---

## Tasks 3–7: translate, one group per commit

Each task is the same five steps. The counts below sum to **162 blocks across 27 files** — the whole
backlog. Verify that before you start; the first version of this plan did not add up.

**The rules for every translation, in all five tasks:**

1. **Translate the meaning, not the words.** These comments carry measurements and reasons. If a
   sentence says a mutation was seen to fail, the English must say the same thing about the same
   mutation.
2. **Keep every Tailwind class name spelled exactly as it is.** Tailwind reads comments, so
   `min-h-0` in a comment is why `min-h-0` is in the bundle.
3. **Keep the Danish string literals.** `"Opgavestiller: Mette Kirkegaard"` is data about the
   product.
4. **Stay under 100 characters per line.** Prettier is a guard and runs inside `dotnet test`.
5. **Do not use `sed -i` or a naive rewrite.** Use `Edit`. This is a CRLF working copy and a tool
   that writes LF leaves `git diff` showing nothing while the file on disk really did change.
6. **If a comment must name a Danish letter, quote it** — backticks, `"quotes"` or `<c>tags</c>` are
   all stripped before scoring. This is what the three collation comments would do if they were
   written today.

### Task 3: `shortcuts/` — 9 blocks in 5 files

`shortcut.ts` (3), `shortcut.spec.ts` (2), `shortcut-key.ts` (1), `shortcut-key.spec.ts` (2),
`shortcut-store.spec.ts` (1), all under `src/Todo.Web/src/app/shortcuts/`.

Watch for: these explain why registration happens in an `effect` with `onCleanup` rather than in
lifecycle hooks, and why `unregister` compares on `key.toLowerCase()`. Both are measured reasons —
carry them across intact.

**Step 1:** Translate with `Edit`. **Step 2:** Remove the five paths from `Backlog`.
**Step 3:** `dotnet test Todo.sln --filter FullyQualifiedName~SourceLanguageTests` → 2 passed.
**Step 4:** `npm.cmd run test --prefix src\Todo.Web -- --watch=false` → **358 passed**, unchanged; a
different number means an edit went into code rather than a comment.
**Step 5:** Commit.

```bash
git add src/Todo.Web/src/app/shortcuts tests/Todo.Api.Tests/SourceLanguageTests.cs
git commit -m "🌐 The shortcut layer explains itself in English"
```

### Task 4: `tasks/` TypeScript — 39 blocks in 6 files

`task-list.spec.ts` (19), `task-detail.spec.ts` (6), `task-row.ts` (5), `task-detail.ts` (4),
`task-list.ts` (3), `task-store.spec.ts` (2), under `src/Todo.Web/src/app/tasks/`.

The largest TypeScript group, and it grew from 18 to 39 when the lexer was fixed — the phantom
strings had been hiding half of it. `task-list.spec.ts` is mixed *inside itself*: Danish blocks
beside English ones. Read the English neighbours first and match their register.

Same five steps. Commit: `🌐 The task list explains itself in English`.

### Task 5: the remaining TypeScript — 6 blocks in 5 files

`app.ts` (2), `ado/ado-import.ts` (1), `jira/jira-import.ts` (1), `retro/retro-import.ts` (1),
`settings/settings.ts` (1).

`app.ts` and `settings.ts` are the two files the broken lexer could not see. `app.ts:58` is the
comment explaining that `Ctrl+Alt` is AltGr on a Danish keyboard and that swallowing it would break
typing `@`, `£` and `$` — one of the more valuable comments in the frontend.

Same five steps. Commit: `🌐 The shell and the import screens explain themselves in English`.

### Task 6: the templates — 73 blocks in 9 files

`ado/ado-import.html` (17), `settings/settings.html` (15), `jira/jira-import.html` (13),
`tasks/task-row.html` (9), `tasks/task-detail.html` (6), `tasks/task-list.html` (5),
`review/review.html` (4), `app.html` (3), `src/index.html` (1).

This is the group that can change the CSS bundle: seven of these nine name Tailwind classes in their
comments. **Task 8 is the check and it belongs to this task's commit** — run it before committing.

Watch for: `task-list.html`'s opening comment is the longest in the repository and explains the
side-by-side grid, why `min-h-0` is absent, and what `xl:-ml-2 + xl:pl-2` buys. Every class name in
it must survive spelled the same.

Steps 1–4 as above, then Task 8, then commit:
`🌐 The templates explain themselves in English, with the class names untouched`.

### Task 7: the two C# files — 35 blocks

`tests/Todo.Api.Tests/LongIdMigrationTests.cs` (34), `tests/Todo.E2E/TaskListScreen.cs` (1).

`LongIdMigrationTests.cs` is the largest single job in this delivery: 34 blocks explaining why a
TEXT-to-INTEGER migration must be hand-written, why `PRAGMA foreign_key_check` proves almost nothing
alone, and why parents must be remapped before children. These are among the most valuable comments
in the repository — translate them slowly.

**Step 4** differs: run `dotnet test Todo.sln` in full rather than Vitest. Expected **186 Core, 400
Api** at this point.

In the same commit, **delete the now-empty `Backlog` and `Every_backlogged_file_is_still_Danish`
with it**, and rewrite the class summary's "why there is a backlog" paragraph into a past-tense
sentence recording that there was one. **The per-extension floors stay** — they are what keeps the
remaining assertion honest once the backlog is gone.

Commit: `🌐 The migration guard explains itself in English, and the backlog is empty`.

---

## Task 8: confirm the CSS bundle did not move

**When:** inside Task 6, before its commit.

**Step 1:** before touching the templates, `powershell -ExecutionPolicy Bypass -File
scripts\build-web.ps1`, then `ls -l src/Todo.Host/wwwroot/styles-*.css`. Write the byte count and
the filename hash down.

**Step 2:** translate, rebuild, compare. Expected: **the same byte count and the same hash.** A
different one means a class name changed spelling or was dropped from a comment. Find it with:

```bash
git diff -U0 -- 'src/Todo.Web/src/app/**/*.html' | grep -E '^[-+].*(bg|text|border|min-h|max-w|overflow|divide|outline|gap|px|py|ml|pl|pr)-'
```

Fix the comment rather than the CSS. `min-h-0` exists in the bundle **only** because two comments
name it, and dropping it silently removes a rule whose absence would be invisible until something
needed it.

---

## Task 9: the whole suite, and the numbers

**Step 1:** rebuild the frontend before E2E. The E2E project has no build step and serves whatever
is in `wwwroot`, so without this Playwright tests the previous bundle.

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

**Step 2:** `Check.cmd`

**Step 3:** compare.

| suite | before | expected after |
|---|---|---|
| `Todo.Core.Tests` | 186 | 186 |
| `Todo.Api.Tests` | 398 | **399** |
| `Todo.E2E` | 83 | 83 |
| Vitest | 358 | 358 |

The baseline was measured, not borrowed: `Check.cmd` on `580d6d8` gave 186 / 398 / 83 / 358, all
green. The only change is **+1 in `Todo.Api.Tests`** — the guard adds two facts and Task 7 removes
one of them. The floors and the duplicate check are assertions inside the two existing facts, not
new ones. Any other movement means a test was lost or duplicated, which is the whole reason these
numbers are written down.

**Step 4:** update the four counts in `CLAUDE.md`'s "Testtal" section and in `docs/HANDOFF.md`, and
say in one sentence why `Todo.Api.Tests` moved. Both files are translated in delivery 3, so **leave
them Danish here** — a half-translated file is worse than a Danish one.

**Step 5:** commit.

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
  to rename. If you find one, that is a discovery worth writing down rather than a task here.
- **It does not touch the three collation comments.** `JiraTaskSource.cs`, `AdoTaskSourceTests.cs`
  and `JiraTaskSourceTests.cs` are already English. They are named in "What was measured" precisely
  so nobody translates them by mistake.
