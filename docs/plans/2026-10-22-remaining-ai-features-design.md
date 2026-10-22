# The remaining three AI features — design

Decided 2026-10-22. Status: **not approved section by section** — the user asked for "the remaining
features the same way, I test everything at the end". Every choice below is Claude's unless it says
otherwise, written down with its reason for the review.

Builds on `2026-10-22-ai-foundation-design.md` and `2026-10-22-three-ai-features-design.md`, and on
what the first round's review found: answers are slow on the user's CPU (no GPU, `ollama ps` says
100 % CPU), so every feature here is one call per click, never one per row.

## 1. What the user decided along the way

- **The answer's language follows the source** (2026-10-22): retro boards and Azure DevOps are
  always English, Jira always Danish. Only text the user typed is guessed (`AiLanguage`).
- **Keyboard shortcuts for every AI action** (2026-10-22, "husk genveje").
- **EmbeddingGemma may be downloaded** and put on the approved list (2026-10-22). It is Google's,
  so the work-PC rule against Chinese models is kept.
- **Prompts are measured against the real model** with `tests\Todo.AiEval`, not only against the
  fake.

## 2. Action titles in the retro import

A retro note is usually an observation — "We should get better at testing" — and a task wants an
action. One button, **"Foreslå handlinger"** (Alt+H), asks the model once for every *selected* row
that is not imported yet, and each row then shows the suggested title above the original, with a
button to switch between them. The suggestion is used unless the user switches back: they asked
for it, and the original stays one click away.

- **Owner guess.** When `Action Owner` is empty and the note names a person, the model guesses the
  owner. The host checks the guess against the user's aliases, so a row can say "probably you". The
  guess becomes the task's requester on import, the same field `Action Owner` fills today. It never
  selects a row by itself.
- **Deduplication is untouched.** The key stays the hash of the original
  `Content|Zone|Author|Created`, and the original title is still what the preview shows as the row.
- **English**, because retro boards always are.
- **The button sits outside the row's `<label>`**, because a control inside it takes the label's
  text into its accessible name (CLAUDE.md).

## 3. Search by meaning, and duplicates across sources

Both need vectors, and Gemma 4 cannot make them (`501 does not support embeddings`, measured), so
**EmbeddingGemma runs beside it** through Ollama's `/api/embed`.

- **Search.** A "Betydning" button beside the search field (Alt+B) asks the host to rank every task
  by meaning against the typed words. The list then shows the substring matches **and** the tasks
  that came back above the threshold, so "den med certifikatet" finds "Forny TLS på testserveren".
  Typing again clears it. Like today's search, it can only show tasks that are loaded.
- **Duplicates.** After a Jira or Azure DevOps preview has loaded, and when search is ready, the
  frontend sends the rows to the host, which compares them with every task from **another** source
  (manual tasks included). A row above the threshold shows "Ligner: <title>". Same-source duplicates
  already have their own rule and are skipped. It is a warning, never a block.
- **Where the vectors live:** see section 6, decided by measurement.
- **The threshold** is set from eval cases with pairs that are and are not the same task.

## 4. Help with the weekly review

A button at the top of `/review`, **"Skriv ugens overblik"** (Alt+U, for "ugen"), sends the host a
request, and the host gathers the facts itself: what was completed in the last seven days, what has
waited longest (with days), and which Someday tasks were created more than 90 days ago. The model
turns them into three to five short points in Danish, including what it would do about them.
Nothing is saved, so there is nothing to accept.

- **"Not touched" can only mean "created long ago".** `TaskItem` has no `UpdatedAt` and there is no
  revision log, so a Someday task edited yesterday but created in spring counts as stale. The
  HANDOFF called this an argument for the revision log first. Building one is a delivery of its own,
  so this one says it plainly in the prompt and in the design, and the eval measures the rest.
- **E4B rather than 26B A4B**, unless the eval says otherwise. 26B on this CPU would be several
  times slower than the 4-8 s every call already takes.

## 5. The API

All under the `Ai` tag, each a `POST` because each is a computation:

| Route | In | Out |
| --- | --- | --- |
| `/api/ai/retro-actions` | rows: key, text, owner | rows: key, title, guessed owner, owner is the user |
| `/api/ai/search` | query | task ids, best first |
| `/api/ai/duplicates` | source id, rows: key, title, note | matches: key, task id, task title |
| `/api/ai/review-overview` | — | points |

`GET /api/ai/status` gains `searchReady`, because search and duplicates need the second model, and
the first can be installed without it.

## 6. Measurements

EmbeddingGemma (`embeddinggemma:latest`, 621 MB, 768 dimensions) on the user's CPU, 2026-10-22.

**Speed.** 10 task texts in 0.15 s, 100 in 1.38 s, a query in 25-50 ms, the cold load 0.9 s.
That decides where the vectors live: **in memory**, not in SQLite as the HANDOFF sketched. A
singleton keyed by task id and a hash of the text embeds what is new or changed at the first search
after a start - about 1.4 s per hundred tasks - and nothing after that. A table would cost a
migration (CLAUDE.md's longest chapter) to save that second and a half once per start. Claude's
choice; the table is the answer if the list grows to thousands.

**Search.** Ten Danish tasks, seven queries, query prefix `task: search result | query:` (the
model's own convention). Plain task text beat both document prefixes, so plain text it is.

| Query | Best match | Score | Next |
| --- | --- | --- | --- |
| den med certifikatet | Forny TLS-certifikatet på testserveren | 0.43 | 0.36 |
| mødet om sprinten | Book mødelokale til sprint review | 0.54 | 0.30 |
| eksport til excel | Svar Lene om CSV-eksportfejlen | 0.62 | 0.27 |
| nøgle til gatewayen | Opdatér API-gatewayens adgangsnøgle i Key Vault | 0.55 | 0.29 |
| regning | *(Ryd op i backloggen)* | 0.29 | — |
| personnummer opslag | *(Ryd op i backloggen)* | 0.31 | — |

So **0.40 is the search threshold**: every concept match cleared it and no noise did. And the limit
is now known rather than assumed: the model matches *concepts*, not *knowledge* — "regning" does
not find "faktura", "personnummer" does not find "CPR". Those two are eval cases that say so.

**Duplicates.** Six pairs that are the same task, four of them across Danish and English, scored
0.63-0.74. Six that are not scored 0.35-0.62 — except "Renew the TLS certificate on the *test*
server" against "*production* server" at 0.86, and two Danish login tasks at 0.70. So **0.62 is the
duplicate threshold**, and the word on screen is "Ligner", not "Dublet": two tasks that close are
worth a look even when they are not the same.
