# 22. Accessibility is measured in a browser, not reviewed in the markup

Date: 2026-08-17
Status: Accepted

## Context

The app targets WCAG AA in both themes (ADR 11), with every action reachable from the
keyboard (ADR 23). None of that can be argued from the source. Whether text meets a contrast ratio
depends on what colour it ended up on, and that depends on every ancestor, the theme and the
utility classes that actually shipped — a question only the browser has answered.

## Decision

`ContrastTests` drives every screen in both themes, computes each text node's colour and its
effective background with `getComputedStyle`, and fails below AA, naming the element and the
ratio.

Three things the first version got wrong are now part of the decision:

- **Colours are read through a canvas.** `getComputedStyle` returns `oklch(...)` for a
  Tailwind 4 palette, and a regex over the digits reads `oklch(0.967 0.003 264.542)` as a
  blue channel of 264. The first version reported invisible text as roughly 8:1 and passed.
- **A field's value is not a text node.** Inputs and textareas have to be read from
  `el.value`, or reaching the editor is mistaken for measuring it.
- **A placeholder has its own colour**, on `::placeholder`, invisible to anything reading the
  element's own style.

The guard can only see what was rendered, so a conditional branch is unmeasured until a
fixture puts the app in that state and the journey opens it.

## Alternatives rejected

**An accessibility linter over the templates.** Sees classes, not resolved colours, and the
resolved colour is the question.

**Reviewing colours by eye against a palette table.** That is how `gray-400` came to be
recorded as 2.85:1 — a Tailwind 3 hex value — when Tailwind 4's oklch definition makes it
2.60:1.

## Consequences

- Adding a conditional line to a screen costs a fixture and a click in the journey, or it
  ships unmeasured.
- Fixing a background can make the guard **stricter** rather than looser: an opaque surface
  stops the walk up the ancestors, so text inside it is suddenly measured against the right
  colour. Progress shows as which findings changed, not as the count going down.
- Some things remain outside any guard, because they are the browser's own windows rather
  than DOM nodes — a `<select>` popup is not in a screenshot. Those are confirmed by the user
  and recorded as such.
