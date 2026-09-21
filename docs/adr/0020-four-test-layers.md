# 20. Four test layers, and a guard has to be seen to fail

Date: 2026-08-13
Status: Accepted

## Context

This app has one developer and no reviewer. A test that is green for the wrong reason is
therefore not caught by anyone downstream — it simply stands, and something later is built on
top of it.

That has happened here often enough to be a design input rather than an anecdote: assertions
that could not fail, fixtures ordered so an unsorted list would pass, guards measuring a
mutation other than their own.

## Decision

Four layers, each answering what the layer below cannot:

| Project | What it runs against |
| --- | --- |
| `Todo.Core.Tests` | Pure functions, injected clock, no I/O |
| `Todo.Api.Tests` | A real host, a real database, real HTTP |
| `Todo.E2E` | Playwright against the running host |
| Vitest | Angular components and stores |

And one discipline that outranks coverage: **a guard test must be seen to fail.** Break what
it protects, confirm it goes red at the right step and for the right reason, restore. A test
nobody has watched fail proves nothing.

Two corollaries follow from having watched these fail badly:

- **Mutate one condition at a time.** A mutation changing two things measures neither, and
  reads as a pass.
- **Write a mutation measurement as identities, not as a count.** "This makes exactly one
  test red" is a claim about the suite's composition, and the same commit that adds a second
  test makes it false.

Test-data builders write straight to the database and skip the API's validation, so they are
for *arrange* only, never for the action under test.

## Alternatives rejected

**Unit tests and a smoke test.** Most of the real defects here were in wiring — a route
constraint, a serialised enum, a colour resolved against the wrong parent — which unit tests
are structurally unable to see.

**Coverage as the target.** Coverage counts executed lines and cannot distinguish an
assertion that could fail from one that could not.

## Consequences

- The suite is slow by unit-test standards, because two layers start a real host.
- `Check.cmd` runs the steps in a load-bearing order: the E2E project does not build Angular,
  so without the frontend build first, Playwright measures the previous build and nothing
  looks wrong.
- The test counts are recorded, for one reason: after a refactor, a changed count means a
  test was lost or duplicated.
- Rules whose branch order is the decision — ADR 12 and ADR 15 — are held here or nowhere,
  because both orderings compile and read correctly.
