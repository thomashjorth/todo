# Consolidation, delivery 2: one source pipeline — implementation plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task.

**Goal:** Jira and Azure DevOps stop being the same design written twice — in the contract, in the
endpoints, in the stores and in the templates — so that slice 13's mentions inbox costs an adapter
rather than a vertical slice.

**Architecture:** Shared contract schemas, with `allOf` only where a source genuinely owns a field.
The paths stay as they are, so the frontend routes and `ContractDriftTests` are untouched. In C# a
template-method base class owns the flow and each source answers nine questions. In the frontend a
generic base store owns all twelve members and each source supplies a four-line port, while the
row's source-specific cells become **data** rather than markup.

**Tech stack:** OpenAPI 3.0 + NSwag (both generators), minimal APIs, Angular signal stores. No new
dependencies.

**Design document:** `docs/plans/2026-09-08-consolidation-design.md`, sections 3, 4, 5 and 8.
**Predecessor:** delivery 1, `docs/plans/2026-09-08-consolidation-1-language.md`, which made every
comment English. Write everything here in English from the first line.

**Baseline, measured with `Check.cmd` on `99e3577`:** 186 Todo.Core.Tests, 399 Todo.Api.Tests,
83 Todo.E2E, 358 Vitest, all green. CSS bundle `styles-XN6R3J65.css`, 39,059 bytes.

---

## The one thing that can fail silently

**`state` → `status` has no compiler over it.** Six hand-written wire fixtures, `ContrastTests`'
route handlers and the E2E suite's staged rows all build a raw response body by hand, so a missed
rename becomes an `undefined` that only shows up when the UI reads it. `CLAUDE.md` has predicted
"the type checker will catch it" four times and been wrong four times.

Counted, so the work can be checked off rather than eyeballed — **117 sites across 9 files**:

| file | sites |
|---|---|
| `tests/Todo.E2E/ContrastTests.cs` | 37 |
| `src/Todo.Web/src/app/ado/ado-import.spec.ts` | 22 |
| `src/Todo.Web/src/app/ado/ado-store.spec.ts` | 17 |
| `tests/Todo.E2E/AdoImportJourneyTests.cs` | 15 |
| `src/Todo.Web/src/app/ado/ado-store.ts` | 8 |
| `src/Todo.Host/Endpoints/AdoEndpoints.cs` | 6 |
| `tests/Todo.E2E/SectionTransitionJourneyTests.cs` | 6 |
| `contracts/openapi.yaml` | 4 |
| `src/Todo.Web/src/app/ado/ado-import.html` | 2 |

**And eleven sites must NOT change.** `System.State` is **Azure DevOps's own field name**, not ours:
nine in `src/Todo.Host/Ado/AdoTaskSource.cs` (`StateField`, the WIQL `[System.State]`, the hydration
field list, `item.Fields?.State`, the `[JsonPropertyName("System.State")]` binding) and two in
`tests/Todo.TestSupport/Ado/FakeAdo.cs`. Renaming one of those breaks the integration against the
real instance while every test stays green, because the fake would be renamed with it. **Our
contract's `state` becomes `status`; the foreign system's `System.State` stays.**

Likewise `AdoStateRoles.ClosedState` holds the literal ADO state name `Closed` — the value is
Azure DevOps's, even though the identifier around it is renamed in task 6.

---

## Task 1: the wire key-set guard, before anything moves

**Files:** Create `tests/Todo.Api.Tests/WireKeySetTests.cs`

The existing wire tests use `Assert.Contains` on raw JSON, so they assert **presence, not
exactness** — an extra key is invisible to them. That matters here because `allOf` makes NSwag emit
`[JsonExtensionData] AdditionalProperties` on the base class, and today's generated code has **zero**
of them. This guard has to exist before the contract changes, so it measures the shape we are
promising to keep.

**Step 1:** Write a test that drives a preview from each source through the real endpoint (the
`FakeJira` / `FakeAdo` pattern — see `AdoImportJourneyTests`), reads the **raw** response body, and
asserts the **exact key set** of one preview row against a written-down list. Not a subset. The
failure message must name the unexpected and the missing keys separately.

