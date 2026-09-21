# 10. Danish is the product, English is the code

Date: 2026-09-08
Status: Accepted

## Context

The app is Danish, because its user is. The code is not read by that user — it is read by
whoever maintains it, including language models, whose training and whose tooling are
English. For most of the first month the two were not separated: comments, plans and test
names were written in whatever language the session happened to be in, and the split ended up
following the *age* of a file rather than any rule.

## Decision

Two languages, with a boundary drawn on purpose:

| Surface | Language |
| --- | --- |
| C#, TypeScript and HTML comments and identifiers | English |
| Test method names | English |
| `CLAUDE.md`, `HANDOFF.md`, `README.md`, new plans and these records | English |
| `da.json` | Danish, and the **source** |
| `en.json` | Danish translated |
| String literals asserting on the app's Danish surface | Danish |
| `docs/plans/*.md` delivered before 2026-09 | Danish, frozen as an archive |

Every user-facing string is a translation key, including `aria-label` and `title`, and a key
must exist in both files or a parity test fails. `da.json` being the source means Danish is
written first and English follows it, not the other way round.

`SourceLanguageTests` enforces the code half: it classifies every comment block by scoring
function words and fails on Danish ones, naming file and line.

## Alternatives rejected

**English throughout, including the UI.** The product would then be translated away from its
only user.

**Danish throughout.** Comments and identifiers would sit outside every tool and every
reference that discusses the code.

**Leave it mixed.** It was already mixed, and the boundary was drifting by file age, which
means it would have kept drifting.

## Consequences

- There are no Danish identifiers anywhere — not a method, class, variable or `data-testid`.
  The Danish remaining in C# is string literals asserting on the app's Danish surface, and
  those stay.
- Date formatting uses `Intl.DateTimeFormat` with the active language rather than Angular's
  `DatePipe`, because `LOCALE_ID` is bound at startup and cannot change at runtime.
- API errors carry `{ code, message }`, where `code` is a translation key and `message` is an
  English fallback for logs. A translated error message is therefore never produced by the
  server.
- The Danish documentation is a known backlog, not a counter-example: the plan archive is
  frozen deliberately, and `CLAUDE.md` and `HANDOFF.md` are still Danish and recorded as
  such.
