# 21. Fake external servers are real Kestrel instances in the test process

Date: 2026-08-18
Status: Accepted, replaces the WireMock plan in the original design

## Context

The original design said contract tests would use WireMock with fixtures recorded from the
real servers. By the time Jira import was built, a better option was available for free:
because the host runs **inside the test process** (ADR 5), anything else listening on loopback
is reachable by the host's own `HttpClient`.

## Decision

`FakeJira` and `FakeAdo` are ordinary ASP.NET Core applications on `127.0.0.1`, started by
the test. A test stores the fake's base URL as the source's configured URL, and then the whole
chain runs for real — the WIQL query, the work item type filter, the note field mapping, the
deduplication and the derived deadline — with nothing intercepted.

Route interception stays, for one job it is better at: staging a **specific field
combination** that the real server could never produce, such as one row with a deadline and
another without when the day count is a single setting for all rows. The rule is: real fake
for the journey, interception for the branch.

A fake must emit the foreign system's **actual** wire format, not .NET's. Jira sends the basic
offset form `+0200`, which `System.Text.Json` rejects on a `DateTimeOffset` property; a fake
typed with `DateTimeOffset` would emit the extended form, pass, and leave the real instance to
throw on every issue.

## Alternatives rejected

**WireMock with recorded fixtures.** A dependency to maintain, and recorded fixtures rot
silently when the foreign system changes. It also only ever replays one answer, so the code
between the request and the screen is never exercised.

**Intercepting every call at the HTTP client.** Measures the one response and nothing the
application does with it.

## Consequences

- Three things can only be measured this way: that a type filter holds all the way to the
  task list, that a derived deadline is the server's arithmetic, and that "imported earlier"
  is the app's own deduplication.
- The fake's **types are themselves a guard**. `FakeAdo` emits timestamps as `string`
  precisely so a work item can carry an unparseable value; typed as `DateTimeOffset`, the
  compiler would refuse the fixture and the gap would look as though it did not exist.
- The fakes are code that has to keep matching two external systems, and nothing tells us
  when they stop. What the real instances actually answer is measured by hand and written
  down.
