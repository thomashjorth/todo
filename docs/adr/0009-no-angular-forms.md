# 9. No Angular forms — the app has fields, not forms

Date: 2026-08-21
Status: Accepted

## Context

By August 2026 five screens had been built with native inputs, and the question was whether
to adopt Angular's forms — the older `FormsModule` and `ReactiveFormsModule`, or the newer
signal forms.

Maturity was ruled out first, by measurement rather than by reputation: in Angular 22.1.1 the
package exports `./signals` and `./signals/compat`, and `form`, `required`, `validate`,
`validateAsync`, `validateHttp` and `validateTree` carry no `@experimental` marking. "Too
new" is therefore not the argument, and saying so keeps it from being rediscovered as one.

## Decision

No Angular forms of either kind. Fields are native inputs bound with `[value]` or `[checked]`
plus `(input)`, `(change)` or `(blur)`, and the state lives in the stores. `@angular/forms`
remains a dependency but is unused — there is no `FormsModule`, `ReactiveFormsModule`,
`ngModel` or `FormControl` anywhere in `src`.

The reason is shape. The app **saves per field** on blur or change and has no form submit,
and **validation lives on the server**, returning error codes the frontend translates. A
forms library is built around a form model with a submit and client-side validity. There is
no submit here to hang it on.

## Alternatives rejected

**Signal forms.** Would touch 40+ fields across six screens and around 90 test calls that set
`.value` and dispatch an event — to relocate a validation the server owns.

**The older reactive forms.** Same mismatch, and a heavier one.

## Consequences

- There is no framework between a field and the store, which is the point; there is also no
  framework catching what the hand-written binding gets wrong.
- The measured price is a real one. `[checked]` is only re-applied when the signal *changes*,
  so when a save is rejected the signal goes `false` to `false`, the binding has nothing to
  do, and the checkbox stays ticked while nothing was stored. This has caused one real bug,
  found by an end-to-end journey rather than by a unit test, and the pattern is still present
  in two other checkboxes that have no coded reason to be rejected today.
- Nothing in the app can be validated before it is sent, so every rule lives in one place —
  the server — and the frontend's job is to show what came back.
