# 7. Only Tailwind utility classes, with two measured exceptions

Date: 2026-08-13
Status: Accepted

## Context

There was no design brief to satisfy. The app has one user, no brand to match and no
designer to answer to — so every hour spent inventing a palette, naming tokens or arguing
with a cascade would have been an hour spent on nothing anyone asked for.

Tailwind's own defaults are good. Taking them as given removes the question rather than
answering it.

## Decision

Styling is standard Tailwind utility classes, with no project stylesheet, no custom tokens
and no SCSS. Every `bg-*`, `text-*` and `border-*` has a `dark:` counterpart, because the app
has no theme switch and both directions must be written out. See ADR 11.

There are exactly two sanctioned exceptions, and both exist because a utility class cannot
express the value:

- **One CSS rule**, clipping a morphing task row so it does not paint over the health line
  during a view transition. A nested view-transition group has no utility form.
- **Two inline styles**, binding `view-transition-name` and `view-transition-group`. The name
  is *data* — the task's id — and a utility class is static.

## Alternatives rejected

**A small project stylesheet for "just the shared bits".** The shared bits grow, and the
moment there are two places a colour can come from, every element needs both checked.

**A custom Tailwind theme.** Tokens worth naming come from a design language the app does not
have.

## Consequences

- An element's classes say the whole truth about how it looks. Nothing else reaches it.
- Dead styling disappears with the markup that used it, rather than outliving it in a
  stylesheet nobody dares prune.
- The palette is Tailwind 4's **oklch**, not Tailwind 3's hex, so contrast ratios have to be
  computed from Tailwind's own theme file rather than looked up from the older palette.
- Accessibility cannot be argued from the classes, because what a colour resolves to depends
  on what is behind it. It is measured in a browser instead. See ADR 22.
