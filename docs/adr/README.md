# Architectural decision records

Why this app is shaped the way it is. Each record owns one standing decision: the context it
was taken in, what was chosen, what was rejected, and what the choice costs today.

These describe the **current state**. A decision that was later reversed appears only where it
explains something you can see now — identifiers are `long` because they used to be `Guid`
(ADR 4), and the app does not start with Windows because autostart was built and removed
(ADR 19). The route there is in `git log` and in `docs/plans/`, not here.

**Where the other documents sit.** `docs/plans/2026-08-13-todo-app-design.md` is the design
and the delivery plan; `docs/HANDOFF.md` is the current state and the next steps; `CLAUDE.md`
is conventions and measured traps. These records hold the *why* for the decisions listed
below, so a decision is explained here and pointed to from there.

## Foundation

| # | Decision |
| --- | --- |
| [1](0001-single-process-photino-shell.md) | One process: a Photino window over ASP.NET Core |
| [2](0002-contract-first-openapi.md) | Contract-first: the OpenAPI file owns the API |
| [3](0003-sqlite-in-appdata.md) | SQLite in `%APPDATA%`, migrated at startup, with a backup that can refuse |
| [4](0004-long-identifiers.md) | Identifiers are `long`, assigned by SQLite |
| [5](0005-headless-host.md) | The host runs without a window, from day one |

## User interface

| # | Decision |
| --- | --- |
| [6](0006-narrow-column-first.md) | The narrow column is the design target, and the wide layout is the addition |
| [7](0007-tailwind-only.md) | Only Tailwind utility classes, with two measured exceptions |
| [8](0008-signal-stores.md) | Signal-based stores own all HTTP |
| [9](0009-no-angular-forms.md) | No Angular forms — the app has fields, not forms |
| [10](0010-danish-product-english-code.md) | Danish is the product, English is the code |
| [11](0011-theme-follows-os.md) | The theme follows Windows, and there is no switch |
| [23](0023-keyboard-shortcuts.md) | Keyboard shortcuts activate, in two layers, from a registry |

## Domain

| # | Decision |
| --- | --- |
| [12](0012-derived-state-is-computed.md) | Derived state is computed, never stored |
| [13](0013-delegation-is-a-shortcut.md) | Delegation is a shortcut, not a state |
| [14](0014-read-external-enrich-locally.md) | Read external, enrich locally — nothing is written back |
| [15](0015-source-status-becomes-a-role.md) | A source status becomes a role, resolved in one ordered place |
| [16](0016-weekly-review-is-computed.md) | The weekly review is owed by calculation, not by a timer |

## Security and operations

| # | Decision |
| --- | --- |
| [17](0017-credentials-in-clear-text.md) | Credentials live in the database in clear text |
| [18](0018-no-outbound-calls-from-served-pages.md) | A page the app serves may not call out, and a guard proves it |
| [19](0019-publishing-and-no-autostart.md) | Publishing is two files, and the app does not start with Windows |

## Testing

| # | Decision |
| --- | --- |
| [20](0020-four-test-layers.md) | Four test layers, and a guard has to be seen to fail |
| [21](0021-fake-servers-are-real-kestrel.md) | Fake external servers are real Kestrel instances in the test process |
| [22](0022-accessibility-measured-in-browser.md) | Accessibility is measured in a browser, not reviewed in the markup |

## Writing a new one

Number it next, name the file after the decision rather than the feature, and keep the four
headings: **Context**, **Decision**, **Alternatives rejected**, **Consequences**. Consequences
stay at the level of the decision — measured traps and the commands that reproduce them belong
in `CLAUDE.md`, which is the one place that is loaded into every session.

Superseding a record means saying so in the new one's `Status` line and in the old one's, not
deleting the old file.
