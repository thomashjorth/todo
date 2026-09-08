# Consolidation: one language, one source pipeline — design

Decided 2026-09-08. Status: approved section by section; implementation follows in three deliveries.

The ask, in the user's words: clean up and streamline, so the project is stringent in its choices and
its architecture, and not mixed in language.

This is the first delivery in this repository that changes nothing a user can see. Everything below
is either a name, a comment, a schema shape, or a file that exists twice. That makes the test suite
the safety net rather than the deliverable: 186 Core, 398 Api, 83 E2E and 358 Vitest tests are green
today, and nearly all of them must still be green afterwards, unchanged.

## 1. The decisions, and who made them

All the user's, made 2026-09-08.

1. **English everywhere in code and prose.** The Danish user-facing strings stay Danish — `da.json`
   remains the source and `en.json` the translation — because the product is Danish and its author
   is its only user. The codebase is not the product.
2. **Jira and ADO share a base schema through `allOf`.** The paths stay as they are. The one wire
   change is ADO's `state`, which becomes `status`.
3. **`CLAUDE.md` is translated and cut**, roughly 1300 lines to 650.
4. **The accordion becomes one component**, and the unused `@angular/forms` dependency goes.
5. **`docs/plans/` is frozen as a dated Danish archive.** New plans are English. Thirty delivered
   plans totalling 18,120 lines are not worth re-typing, and every re-typed line is a chance to make
   a historical measurement wrong.
6. **Test folders and plan filenames are left alone.** Both are churn — 67 file moves and 30 renames
   — with no change to a single line of behaviour.

## 2. The measurements, done rather than guessed

Everything in this section was measured on 2026-09-08 against the working tree at `d7f968b`.

**The language split follows time, not a rule.** C# is English in 147 of 149 files; the two
exceptions are `LongIdMigrationTests.cs` and `TaskListScreen.cs`. TypeScript is split 16 Danish
against 46 English, and the boundary is age: `ado/`, `jira/`, `review/`, `settings/` and `layout/`
are English, while `tasks/`, `shortcuts/` and `app.ts` are Danish. `task-list.spec.ts` is mixed
inside one file — 33 Danish comment lines against 37 English. Every HTML template comment is Danish.
`README.md` is English while `HANDOFF.md` and every plan are Danish.

**There are no Danish identifiers anywhere.** Not one method, class, variable or `data-testid`. This
was the measurement that shrank the job: the Danish in C# is entirely string literals asserting on
the app's Danish surface — `"Opgavestiller: Mette Kirkegaard"`, `"Indstillinger"`,
`"Gennemgang forfalden"`. Those are data about the product, not code, and they stay.

**The two import features are the same design written twice, not copied code.** Line by line, with
`Jira`/`Ado` normalised away, 538 of `JiraEndpoints.cs`'s 745 lines match `AdoEndpoints.cs`. But
compared member by member with comments stripped, only 18 lines are byte-identical
(`Refused`, `AsUtc`, `ImportedKeysAsync`, `TitleMaxLength`). Seven more members have **exactly the
same length and a different body** — `NotConfigured`, `NotReady`, `StatusFor`, `ReopenTargetFor`,
`ValidateClosure`, `ValidateReopening`, `ReopenAsync` — where the difference is entirely type names
and error codes.

**No accidental drift was found.** The two members whose line counts differ were both diffed in
full. `CloseAsync` differs by a single wrapped variable declaration and nothing else. `ValidateRow`
differs by ADO's extra `workItemType` branch, which is a real source-specific field.
`ReconciledRowsAsync` is 85% similar and its four differences are all explained — line wrapping, a
differently worded error string, the known source-specific fields, and one that matters:

```
Jira:  DoneAt = ... ? AsUtc(await source.FetchStatusChangedAtAsync(key)) : null
ADO:   DoneAt = ... ? AsUtc(item?.StatusChangedAt) : null
```

That is not drift. It is `ITaskSource`'s documented rule — read the row's field first, call the
fallback only when it is null — and ADO must deliberately not chase, guarded by
`An_unreadable_state_change_date_is_not_chased_with_a_second_call`. **The seam of the shared
template goes exactly there.**

**Of the nine contract schema pairs, only three are identical all the way down.** The earlier count
of five was a field-*name* comparison and was wrong: `PreviewResponse` and `ImportRequest` share
their field names but point at different row types.

