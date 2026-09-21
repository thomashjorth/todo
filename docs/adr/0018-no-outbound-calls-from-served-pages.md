# 18. A page the app serves may not call out, and a guard proves it

Date: 2026-08-17
Status: Accepted

## Context

The app runs on a laptop that may have no network. A page that quietly fetches a font, a
script or an analytics beacon from someone else's server is slow when the network is bad and
broken when there is none — and it tells a third party when the user opened it.

The API documentation page is the case that made this concrete. Scalar renders the contract
beautifully and, by default, reaches out for fonts and for an assistant feature.

## Decision

Everything a served page needs is served by the app. Scalar runs with `.DisableAgent()` and
`.DisableDefaultFonts()`, and the documentation reads `/openapi/contract.yaml` — the contract
file embedded in `Todo.Host` as a resource, not read from disk, because a published
executable has no `contracts\` folder beside it.

The rule is enforced at **route level**: `ApiDocsJourneyTests` blocks every request outside
the app's own origin and asserts that none was attempted.

## Alternatives rejected

**Reviewing the HTML and the bundle for foreign hostnames.** Both checks were run and both
passed, while the page was fetching `api.scalar.com` from JavaScript after mount and loading
`@font-face` from `fonts.scalar.com`. Static inspection cannot see a request that a script
makes later.

**Dropping the documentation page.** It is the payoff for ADR 2: the contract is the
documentation, and a contract nobody can read is half a decision.

## Consequences

- The app has **two** OpenAPI documents on purpose, and neither is a duplicate of the other:
  the runtime derivation is what the drift test measures against, and the embedded contract
  is what the documentation page shows.
- Any future page that wants a CDN has to bring the asset into the app instead.
- The guard is about behaviour, not markup, so it keeps working when a library changes how it
  loads things.
