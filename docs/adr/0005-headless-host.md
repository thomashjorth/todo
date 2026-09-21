# 5. The host runs without a window, from day one

Date: 2026-08-13
Status: Accepted

## Context

The UI is the point of the app, so the tests that matter most drive a browser. If the only
way to start the app were to open a Photino window, every end-to-end run would fight a real
window on the developer's desktop — and CI has no desktop at all.

This was a requirement in the first slice, not a later accommodation. Retrofitting it would
have meant unpicking startup after everything already depended on the window being there.

## Decision

`Todo.Host` takes `--headless` and starts Kestrel without opening a window. Tests do not
shell out at all: `RunningHost` in `Todo.TestSupport` starts the host **in the test process**,
on a free port, against a temporary database given by `--Data:Path`. Test classes inherit
`ApiTest` or `BrowserTest` rather than starting a host themselves.

## Alternatives rejected

**Test the UI only through component tests.** Vitest and the Angular compiler cannot see what
a real browser does — computed colours, view transitions, whether the browser swallows a
keyboard combination. Several of this app's guards exist because only a browser can answer.

**Launch the published executable and drive it from outside.** Slower, and it puts the real
`%APPDATA%` database one mistake away from a test run.

## Consequences

- Because the host runs inside the test process, a fake external server can be an ordinary
  Kestrel instance on loopback that the host's own `HttpClient` reaches. That is what makes
  ADR 21 possible.
- The window is not part of anything the tests assert, so window behaviour is verified by
  hand or not at all.
- Publishing verifies the same path it ships: `Publish.cmd` runs the produced executable
  headless against a temporary database before calling the publish good. See ADR 19.