| pair | shared fields | Jira only | ADO only |
|---|---|---|---|
| `ConnectionResponse` | `displayName` | — | — |
| `StatusesResponse` / `StatesResponse` | `names` | — | — |
| `ImportResponse` | four integers | — | — |
| `PreviewResponse` | 3 | — | — (differs: `rows` item type) |
| `ImportRequest` | 3 | — | — (differs: three item types) |
| `PreviewRow` | 16 | `isDuty`, `status` | `state`, `workItemType` |
| `ImportRow` | 5 | `status`, `deadline` | `state`, `workItemType` |
| `ClosureRow` | 3 | `status` | `state` |
| `ReopeningRow` | 2 | `status` | `state` |

`status` against `state` is the same concept spelled two ways in four schemas. It is the only
recurring divergence, and unifying it is what lets `ClosureRow` and `ReopeningRow` collapse to one
schema each.

**NSwag generates real inheritance from `allOf`, in both generators.** This was the load-bearing
unknown, and it was measured with a throwaway contract rather than read about. Note that the six
`allOf` already in `contracts/openapi.yaml` are **not** precedent for this: they are the
single-`$ref` idiom for attaching a `description` to a `$ref` under OpenAPI 3.0, which is a
different thing entirely.

```
C#   public partial class JiraPreviewRow : SourcePreviewRow
TS   export class JiraPreviewRow extends SourcePreviewRow implements IJiraPreviewRow
     export interface IJiraPreviewRow extends ISourcePreviewRow
```

The interface inheriting matters too: the hand-written wire fixtures cast through those interfaces.
No discriminator and no `$type` is emitted, so the serialised object stays flat — which is the whole
promise of choosing `allOf` over one merged route.

**But the base class gains `[JsonExtensionData] AdditionalProperties`, and today's generated code
has zero of them.** That is a real side effect of `allOf`, and section 8 turns it into a guard.

**The frontend duplication is the same shape.** `jira-store.ts` and `ado-store.ts` have the same
twelve members in the same order; only `statuses`/`loadStatuses` against `states`/`loadStates`
separates them, and that name falls with the `status` rename. The two templates are 78% similar
structurally, and what separates them is the row's sub-line: Jira shows status, deadline and a duty
badge; ADO shows work item type, state, and deadline or "no deadline". Every one of them is the same
markup — `<span class="block text-xs text-gray-500 dark:text-gray-400">` — with a different key.

| pair | lines | identical after normalising |
|---|---|---|
| `JiraEndpoints.cs` / `AdoEndpoints.cs` | 745 / 969 | 538 |
| `JiraEndpointsTests.cs` / `AdoEndpointsTests.cs` | 1432 / 1664 | 866 |
| `jira-import.ts` / `ado-import.ts` | 281 / 315 | 243 |
| `jira-import.html` / `ado-import.html` | 263 / 342 | 201 |
| `jira-store.ts` / `ado-store.ts` | 246 / 327 | 182 |
| `settings-section` / `review-section` | 81 / 93 | 67 |

**`ErrorCodes` is almost parallel already.** Eleven names are identical across the two families:
`ExcludedDone`, `ExcludedWaiting`, `MissingInSource`, `NoneRecognized`, `NotConfigured`,
`OutsideFilter`, `Refused`, `RowKeyRequired`, `RowTitleRequired`, `RowTitleTooLong`, `Unreachable`.
Two pairs are one concept under two names: `ProjectKeyRequired` / `ProjectRequired`, and
`RowStatusRequired` / `RowStateRequired`.

**`AdoStateRole` is a strict subset of `JiraStatusRole`** — the same roles minus `Duty`.

**The 102 "unused" i18n keys are a false positive.** They are built at runtime from a prefix plus a
code, a bucket name, or a plural suffix. There is no dead translation. Do not chase this again.

## 3. The contract

Eighteen Jira/ADO schemas become fifteen, but the count is not the point. The point is that **every
shared field and every field description comes to exist once**. The 621 lines of schema block become
roughly 360.

```yaml
# Shared outright — identical field for field and type for type
SourceConnectionResponse:   { displayName }
SourceStatusesResponse:     { names }
SourceImportResponse:       { imported, skipped, closed, reopened }

# Shared because state -> status removes the only difference
SourceClosureRow:           { key, reason, status, ... }
SourceReopeningRow:         { key, status, ... }

# Base plus a thin derived type, because one field belongs to one source
SourcePreviewRow:                                     # the 17 shared fields
JiraPreviewRow: allOf [SourcePreviewRow, { isDuty }]
AdoPreviewRow:  allOf [SourcePreviewRow, { workItemType }]
SourceImportRow:                                      # the 6 shared fields
JiraImportRow:  allOf [SourceImportRow, { deadline }]
AdoImportRow:   allOf [SourceImportRow, { workItemType }]
```

