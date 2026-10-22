# Three AI features — design

Decided 2026-10-22. Status: **not approved section by section.** The user asked mid-delivery for
"the three simplest AI features" and said they would review at the end, so every choice below is
Claude's, written down with its reason so the review has something to push against.

Builds on `2026-10-22-ai-foundation-design.md`: `OllamaClient.CompleteAsync<T>`, `AiStore.ready`,
`FakeOllama`, and the closed port in `RunningHost`.

## 1. Which three, and why

Of the six in `docs/HANDOFF.md`, these three need no new model, no new table and no change to an
import's deduplication:

| Feature | Where | What the user accepts |
| --- | --- | --- |
| **Quick entry** | Beside the new-task field | A task with title, requester and deadline |
| **Split a task** | The detail panel's subtasks | The ticked subtask suggestions |
| **Summarise an imported issue** | The detail panel's note | The summary, replacing the note |

Left out: **action titles in the retro import** (it also guesses an owner, and the suggestion must
sit beside the original without touching the `Content` + `Zone` + `Author` + `Created` key), **search
by meaning** (needs an embedding model the script does not install), and **help with the weekly
review** (wants the revision log first, and maybe the larger model).

## 2. Measured against the real model

`gemma4:e4b` on the user's machine, 2026-10-22, `think:false`, `temperature:0`, warm. The probes
are Python against `/api/chat` with the prompts below.

| Prompt | Time | Result |
| --- | --- | --- |
| Quick entry, today's date only | ~5 s | "næste mandag" became **2026-10-27, a Tuesday** |
| Quick entry, with a 14-day calendar | ~7.3 s | Five of five dates right, "næste mandag" = 2026-10-26 |
| Split, a task with a note | 7.9 s | Four sensible Danish subtasks |
| Summary, a Danish Jira description | 7.8 s | Two sentences, signature and steps left out |
| Summary, an English description | 6.9 s | Two English sentences |

**The model cannot count days.** Given only today's date, it put a weekday on the wrong date. So the
prompt carries the next fourteen days written out with their Danish weekday names, computed by the
host from `IClock.Today`, and the model only has to look the day up. Of the 7.3 s, about 3.9 s is
reading the ~390-token prompt; the calendar is the price of a right date.

**Seven seconds is the wait for every feature.** That is why every button says what it is doing
while it waits (below), and why nothing is called without a click.

## 3. The API

All under the `Ai` tag, all `POST` because each one is a computation with a cost, not a resource.

- `POST /api/ai/parse-task` `{ text }` → `{ title, requester, deadline }`. `ai.textRequired` for
  blank text. The title falls back to the typed text if the model returns none, and is cut to the
  500 characters a task allows. A deadline that is not `yyyy-MM-dd` becomes `null` rather than an
  error: the rest of the suggestion is still worth having.
- `POST /api/ai/tasks/{id}/split` → `{ titles }`. 404 for an unknown task. Trimmed, blanks dropped,
  duplicates dropped case-insensitively, existing subtasks dropped, at most six, each cut to 500.
- `POST /api/ai/tasks/{id}/summary` → `{ summary }`. 404 for an unknown task, `ai.noteRequired`
  when the note is empty. An empty summary is `ai.unreadableAnswer`.
- Every `SourceException` from the client becomes a 400 with its code, as the import endpoints do.

**The prompts are English, the answers follow the user's language.** English because that is the
language the model follows instructions best in and the code's language (ADR 10); "in the same
language as the line/task/description" because the measured English description got an English
summary and the Danish ones Danish.

## 4. The screens

All three are **hidden unless `AiStore.ready()`**. Suggestions live in `AiStore`, not in the
components, for the same reason as `TaskStore.askingWho`: an accept reloads the list, and state held
in a row can be destroyed with it.

- **Quick entry.** A "Fortolk" button to the right of the new-task field. Enter keeps creating a
  plain task exactly as before, so nothing anyone types today changes meaning. The suggestion shows
  under the field — title, requester and deadline — with "Opret" and "Kassér". "Opret" creates the
  task with all three fields and empties the field.
- **Split.** "Foreslå underopgaver" under the subtasks. The suggestions are checkboxes, **all ticked**
  (the model was asked for subtasks, so the expected answer is yes), with "Tilføj valgte" and
  "Kassér". The ticked ones are created one after another and the list is loaded **once**.
- **Summary.** "Opsummér" beside "Rediger note", **only on an imported task** — one with an
  `externalUrl`. On a task of your own the note is the only copy, and replacing it would lose it; on
  an imported one the full description is one click away behind the link (ADR 14 keeps the note a
  local enrichment, and re-import never overwrites it). The summary shows above the note with
  "Erstat noten" and "Kassér".
- **While waiting** the button is disabled and reads "Spørger modellen…". This is not a loading
  state in the forbidden sense; it is the one feedback a seven-second wait needs.
- **Errors** show under the feature that failed, one signal per feature in `AiStore`, so a failed
  split does not print its message in the quick-entry field.

**No new keyboard shortcuts.** The panel's `alt-shift` layer has nine letters and two guards that
count them, and a shortcut for a seven-second action is a later decision.

## 5. Tests

- **Api:** each endpoint against `FakeOllama` — what the prompt carries (the text, the calendar, the
  note, the existing subtasks), the clean-up rules above, the 404s and the two new codes, and that an
  unavailable model is a 400 with `ai.unavailable`.
- **Vitest:** `AiStore`'s three calls and their errors; each button hidden when not ready; accept and
  discard on each screen.
- **E2E:** one journey per feature against `FakeOllama`, the whole chain real.
- **Contrast:** the three suggestion panels and their error lines in both themes, with the status
  and the answers staged through `page.RouteAsync`. A branch that is never rendered is never measured.
