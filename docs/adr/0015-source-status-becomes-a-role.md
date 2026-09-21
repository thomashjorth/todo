# 15. A source status becomes a role, resolved in one ordered place

Date: 2026-08-19
Status: Accepted

## Context

Jira and Azure DevOps each have their own vocabulary of statuses, configured per instance,
and the user names which of them mean what. Those names can overlap: a status can be in the
"waiting" list *and* in the "done" list, or in the "waiting" list *and* the on-duty list.

The importer and the preview both have to answer the same question about the same status, so
the answer cannot live in two places.

## Decision

A source status is translated into a **role** by one function per source, and the role is an
enum rather than a boolean.

- `JiraStatusRoles.For` answers `Done`, then `Duty`, then `Waiting`, then `Actionable`.
- `AdoStateRoles.For` answers `Done`, then `Waiting`, then `Actionable`.

The order is the decision, twice over:

- **Done outranks everything.** A resolved issue is not waiting on anyone, so if the two
  branches were swapped it would be marked as work someone owes you and the offer to close
  it would be hidden behind the waiting branch.
- **Duty outranks waiting.** The same status means *waiting on the pool* when you are not on
  duty and *waiting on you* when you are, so the on-duty switch decides, not the status.

An enum rather than a boolean because a status can belong to two lists, and a boolean has no
way to say which one won. ADO's was a `bool` until "done" arrived, and the class documentation
had predicted the change word for word.

## Alternatives rejected

**A boolean per question.** Cannot express precedence, and the precedence is the whole
problem.

**Resolving in the caller.** Preview and import would each have their own copy, free to
disagree, and a disagreement would show as an issue behaving differently before and after
import.

## Consequences

- Both outcomes of a swapped branch are legal roles, so a reordering compiles and reads
  correctly. Only tests naming the specific pair can see it, and they exist for that reason.
- Empty "done" lists are a valid state meaning "suggest nothing", unlike an empty work item
  type list, which means "restore the defaults".
- One state is hard-coded rather than configured: ADO's `Closed` is done without appearing in
  any list, because the query that offers state names excludes it, so it can never be chosen.
