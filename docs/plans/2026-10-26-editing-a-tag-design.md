# Editing a tag - design

Decided 2026-10-26, approved by the user. The wish: *"Det skal være muligt at overstyre farven og
navnet"*. The user chose that a click on the chip opens the editing (rather than a pencil beside the
cross). Builds on `2026-10-26-tag-chips-design.md`, and takes back "no rename" from section 1 of
`2026-10-22-tags-design.md`.

## 1. In Settings › Tags

- **A click on a chip opens its editing** in a panel under the chips: the name field and the 21
  colours in three rows - light, middle, deep. One tag open at a time; a click on the open chip folds
  it again. The cross still deletes with one click.
- **The name saves on Enter or blur**, like every field in the app. An empty name is rolled back to
  the old one, like a task's title. A name another tag has is refused with `tags.duplicate`, case
  aside; renaming "estimat" to "Estimat" is allowed, being the same tag.
- **The colour saves on a click.** Each colour is a button with `aria-pressed` and a name such as
  "Blå, lys"; the chosen one has a tick.
- **Two tags may share a colour when the user chooses it.** The automatic slot counts a chosen colour
  as used: choose green for "estimat", and the next new tag does not get green while others are free.

## 2. API

`PUT /api/tags/{id}` with `name` and `color`, both required - a full replacement like the task's
update, which is harmless here because a tag has no other field. The name's codes are the creation's
(`tags.nameRequired`, `tags.nameTooLong`, `tags.duplicate`), and a colour outside 0-20 is
`tags.colorInvalid`. A rename reaches every task at once, because a task refers to the tag and not to
its name; the task list reads its tags again the next time it opens.

## 3. Tests

- **Api:** rename, own name in another case, duplicate, empty name, colour outside the palette, the
  rename seen on a task, a chosen colour counted as used, 404 for an unknown tag.
- **E2E:** a journey that renames and recolours from Settings.
- **Contrast:** the colour picker in both themes.
