# 2. Contract-first: the OpenAPI file owns the API

Date: 2026-08-13
Status: Accepted

## Context

One person owns both sides of this API, and both sides live in the same process. The usual
argument for a contract — independent teams that must agree before they can build — does not
apply here.

Two other arguments do. Writing the endpoint in YAML first forces its shape to settle while
it is still cheap to change, before there is code to defend. And the contract doubles as the
documentation: six months later the API is readable without reading the implementation.

## Decision

`contracts/openapi.yaml` is the truth. Endpoints are hand-written minimal APIs in
`Todo.Host`; NSwag generates the C# DTOs in `Todo.Contracts` and the Angular client from the
contract, and the generated code is committed. `scripts\generate-api.ps1` regenerates it.

Two tests hold the arrangement up. `ContractDriftTests` compares the contract against the
running API's own OpenAPI derivation, and `GeneratedCodeFreshnessTests` fails when the
generated code is older than the contract.

## Alternatives rejected

**Code-first, generating OpenAPI from the endpoints.** The document would then describe
whatever was written, which makes it a report rather than a decision. The design step
disappears, and with it the reason for having the file.

**No contract at all, with a hand-written client.** The Angular client and the C# DTOs would
become two hand-maintained descriptions of the same wire, free to drift.

## Consequences

- The generated Angular client is a consequence, not the motivation. It is a real saving,
  but it is not why the contract exists.
- Generated code in the repository means a contract change is a two-step change, and the
  freshness test is what makes the second step non-optional.
- **Drift testing reaches only as far as paths and methods.** Schemas and enum values cannot
  fail it, so wire-format assertions on raw JSON are a separate obligation. See ADR 20.
- The app serves **two** OpenAPI documents on purpose: the runtime derivation that the drift
  test measures against, and the contract itself, embedded in `Todo.Host` and shown by the
  documentation page. See ADR 18.
