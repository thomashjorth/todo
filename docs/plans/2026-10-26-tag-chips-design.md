# Tags as coloured chips - design

Decided 2026-10-26, approved by the user section by section. The wish, in the user's words: *"jeg vil
gerne have chips til tags"*, on four surfaces - the panel, the row, the filter and the settings
group, where *"Det skal være muligt at slette nemt. Så de skal ikke være små"* - with a colour per
tag: *"gå regnbuen igennem. Og når vi er løbet tør for farver går vi i nuancerne"*, and *"hvis et tag
bliver slettet bliver farven frigjort"*. Builds on `2026-10-22-tags-design.md`, whose section 1 said
"no colour"; this design takes that back.

## 1. The colours

- **The order is the rainbow:** red, orange, yellow, green, blue, indigo, violet - Tailwind's own
  families, so styling stays utility classes only.
- **When the seven are used, the shades follow.** Three rounds: light (`bg-*-100` with `text-*-900`),
  middle (`bg-*-300` with `text-*-950`) and deep (`bg-*-700` with white). The dark theme mirrors each
  round (`900`, `700`, `300`) with a text step that reads on it. 21 colours before any repeats; past
  21, the colour used by the fewest tags, the earliest in the order on a tie.
- **The colour is stored on the tag when it is created**, as its slot 0-20 (`Tag.Color`), and the new
  tag gets **the first free slot**. Delete the green one, and the next new tag is green again: the
  user's "frigjort". Not chosen and not changeable - the user chose the automatic order; a picker can
  come later without changing anything here.
- **The price:** a column and a migration. Existing tags get their slots in the order they were
  created (by id), so the user's first seven are the rainbow.
- **Rejected: deriving the colour from the id.** No migration, but a deleted tag would leave a hole
  in the rainbow nothing ever fills - the opposite of "frigjort".

## 2. The chips on the four surfaces

All chips are rounded pills in the tag's colour. Where a chip toggles, the state is never carried by
colour alone: **pressed** is filled with the colour and has a `✓` before the name, **not pressed** is
an outline in the colour on a neutral background, without the tick.

- **The row:** filled chips under the title, display only, a little larger than the thin frames of
  today but still `text-xs`.
- **The filter:** toggle chips (`aria-pressed`, Alt+L) as today.
- **The panel:** the checkboxes become toggle chips - buttons with `aria-pressed` - that save on a
  click; Alt+Shift+G focuses the first. A button's pressed state comes only from its binding, so the
  `[checked]` write-back the checkboxes needed (CLAUDE.md) goes away with them.
- **Settings › Tags:** full-size chips (`text-sm`) with a `×` each, its own button at least 24 px with
  the name "Slet <tag>". **One click deletes, without confirmation** - the user asked for easy, and the
  price is that the tag leaves every task carrying it.

## 3. Tests

- **Contrast:** one task carrying 21 tags, so every colour is painted filled on the row, pressed and
  not in the filter, in the panel and in the settings group, in both themes.
- **Api:** a deleted tag's slot goes to the next new tag; the 22nd tag repeats the least used colour;
  the colour is on the wire.
- **Migration:** existing tags get slots in id order.
