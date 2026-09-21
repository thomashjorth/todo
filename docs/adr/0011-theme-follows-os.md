# 11. The theme follows Windows, and there is no switch

Date: 2026-08-14
Status: Accepted

## Context

The app runs inside a WebView2 window on one machine, beside applications that already follow
the system theme. A theme of its own would be one more thing to set, store, migrate and keep
in step with the rest of the desktop.

## Decision

Dark mode follows the operating system through `prefers-color-scheme`. There is no toggle, no
setting and nothing persisted. Tailwind's `dark:` variant is the only mechanism, and every
colour utility is written with both directions present.

`color-scheme` is declared on the **root** element, in both directions at once, because the
app cannot know which one applies. That is what makes the browser's own surfaces — scrollbars
and the canvas — follow the theme.

## Alternatives rejected

**A theme switch in settings.** A field on the contract, a row in `Setting`, a round trip and
a migration, to override a preference the user already expressed to Windows.

**Light only.** The app sits open beside a dark editor all day.

## Consequences

- Both directions of every colour are always in the bundle, because there is no runtime
  branch that could drop one.
- A pair is not the same step on both sides. Muted text is `text-gray-500 dark:text-gray-400`,
  because `gray-500` passes AA on white but fails on `gray-900`. Swapping mechanically
  between the two breaks one of them.
- Contrast must be verified in **both** themes, which is why the guard walks every screen
  twice. See ADR 22.
- `color-scheme` does not reach a `<select>` popup in WebView2 — measured twice, both
  resolved and written out explicitly. A `<select>` therefore paints its own background
  rather than inheriting a transparent one.