`JiraPreviewResponse`, `AdoPreviewResponse`, `JiraImportRequest` and `AdoImportRequest` **stay as
four schemas**. They differ only in which row type they point at, and forcing that into an `allOf`
would need a property override, which NSwag handles unreliably. Four envelopes of three fields each
is the cheaper price.

**The paths are not touched.** `/api/jira/preview` and `/api/ado/preview` remain, each with its own
response schema. That is what keeps the frontend routes, `ContractDriftTests` and the whole `/api/`
surface out of this delivery.

**The operation ids stay as they are.** `previewJira` and `previewAdo` could not become one
`preview`: OpenAPI requires operation ids to be unique across the document. The generated clients
therefore keep source-specific method names, and section 5 pays four bound calls for it.

**The one wire change is ADO's `state` becoming `status`,** through four schemas. Section 8 says what
guards it.

## 4. The C# side

`JiraEndpoints` and `AdoEndpoints` become one template method plus two fillings.

```
Todo.Host/Sources/SourceImportFeature.cs      ~550 lines   routes, flow, the shared guards
Todo.Host/Jira/JiraImportFeature.cs           ~120 lines
Todo.Host/Ado/AdoImportFeature.cs             ~120 lines
```

An abstract base class rather than static helpers, and the choice is reasoned: the base owns the
**flow**, the subclass owns **every decision**, and the compiler makes a new source answer all of
them. That is the whole reason slice 13 gets cheaper rather than just tidier.

The nine decisions a subclass answers: the route slug, the error code set, reading settings,
`NotReady`, `StatusFor`, `ReopenTargetFor`, how a preview row is decorated with its source-specific
field, the extra row validations — and `DoneAtAsync`, which is the one seam section 2 found. Jira
chases with a second call; ADO deliberately does not.

Three things follow in `Todo.Core`:

- **`AdoStateRole` becomes `SourceStatusRole`, shared with Jira.** ADO simply never returns `Duty`.
  `AdoStateRoles` becomes `AdoStatusRoles`. The two `For` methods stay separate, because the two
  rules genuinely differ: duty outranks waiting for Jira, and `Closed` is hardcoded for ADO.
- **The eleven identical error code names** collapse into one shared set that each source fills in
  with its prefix.
- **`ProjectKeyRequired` / `ProjectRequired` become one name**, and `RowStateRequired` disappears
  with `state`.

Error codes reach the user through `errors.<code>`, so those renames touch `da.json` and `en.json` —
and `ErrorCodeTranslationTests` is already the guard that catches a missed key.

1714 lines become roughly 790.

## 5. The frontend

A generic abstract base store carries all twelve members. The subclass supplies a four-line port,
because the generated method names must stay source-specific:

```ts
protected readonly port = {
  testConnection: () => this.api.testJiraConnection(),
  listStatuses:   () => this.api.listJiraStatuses(),
  preview:        () => this.api.previewJira(),
  import:  (b: JiraImportRequest) => this.api.importJira(b),
};
```

The source-specific part of the template becomes **data rather than markup**. Each store answers with
an ordered list of `{ testid, key, params }`, and the shared template renders them in one `@for`:

```html
@for (fact of facts(row); track fact.testid) {
  <span [attr.data-testid]="fact.testid" class="block text-xs text-gray-500 dark:text-gray-400"
    >{{ fact.key | transloco: fact.params }}</span>
}
```

Translation stays in the template as `| transloco`, so a language switch is still reactive — a string
computed in TypeScript would not be. No `<ng-template let-...>`, which the conventions forbid because
its context type is `any`.

**Every `data-testid` survives unchanged**, so the E2E screens do not move — except `open-issue` and
`openIssue`, which become `open-item` and `openItem` as ADO already spells them.

The accordion becomes one `layout/collapsible-section`, used by settings and review with their own
content. `@angular/forms` is removed: measured across all of `src`, there is no `FormsModule`,
`ReactiveFormsModule`, `ngModel` or `FormControl` anywhere.

1774 lines become roughly 900.

## 6. The language rule, written down so it stops drifting

| where | language |
|---|---|
| C#, TypeScript, HTML — comments and identifiers | English |
| Test method names | English |
| `CLAUDE.md`, `HANDOFF.md`, `README.md`, new plans | English |
| `docs/plans/*.md` delivered before 2026-09 | Danish, frozen |
| `da.json` | Danish, and the source |
| String literals asserting on the app's Danish surface | Danish |

The last row is the one that needs saying out loud, because a guard that misses it forbids the one
thing that must stay.

