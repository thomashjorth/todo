# 8. Signal-based stores own all HTTP

Date: 2026-08-13
Status: Accepted

## Context

The app has one user, one machine and no shared state between screens beyond the task list
itself. Angular's own signals cover that. A state library would be a layer looking for a
problem the app does not have.

## Decision

Feature stores are plain injectable services holding signals, and they own every HTTP call.
Components never inject a generated API client and never call `.subscribe()`. A component
reads signals and calls store methods.

Not NgRx. The decision is recorded in four places in the repository precisely because it is a
choice rather than an omission.

## Alternatives rejected

**NgRx.** Actions, effects, reducers and selectors per feature, for one entity and one user.
The ceremony buys traceability and time-travel debugging that nothing here needs.

**Components calling the generated client directly.** Cheapest to write and the reason the
rule is written down: it puts the same request in several places and gives the list no single
owner.

## Consequences

- A store method that sets a signal and then reloads has to defend against out-of-order
  responses itself. Two reloads can be in flight at once, and the framework will not
  sequence them; `load()` carries a sequence counter so only the newest write wins.
- Because the store reloads after a write, a value read straight after a save is already the
  server's answer rather than local state.
- There is no framework-imposed shape, so consistency between stores is a convention held by
  review rather than by the compiler.