**Step 2:** Run it. Expected: 2 passed (one per source).

**Step 3:** **See it fail.** Add a throwaway property to the contract's `AdoPreviewRow`, regenerate,
run. It must name the new key. Revert and regenerate.

**Step 4:** Commit. `✅ The wire is asserted key for key, because Contains cannot see one too many`

## Task 2: the three schema pairs that are already identical

**Files:** `contracts/openapi.yaml`, then whatever regeneration breaks.

`ConnectionResponse` (one `displayName`), `StatusesResponse`/`StatesResponse` (one `names`) and
`ImportResponse` (four integers) are identical field for field **and type for type**. They become
one schema each — `SourceConnectionResponse`, `SourceStatusesResponse`, `SourceImportResponse` —
referenced from both sources' operations. **No `allOf`, and no wire change**: the serialised body is
identical, so no fixture moves.

**Step 1:** Edit the contract. **Step 2:** `powershell -ExecutionPolicy Bypass -File
scripts\generate-api.ps1`. **Step 3:** Fix the C# and TypeScript that named the old types — this is a
mechanical rename, and the compiler finds every site. **Step 4:** `dotnet test Todo.sln` and Vitest,
both unchanged. **Step 5:** Commit.

`♻️ Three response shapes were the same shape twice`

## Task 3: `state` becomes `status`

Its own commit, so it can be reverted alone if the real instance disagrees.

Work the table above **file by file, top to bottom**, and check each off. Read the "must NOT change"
paragraph again before touching `AdoTaskSource.cs`.

`ErrorCodes.AdoRowStateRequired` becomes `AdoRowStatusRequired`, which moves its `errors.*` key in
`da.json` and `en.json`; `ErrorCodeTranslationTests` is the guard that catches a missed one.

**Step 1:** Contract, then regenerate. **Step 2:** C#. **Step 3:** Frontend source. **Step 4:** The
fixtures, all six files. **Step 5:** `powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1`
— E2E does not build Angular, and this task changes a template. **Step 6:** `Check.cmd` in full.
Every number unchanged. **Step 7:** Commit.

`♻️ Jira says status and ADO says state for the same thing, so now both say status`

## Task 4: `SourceClosureRow` and `SourceReopeningRow`

With `state` gone, these two pairs differ in nothing. One schema each, referenced by both sources'
`ImportRequest`. Same five steps as task 2. No wire change.

`♻️ A closure row is a closure row whichever source raised it`

## Task 5: `SourcePreviewRow` and `SourceImportRow`, through `allOf`

```yaml
SourcePreviewRow:                                     # the 17 shared fields
JiraPreviewRow: allOf [SourcePreviewRow, { isDuty }]
AdoPreviewRow:  allOf [SourcePreviewRow, { workItemType }]
SourceImportRow:                                      # the 6 shared fields
JiraImportRow:  allOf [SourceImportRow, { deadline }]
AdoImportRow:   allOf [SourceImportRow, { workItemType }]
```

`JiraPreviewResponse`, `AdoPreviewResponse`, `JiraImportRequest` and `AdoImportRequest` **stay as
four schemas**. They differ only in which row type they point at, and forcing that into an `allOf`
needs a property override, which NSwag handles unreliably.

**Measured before this plan was written**, with a throwaway contract through both generators:

```
C#   public partial class JiraPreviewRow : SourcePreviewRow
TS   export class JiraPreviewRow extends SourcePreviewRow implements IJiraPreviewRow
     export interface IJiraPreviewRow extends ISourcePreviewRow
```

No discriminator, no `$type`, same `JsonPropertyName` attributes — the serialised object stays flat.
The interface inheriting matters too: the hand-written fixtures cast through those interfaces.

**Watch for the one measured side effect:** the base class gains `[JsonExtensionData]
AdditionalProperties`. Task 1's guard is what says whether it reaches the wire. If it does, the
answer is a `JsonIgnore` on the generated partial's other half, not a weakened guard.

