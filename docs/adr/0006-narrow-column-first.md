# 6. The narrow column is the design target, and the wide layout is the addition

Date: 2026-08-13
Status: Accepted

## Context

The app is meant to stand open beside the work all day — a column next to an editor or a
browser, not a window you switch to. That makes width the scarcest resource in the design,
and it makes it scarce permanently rather than occasionally.

## Decision

The primary target is a column of roughly **480 px**, a quarter of a 1080p screen. The
unprefixed Tailwind classes *are* the narrow layout; `sm:` and `md:` are only ever used to
widen. From `xl` — 1280 px, written as `80rem` — the task list becomes two columns with the
detail panel beside the list.

The detail panel is rendered in exactly one place, chosen by a signal rather than by CSS. A
`hidden xl:block` pair would leave both copies in the DOM.

## Alternatives rejected

**Design for a normal window and let it shrink.** Responsive shrinking produces a layout that
is tolerable narrow rather than good narrow, and narrow is the case that matters here.

**Narrow only.** The app is also used maximised, and at that width a single 480 px column in
the middle of a wide screen wastes the room it was given.

## Consequences

- Width forces the priority: at 480 px only what matters fits, so the narrow layout is where
  the editing decisions get made.
- Almost the whole test suite runs at 480. Anything behind `xl:` is only measured by the
  tests that ask for the wide viewport, and a test that does not state a width gets
  Playwright's 1280 default — which is the *wide* layout.
- Because the panel moves rather than duplicates, its position is application state rather
  than a stylesheet concern, and the breakpoint is written once in TypeScript.
