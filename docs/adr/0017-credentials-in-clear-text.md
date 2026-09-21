# 17. Credentials live in the database in clear text

Date: 2026-08-14
Status: Accepted

## Context

The app needs a Jira personal access token and an Azure DevOps one. Everything else it
configures — languages, URLs, aliases, statuses — already lives in the `Setting` table and is
edited on the settings page.

The trade was put on the table explicitly: **one place to administer everything**, against
**two work tokens sitting readable in a file**.

## Decision

Tokens are stored in the `Setting` table in clear text, like every other setting. The
frontend never sees a token: it writes one, and after that only C# attaches it to outgoing
requests. `SettingsResponse` carries `hasJiraToken`, never the token.

A secret field gets its own endpoint. `PUT /api/settings` is a full replacement that reads an
absent field as "clear", so a token carried in that payload would be wiped every time the
user saved something else on the page. Tokens therefore have `PUT` and `DELETE` routes of
their own.

`ICredentialStore` remains as an interface with a database-backed implementation, so changing
this decision means replacing one class rather than every call site.

## Alternatives rejected

**Windows Credential Manager.** Correct, and it splits configuration across two places with
two editing experiences, for a single-user app on a machine the user already controls.

**Encryption at rest with a key beside it.** The key would live in the same place as the
data, which is ceremony rather than protection.

## Consequences

- `%APPDATA%\TodoApp\todo.db` grants access to Jira and Azure DevOps. The folder must not be
  synchronised to OneDrive or anything like it.
- The `.bak-*` copies a migration leaves behind are equally secret. See ADR 3.
- The database file must never be attached to a bug report or committed to a repository.
- The tokens only ever need read scope, which bounds what a leak is worth. See ADR 14.
- A guard keeps real instance names out of the repository, so fixtures cannot quietly carry
  the user's own hosts.