**Step 4** additionally runs task 1's guard on its own and expects it green.

`✨ One row shape with two source-owned fields, rather than two row shapes`

## Task 6: Core — one role enum and one error-code set

- `AdoStateRole` → **`SourceStatusRole`** in `Todo.Core/Sources`, shared with Jira. ADO simply never
  returns `Duty`. `AdoStateRoles` → `AdoStatusRoles`. **The two `For` methods stay separate**,
  because the two rules genuinely differ: duty outranks waiting for Jira, and `Closed` is hardcoded
  for ADO. Do not merge them — `Done_outranks_waiting_when_a_state_is_in_both_lists` and its three
  siblings are the guard, and they are the only thing that can see a swapped branch.
- The **eleven identical error-code names** — `ExcludedDone`, `ExcludedWaiting`, `MissingInSource`,
  `NoneRecognized`, `NotConfigured`, `OutsideFilter`, `Refused`, `RowKeyRequired`,
  `RowTitleRequired`, `RowTitleTooLong`, `Unreachable` — collapse into one shared set each source
  fills in with its prefix. The `errors.<code>` keys on the wire must not change; if a rename would
  move one, keep the old string.
- `ProjectKeyRequired` / `ProjectRequired` become one name.

`ErrorCodeTranslationTests` enumerates `ErrorCodes` by reflection and demands a key in **both**
language files, so it catches a translation left behind. It cannot catch a *changed* code string
reaching a client that still sends the old one — but nothing outside this repo does.

`♻️ A role is a role and eleven error codes were written twice`

## Task 7: C# — one template method, two fillings

```
Todo.Host/Sources/SourceImportFeature.cs      ~550 lines   routes, flow, the shared guards
Todo.Host/Jira/JiraImportFeature.cs           ~120 lines
Todo.Host/Ado/AdoImportFeature.cs             ~120 lines
```

An abstract base class rather than static helpers: the base owns the **flow**, the subclass owns
**every decision**, and the compiler makes a new source answer all of them.

The nine decisions a subclass answers: the route slug, the error-code set, reading settings,
`NotReady`, `StatusFor`, `ReopenTargetFor`, decorating a preview row with its source-owned field,
the extra row validations, and **`DoneAtAsync`**.

**`DoneAtAsync` is the one seam that is not naming.** Measured on the two implementations:

```
Jira:  DoneAt = ... ? AsUtc(await source.FetchStatusChangedAtAsync(key)) : null
ADO:   DoneAt = ... ? AsUtc(item?.StatusChangedAt) : null
```

That is `ITaskSource`'s documented rule — read the row's field first, call the fallback only when it
is null — and **ADO must deliberately not chase**, guarded by
`An_unreadable_state_change_date_is_not_chased_with_a_second_call`. Put the difference behind this
method and leave that guard alone.

Everything else that differs between the two files is type names and error codes. Measured with
comments stripped: seven members have **exactly the same length and a different body** for that
reason alone, `CloseAsync` differs only by a wrapped variable declaration, and `ValidateRow` differs
only by ADO's extra `workItemType` branch. **No accidental drift was found**, so a difference you
cannot explain is a signal to stop and measure rather than to unify.

`JiraEndpointsTests` (1432 lines) and `AdoEndpointsTests` (1664) are the safety net under this task.
They test observable behaviour through HTTP, which does not change, so **they should not need
editing**. If one needs a change beyond a renamed type, that is the interesting event of this
delivery — report it rather than adjusting the test.

`♻️ The two import features were one design written twice`

## Task 8: the frontend base store

The two stores have the same twelve members in the same order. A generic abstract base carries them;
each subclass supplies a four-line port, because OpenAPI requires operation ids to be unique so the
generated methods keep source-specific names:

```ts
protected readonly port = {
  testConnection: () => this.api.testJiraConnection(),
  listStatuses:   () => this.api.listJiraStatuses(),
  preview:        () => this.api.previewJira(),
  import:  (b: JiraImportRequest) => this.api.importJira(b),
};
```

