# The AI foundation — design

Decided 2026-10-22. Status: approved section by section; the implementation follows.

The wish is the "Local AI model" list in `docs/HANDOFF.md`: six features sharing one set of
premises, with quick entry in free text named as the place to start. This delivery is **the layer
under the first feature and nothing visible**: the user asked for "the foundation for the first AI
feature" and chose, when offered, the AI layer alone over the layer plus a parse endpoint or the
whole first feature. Quick entry is the next delivery, and it writes its prompt, its schema and its
screen on top of what this one measures.

## 1. Measured, not guessed

Against the user's own Ollama 0.40.8 on 2026-10-22, model `gemma4:e4b` installed:

| Call | Answer |
| --- | --- |
| `POST /api/show {"model":"gemma4:e4b"}` | **200** in 17 ms |
| `POST /api/show {"model":"findes-ikke:x"}` | **404**, body `{"error":"model 'findes-ikke:x' not found"}` |
| `POST /api/chat` to a model that does not exist | **404**, same body shape |
| `POST /api/chat`, `format` a JSON schema, `stream:false` | **200**, the JSON as a **string** in `message.content` |
| The same, cold, without `think` | **22.3 s**: 14.5 s loading the model, then 156 tokens of thinking |
| The same, warm, `"think": false` | **0.7–0.9 s**, 12 tokens |

Two consequences carry the design. **Gemma 4 thinks unless told not to**, and the reasoning comes
back in a `message.thinking` field beside the answer, so `think:false` is a requirement and not a
tuning. And **the first call after the model was unloaded costs about 15 s**, which sets the timeout
below and is a fact the quick-entry delivery inherits; warming the model up is not part of this one.

## 2. Architecture

Only the host talks to Ollama. `localhost:11434` is another origin than the app's own, and ADR 18
forbids a served page to call out.

- **`AiOptions`** in `Todo.Core/Ai`: `BaseUrl` (default `http://localhost:11434`) and `Model`
  (default `gemma4:e4b`), bound from configuration under `Ai:` — the same mechanism as
  `--Data:Path`. **Configuration, not the settings page**: the user's choice 2026-10-22. The price
  is that a switch to 26B A4B means editing a file or an argument rather than clicking, in return for
  no contract fields, no `Setting` rows, no seventh accordion group and no contrast snapshots for
  something that has one value today.
- **`OllamaClient`** in `Todo.Host/Ai`, a typed client like `JiraTaskSource`, with two methods:
  - `IsReadyAsync()` asks `/api/show` and answers `true` on 200 only. A refused connection, a 404,
    a 5xx and a timeout all answer `false`; it never throws.
  - `CompleteAsync<T>(prompt, schema)` posts to `/api/chat` with `stream:false`, `think:false` and
    `options.temperature:0`, and reads `T` out of `message.content`. The user chose to include it
    2026-10-22 so the first feature only has to write its prompt, its schema and its screen. Nothing
    calls it yet, so it is tested directly against the fake, the way `JiraTaskSourceTests` test the
    source.
- **`GET /api/ai/status`** answers `{ "ready": bool }`. It goes in `contracts/openapi.yaml`, so the
  freshness and drift tests cover it like every other route.
- **`AiStore`** in Angular asks **once, at startup**, and exposes `ready` as a signal. The user's
  choice 2026-10-22, over asking on every window focus or polling. The price, written down so it is
  not rediscovered: a model whose download finishes after the app started needs a restart before the
  features appear, and an Ollama that dies mid-session keeps looking ready until a call fails.
- **`FakeOllama`** in `Todo.TestSupport`, a real Kestrel on loopback (ADR 21). It answers
  `/api/show` and `/api/chat` in Ollama's measured shape — the answer as a JSON *string* inside
  `content`, not as an object, because that is what the real one sends — and records the requests it
  gets, so a test can assert that `think:false` and the schema were sent.

**Timeouts.** 60 s on the client, because the cold call measured 22 s with thinking on and a slower
disk must not turn the first quick entry into an error. The status call gets its own 3 s limit
through a linked cancellation, so startup never waits on an Ollama that hangs.

## 3. Errors

- **Status never throws.** Every failure is `ready: false`. `AiStore` also reads a failed
  `/api/ai/status` call as not ready and shows no error line: the HANDOFF premise is that when Ollama
  does not answer, the features are hidden and the app works as before.
- **`CompleteAsync` throws `SourceException` with one of two codes.** `ai.unavailable` for
  transport, timeout and the 404 for a missing model — the user's move is to start Ollama.
  `ai.unreadableAnswer` when `content` cannot be read as `T` — the user's move is to try again. Two
  codes because the two moves differ. `SourceException` rather than a new type, because the
  endpoints already turn it into a 400 the frontend translates, for Jira and Azure DevOps alike.
- Both codes get keys in `da.json` and `en.json` without `{{value}}`, and
  `ErrorCodeTranslationTests` enforces that by itself.
- **The prompt is not logged.** It will carry the user's own task text. Whether
  `SensitiveLoggingTests` needs to reach the new client is a question for the plan.

## 4. The trap closed first: no test may reach the real Ollama

The default address answers on the user's machine. The moment `AiStore` asks at startup, every E2E
test would call the real Ollama, and the answer would depend on the machine: ready here, not ready
in CI. So:

- `RunningHost` always passes `--Ai:BaseUrl` pointing at a closed loopback port, unless the test
  passes its own.
- A guard asserts that a host started without arguments does not point at port 11434, and is seen
  to fail.
- The plan measures how long a refused connection takes on Windows. Windows can retry a refused
  connect for about two seconds, and that must not add to every test's startup.

## 5. Tests

- **Api, `OllamaClientTests` against `FakeOllama`:** ready when the model exists; not ready on 404,
  when nothing listens, and when the fake is slower than 3 s. `CompleteAsync` sends `model`,
  `stream:false`, `think:false`, `temperature:0` and the schema, and reads `T` out of `content`. The
  two error codes.
- **Api, elsewhere:** `GET /api/ai/status` both ways; a registration test like
  `JiraTaskSourceRegistrationTests`; `"ready":true` in a wire-format assertion; the guard against
  port 11434.
- **Vitest:** `AiStore` — ready, not ready, and an error read as not ready; it asks exactly once at
  startup.
- **E2E:** no new journey, because there is nothing visible to walk through. The existing suite has
  to stay green against the closed port.

## Out of scope

Every one of the six features, quick entry included. Warming the model at startup. An embedding
model. A settings group for the address or the model.