## 7. `CLAUDE.md`

Translated and cut in the same pass, because the file is being rewritten line by line anyway. It
argues against its own size already, and it records that 265 lines were removed once for exactly
this reason.

| section | now | after |
|---|---|---|
| Three commands | 36 | 36 |
| The machine | 92 | 92 |
| Data safety | 13 | 13 |
| Conventions | 689 | ~300 |
| Test discipline | 360 | ~220 |
| Test numbers | 100 | ~15 |

Three cutting criteria, applied in this order:

1. **A lesson that now has a guard becomes one line plus the guard's name.** The guard is the durable
   record; the prose was the stand-in for it.
2. **The history of how a measurement was reached goes; the measurement stays.** "Measured twice,
   the second time after the first fix failed" is a story. "`color-scheme` does not reach a
   `<select>` popup in WebView2" is the finding.
3. **Numbers that go stale go**, except the four current test counts, which exist to make a changed
   count mean something.

Every removed lesson is named in the plan file, so nothing disappears quietly.

## 8. The guards

**`WireKeySetTests`** — the existing wire tests use `Assert.Contains` on raw JSON and therefore assert
**presence, not exactness**. An `AdditionalProperties` leaking out of `allOf` would be invisible to
them. The new test asserts the **exact key set** of a preview row from each source. Seen to fail by
adding a field.

**`SourceLanguageTests`** — the current mixture exists because nothing enforced a rule; translating
without a guard means drifting again. Built like `LineEndingTests`: `git ls-files`, extract comments
from `.cs`, `.ts` and `.html`, fail on Danish stop-words, naming file and line. **String literals are
stripped first** — otherwise `"Opgavestiller: Mette Kirkegaard"` fails it, and the one thing that must
stay Danish is the thing it forbids. It is seen to fail for free: it is red on 29 files before the
translation starts.

**The `state` rename has no compiler over it**, and it is the most dangerous part of this delivery.
Six hand-written wire fixtures, `ContrastTests`' route handlers and the E2E suite's staged rows must
all be led through by hand — `CLAUDE.md` has predicted "the type checker will catch it" and been
wrong four times. The real guard is `AdoImportJourneyTests`' genuine `FakeAdo` journey, which runs the
whole chain without interception: miss a fixture and the client reads `undefined` and the row renders
empty.

## 9. The three deliveries, and why in that order

1. **Language.** Write `SourceLanguageTests`, watch it fail on the 29 files, translate the comments,
   watch it pass. No behaviour changes, so a red test in this delivery can only be the guard itself.
2. **Source consolidation.** Contract, then `generate-api.ps1`, then C#, then frontend. Written in
   English from the first line.
3. **Documentation.** `CLAUDE.md` cut and translated, `HANDOFF.md` translated,
   `docs/plans/README.md` added.

The order is load-bearing in one place: **`CLAUDE.md` goes last**, because delivery 2 makes several of
its sections wrong. Translating it first would mean correcting it afterwards.

## 10. The price, said plainly

- **The `state` rename is a real wire change** and touches hand-written fixtures with no compiler over
  them. It is the one place in this delivery where a mistake is silent.
- **An abstract base class is a new idiom in this codebase**, which is otherwise static classes and
  records. It is justified by the compiler making a new source answer every decision, but it is a new
  thing to learn.
- **`docs/plans/` stays Danish**, so the repository has two languages in it for as long as the archive
  does. The rule is dated rather than absolute.
- **Nothing a user can see improves.** The whole delivery is paid for by slice 13 and everything after
  it.

## 11. Ruled out

- **One merged route** (`/api/sources/{source}/*`). It would collapse the two envelopes and the two
  schema families as well, but it changes the wire everywhere and rewrites the app's most heavily
  tested area — 3096 lines of endpoint tests. `allOf` buys most of it for a fraction of the risk.
- **Hand-written partial interfaces over the generated code.** Cheapest of the three, and rejected
  because it puts hand-written code on top of generated code and takes the contract's sole ownership
  of the shape away — exactly the kind of mixed choice this delivery exists to remove.
- **Mirroring the source feature folders in `Todo.Api.Tests` and `Todo.E2E`.** The convention is real
  and is kept in one project out of three, but it costs 67 file moves and changes no behaviour.
- **One naming convention for `docs/plans/`.** Four conventions coexist there and the slice numbers
  already have holes. Renaming an archive that is now frozen buys nothing.
- **Translating the 30 delivered plans.** 18,120 lines of archive, more than the whole C# codebase,
  where each retyped line is a chance to make a historical measurement wrong.
