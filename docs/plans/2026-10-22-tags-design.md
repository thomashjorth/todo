# Tags on tasks — design

Decided 2026-10-22. Status: **not approved section by section** — the user asked for it mid-delivery
and tests everything at the end. The wish, in the user's words: *"tags på opgaver. Jeg definerer selv
tags. Men når en opgave oprettes så foreslår programmet selv uden at blokere ingen eller flere af de
tags der er at vælge imellem. Man skal kunne filtrere opgaver vha tags."* Everything below that is
not in that sentence is Claude's choice, with its reason.

## 1. Data

- **`Tag`** (`Id` long, `Name` up to 50 characters, unique without regard to case) and a join table
  **`TaskTags`** (`TaskItemId`, `TagId`), both foreign keys cascading: deleting a tag takes it off
  every task, deleting a task takes its tags with it. A table rather than a JSON setting like the
  delegates, because a task refers to a tag and a rename must not orphan it.
- **A hand-checked migration**, `Tags`. CLAUDE.md's migration chapter is the checklist: scaffolded
  output read line by line for stray `AlterColumn`s, and the model-against-database guard extended to
  the two new tables.
- **No colour, no order, no rename.** Tags sort by name. Rename is a later decision: delete and
  re-add costs the assignments, and that price is written here rather than discovered.

## 2. API

- `GET /api/tags`, `POST /api/tags` (`tags.nameRequired`, `tags.nameTooLong`, `tags.duplicate`),
  `DELETE /api/tags/{id}`.
- `PUT /api/tasks/{id}/tags` with the full list of tag ids. **Its own route, not a field on
  `UpdateTodoTaskRequest`**: that request is a full replacement which reads an absent field as
  "clear" (CLAUDE.md), so every save of a title would have to carry the tags or wipe them.
- `TodoTask.tags` — id and name per tag — is **optional** on the contract, though the host always
  sends it. Required, it would be one more field in every hand-written wire fixture and every
  `new TodoTask({...})` in the specs (CLAUDE.md's "a new required field" list); optional, the client
  reads a missing list as empty, which is what it would be.
- `POST /api/ai/tasks/{id}/tag-suggestions` → tag ids. The prompt gives the task's title and note and
  the user's tag names, and the **JSON schema's `enum` is the tag names**, so the model can only pick
  among them; the host maps the names back to ids and drops any the task already has.

## 3. Suggesting without blocking

A task is created exactly as today — Enter or quick entry, nothing waits. Then, when the model is
ready and at least one tag exists, the app asks for suggestions **in the background** and shows them
under the new-task field, where the user still is: "Foreslåede tags til 'Køb mælk': Privat, Indkøb",
with "Tilføj" and "Afvis". The accept button takes the focus, so Enter adds them and Escape throws
them away, as with every other AI suggestion. An answer with no tags shows nothing. A failure shows
nothing either: a suggestion nobody asked for is not worth an error line.

Imports do not ask: one model call per imported row is seconds each on the user's CPU.

## 4. Filtering and assigning

- **The filter** is a row of tag buttons above the list, each pressed or not (`aria-pressed`).
  **Several pressed narrow the list (AND)**, the way the search narrows it: each choice takes tasks
  away. Not saved, like the search. Alt+L focuses the first one.
- **In the panel**, a "Tags" field lists every tag as a checkbox; ticking saves at once, like the
  other fields. Alt+Shift+G focuses it.
- **The row shows the task's tags** after its title. Inside the row's button on purpose - a reader
  should hear them - which means `RowTitled` no longer matches a tagged row exactly; journeys with
  tags use `RowShowing`.
- **Settings** get a seventh group, "Tags": a field to add one and a list with a remove button each,
  the delegates' pattern.