`retro-store` and `review-store` do **not** join. Retro is a pasted CSV, not a source.

`♻️ Twelve store members were written twice`

## Task 9: the shared import screen

The two templates are 78% identical. What differs is the row's sub-line — Jira shows status, deadline
and a duty badge; ADO shows work item type, status, and deadline or "no deadline" — and all of them
are the same markup with a different key. So the source-specific part becomes **data**:

```html
@for (fact of facts(row); track fact.testid) {
  <span [attr.data-testid]="fact.testid" class="block text-xs text-gray-500 dark:text-gray-400"
    >{{ fact.key | transloco: fact.params }}</span>
}
```

Translation stays in the template as `| transloco`, so a language switch is still reactive. **No
`<ng-template let-…>`** — the conventions forbid it because its context type is `any`.

**Every `data-testid` must survive unchanged**, so the E2E screens do not move — except
`open-issue`/`openIssue`, which become `open-item`/`openItem` as ADO already spells them.

**Delivery 1 measured two ways a comment can move the CSS bundle**, and this task rewrites templates:
a class name that loses its spelling disappears from the bundle (four names live only in comments:
`min-h-0`, `outline-2`, `outline-offset-2`, `pr-6`), and an English prose word that happens to be a
utility creates one (`container`, `grow` cost +359 bytes once). Check the bundle against **39,059
bytes** before committing. The full mechanism is in delivery 1's Task 8.

`♻️ One import screen with the source-specific cells as data`

## Task 10: the accordion, and one dead dependency

`settings-section` and `review-section` are 36 of 37 HTML lines and 31 of 44 TypeScript lines
identical. One `layout/collapsible-section`, used by both with their own content. The chevron stays a
text glyph with `aria-hidden="true"` — that is a **testing** decision, measured: an `<svg>`
contributes nothing to the accessible name, so the guard could not have been written against one.

`@angular/forms` is removed: measured across all of `src`, there is no `FormsModule`,
`ReactiveFormsModule`, `ngModel` or `FormControl` anywhere.

`🔥 One accordion, and a dependency nothing imports`

## Task 11: the suite, the numbers, the documentation

**Step 1:** `powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1`, then `Check.cmd`.

**Step 2:** compare against the baseline. **The expectation is +2 in `Todo.Api.Tests` and nothing
else** — task 1's two facts. Everything this delivery does is a move, not a behaviour change, so
every other number must be identical.

That expectation is the deliverable's own guard. **A number that moves for any other reason means a
test was lost in the consolidation**, which is precisely the failure this refactoring risks: two test
files that were near-duplicates are the safety net, and quietly deleting half of one would look like
tidying.

**Step 3:** update the four counts in `CLAUDE.md`'s "Testtal" and in `docs/HANDOFF.md`, and say in
one sentence why. Leave both files Danish — delivery 3 translates them.

**Step 4:** `docs/HANDOFF.md`'s "Næste skridt" says slice 13 is the mentions inbox and that two kinds
of mention come from two sources. Add one sentence recording what a third source now costs: an
adapter and its source-owned fields, rather than a contract slice, an endpoint file, a store, a
component and two thousand lines of tests.

`📝 One pipeline, two sources, and the numbers say nothing was lost`

---

## What this delivery deliberately does not do

- **It does not merge the routes.** `/api/jira/*` and `/api/ado/*` stay. Merging them would collapse
  the four envelopes too, but it changes the wire everywhere and rewrites the app's most heavily
  tested area for a saving the `allOf` split already mostly banks.
- **It does not merge `JiraStatusRoles.For` with `AdoStatusRoles.For`.** The two rules genuinely
  differ and both differences are load-bearing.
- **It does not touch `retro`.** A pasted CSV is not an external source, and `ITaskSource` does not
  fit it.
- **It does not touch `CLAUDE.md` beyond the four numbers.** Delivery 3 translates and cuts it, and
  goes last because this delivery makes several of its sections wrong — the Jira/ADO paragraphs
  under "Konventioner" especially.
