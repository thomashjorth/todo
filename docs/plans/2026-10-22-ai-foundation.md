# The AI foundation — implementation plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** The host can tell whether the local model is ready and can ask it for an answer in a given
JSON shape, and the frontend knows at startup whether AI features may be shown. Nothing visible.

**Architecture:** `AiOptions` (configuration `Ai:`) → `OllamaClient`, a typed client in the host
with `IsReadyAsync` and `CompleteAsync<T>` → `GET /api/ai/status` → `AiStore.ready` in Angular,
asked once from `App`'s constructor. `FakeOllama` is a real Kestrel on loopback, and `RunningHost`
points every test host at a closed port so no test can reach the user's real Ollama. The design is
`2026-10-22-ai-foundation-design.md`.

**Tech stack:** ASP.NET Core minimal APIs, `IHttpClientFactory` typed client, `System.Text.Json`,
NSwag generation via `scripts\generate-api.ps1`, Angular signals, Vitest, xUnit v3.

**Baseline before Task 1:** `Check.cmd` green at 186 Core, 399 Api, 83 E2E, 366 Vitest.

**Rules that bite here** (from `CLAUDE.md`): edit with `Edit`, never `sed -i`; a file made with
`Write` is converted to CRLF and checked with `tr -dc '\r' | wc -c` against `tr -dc '\n' | wc -c`;
`npm.cmd`, not `npm`; `--prefix src/Todo.Web` with forward slashes from Bash; every guard is seen to
fail once, one condition at a time, with `git diff --stat` between mutations; commits are gitmoji,
one line, no attribution.

---

### Task 1: Configuration, and no test host can reach the real Ollama

**Files:**
- Create: `src/Todo.Core/Ai/AiOptions.cs`
- Modify: `src/Todo.Host/TodoHost.cs` (register the options next to the other typed clients)
- Modify: `tests/Todo.TestSupport/RunningHost.cs` (default `--Ai:BaseUrl`)
- Create: `tests/Todo.Api.Tests/AiIsolationTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Todo.Core.Ai;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The shipped default is the user's own Ollama, and it answers on the machine the suite runs on.
/// Without RunningHost's closed port, every E2E test would ask the real model at startup and the
/// result would depend on the machine.
/// </summary>
public partial class AiIsolationTests : ApiTest
{
    [Fact]
    public void A_test_host_never_points_at_the_real_ollama()
    {
        var options = Host.Services.GetRequiredService<IOptions<AiOptions>>().Value;
        var address = new Uri(options.BaseUrl);

        Assert.Equal("127.0.0.1", address.Host);
        Assert.NotEqual(new Uri(new AiOptions().BaseUrl).Port, address.Port);
    }

    /// <summary>
    /// Todo.cmd installs one model and the host asks for one model, and the two are written in two
    /// languages in two files. A drift would leave the app asking for a model nobody installed.
    /// </summary>
    [Fact]
    public void The_host_asks_for_the_model_the_install_script_installs()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "install-ai-model.ps1"));
        var match = ScriptModel().Match(script);

        Assert.True(match.Success, "install-ai-model.ps1 no longer declares [string]$Model = '...'.");
        Assert.Equal(match.Groups[1].Value, new AiOptions().Model);
    }

    [GeneratedRegex(@"\[string\]\$Model\s*=\s*'([^']+)'")]
    private static partial Regex ScriptModel();
}
```

**Step 2:** `dotnet test tests\Todo.Api.Tests --filter FullyQualifiedName~AiIsolationTests` →
compile error, `AiOptions` does not exist.

**Step 3: Implement**

`src/Todo.Core/Ai/AiOptions.cs`:

```csharp
namespace Todo.Core.Ai;

/// <summary>
/// Where the local model lives. Configuration rather than a setting on the settings page: there is
/// one value today, and a switch of model is an edit of a file or an argument (the design's
/// section 2, the user's choice 2026-10-22).
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Must match the default in scripts\install-ai-model.ps1; AiIsolationTests holds the two together.</summary>
    public string Model { get; set; } = "gemma4:e4b";
}
```

In `TodoHost.Build`, after the `AdoTaskSource` client:

```csharp
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
```

In `RunningHost.StartAsync`, put the default **before** `.. extraArgs`, so a test's own
`--Ai:BaseUrl` wins (the command-line provider keeps the last value for a repeated key):

```csharp
"--Ai:BaseUrl", ClosedLoopbackUrl(),
```

```csharp
/// <summary>
/// An address nothing listens on. Asked for rather than hard-coded: a fixed port can be taken on
/// somebody's machine. Measured 2026-10-22: a refused connect on loopback takes under 1 ms on
/// Windows, so pointing every host here costs the suite nothing.
/// </summary>
private static string ClosedLoopbackUrl()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();

    return $"http://127.0.0.1:{port}";
}
```

**Step 4:** run the filter again → 2 green.

**Step 5: See the guard fail.** Remove the `"--Ai:BaseUrl", ClosedLoopbackUrl(),` line → the first
test fails on `Assert.Equal("127.0.0.1", ...)` (`localhost`). Put it back. Then change the script's
default to `'gemma4:e2b'` → the second test fails naming both values. Put it back. `git diff --stat`
shows only the intended files.

**Step 6: Commit** — `🔧 The host reads where the AI model lives, and no test host can reach the real one`

---

### Task 2: FakeOllama and `IsReadyAsync`

**Files:**
- Create: `tests/Todo.TestSupport/Ai/FakeOllama.cs`
- Create: `src/Todo.Host/Ai/OllamaClient.cs`
- Create: `tests/Todo.Api.Tests/OllamaClientTests.cs`

**Step 1: FakeOllama.** Mirror `FakeJira`'s lifecycle (`StartAsync`, `BaseUrl`, `StopServerAsync`,
`DisposeAsync`, `CreateSlimBuilder`, `ClearProviders`, its own `HttpClient`). It answers in the
shape measured against Ollama 0.40.8 (design section 1):

- `POST /api/show` with `{"model": m}`: 200 and `{}` when `m == Model`, otherwise 404 and
  `{"error":"model '<m>' not found"}`. When `HangOnShow` is set, wait on `RequestAborted`.
- `POST /api/chat`: record the body as a `JsonObject` in `ChatRequests`; 404 with the same error
  body for an unknown model; otherwise 200 and
  `{"model":m,"created_at":"2026-10-22T14:57:19.7221206Z","message":{"role":"assistant","content":Answer},"done":true,"done_reason":"stop"}`.
  `content` is a **string** holding JSON, because that is what the real one sends.
- `public const string Model = "gemma4:e4b";`, `public string Answer { get; set; } = "{}";`,
  `public bool HangOnShow { get; set; }`.
- `public OllamaClient ClientFor(string? model = null)` →
  `OllamaClient.With(_client, new AiOptions { BaseUrl = BaseUrl, Model = model ?? Model })`.

**Step 2: The failing tests**

```csharp
public class OllamaClientTests
{
    [Fact]
    public async Task The_model_is_ready_when_ollama_has_it()
    {
        await using var fake = await FakeOllama.StartAsync();

        Assert.True(await fake.ClientFor().IsReadyAsync());
    }

    [Fact]
    public async Task A_model_ollama_does_not_have_is_not_ready()
    {
        await using var fake = await FakeOllama.StartAsync();

        Assert.False(await fake.ClientFor("gemma4:e2b").IsReadyAsync());
    }

    [Fact]
    public async Task Nothing_listening_is_not_ready_rather_than_an_error()
    {
        await using var fake = await FakeOllama.StartAsync();
        var client = fake.ClientFor();
        await fake.StopServerAsync();

        Assert.False(await client.IsReadyAsync());
    }

    [Fact]
    public async Task An_ollama_that_hangs_is_not_ready_within_the_readiness_limit()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.HangOnShow = true;
        var clock = Stopwatch.StartNew();

        Assert.False(await fake.ClientFor().IsReadyAsync());
        Assert.True(clock.Elapsed < OllamaClient.ReadinessLimit * 2, $"Took {clock.Elapsed}.");
    }
}
```

**Step 3:** run → compile error.

**Step 4: Implement** `OllamaClient` with the DI constructor `(HttpClient, IOptions<AiOptions>)`, a
private one taking `AiOptions`, and a static `With` — the same shape as `JiraTaskSource` and for
the same reason (ActivatorUtilities and two public constructors).

```csharp
public static readonly TimeSpan ReadinessLimit = TimeSpan.FromSeconds(3);

public async Task<bool> IsReadyAsync(CancellationToken ct = default)
{
    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
    limit.CancelAfter(ReadinessLimit);

    try
    {
        using var response = await _client.PostAsJsonAsync(
            UriFor("api/show"), new ShowRequest(_options.Model), Json, limit.Token);

        return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
        return false;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        return false;
    }
}

private Uri UriFor(string path) => new($"{_options.BaseUrl.TrimEnd('/')}/{path}");
```

`/api/show` rather than `/api/tags`: Ollama resolves the name itself, the same way
`install-ai-model.ps1` asks with `ollama show`, and it measured 17 ms.

**Step 5:** run → 4 green. **See it fail:** make the `HttpRequestException` catch rethrow → the
"nothing listening" test fails. Restore.

**Step 6: Commit** — `🧪 A fake Ollama answers in the measured shape, and readiness never throws`

---

### Task 3: `CompleteAsync<T>` and its two error codes

**Files:**
- Modify: `src/Todo.Host/Ai/OllamaClient.cs`
- Modify: `src/Todo.Core/Errors/ErrorCodes.cs`
- Modify: `src/Todo.Web/public/i18n/da.json`, `src/Todo.Web/public/i18n/en.json` (an `ai` group
  last under `errors`, after `system`)
- Modify: `tests/Todo.Api.Tests/OllamaClientTests.cs`

**Step 1: The failing tests**

```csharp
private sealed record Sum(int Svar);

private static readonly JsonObject SumSchema = new()
{
    ["type"] = "object",
    ["properties"] = new JsonObject { ["svar"] = new JsonObject { ["type"] = "integer" } },
    ["required"] = new JsonArray("svar"),
};

[Fact]
public async Task The_answer_is_read_out_of_the_message_content()
{
    await using var fake = await FakeOllama.StartAsync();
    fake.Answer = "{\n  \"svar\": 5\n}";

    var answer = await fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema);

    Assert.Equal(5, answer.Svar);
}

[Fact]
public async Task The_request_turns_thinking_off_and_sends_the_schema()
{
    await using var fake = await FakeOllama.StartAsync();
    fake.Answer = "{\"svar\": 5}";

    await fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema);

    var request = Assert.Single(fake.ChatRequests);
    Assert.Equal(FakeOllama.Model, (string?)request["model"]);
    Assert.False((bool?)request["stream"]);
    Assert.False((bool?)request["think"]);
    Assert.Equal(0, (double?)request["options"]?["temperature"]);
    Assert.True(JsonNode.DeepEquals(SumSchema, request["format"]));
    var message = Assert.Single(request["messages"]!.AsArray());
    Assert.Equal("user", (string?)message!["role"]);
    Assert.Equal("Hvad er 2+3?", (string?)message["content"]);
}

[Fact]
public async Task A_missing_model_is_unavailable()  // fake.ClientFor("gemma4:e2b") → SourceException, Code ai.unavailable

[Fact]
public async Task Nothing_listening_is_unavailable()  // StopServerAsync first → ai.unavailable

[Fact]
public async Task An_answer_in_the_wrong_shape_is_unreadable()  // Answer = "not json" → ai.unreadableAnswer

[Fact]
public async Task The_error_never_carries_the_prompt()
// Answer = "not json", prompt "Ring til Anna om fakturaen" → Assert.DoesNotContain("Anna", exception.Message)
```

**The thinking assertion is this delivery's tooth.** Without `think:false` the measured call took
22 s instead of under 1 s; nothing else would notice it missing until a user waited.

**Step 2:** run → compile error.

**Step 3: Implement.** Codes, each with a `<summary>` saying the user's move:

```csharp
/// <summary>Ollama did not answer, or does not have the model. The user's move is to start Ollama.</summary>
public const string AiUnavailable = "ai.unavailable";

/// <summary>The model answered, but not in the requested shape. The user's move is to try again.</summary>
public const string AiUnreadableAnswer = "ai.unreadableAnswer";
```

Translations, without `{{value}}`:

- da `unavailable`: `"Den lokale AI-model svarer ikke. Kontrollér at Ollama kører."`
- da `unreadableAnswer`: `"AI-modellen gav et svar der ikke kunne læses. Prøv igen."`
- en `unavailable`: `"The local AI model is not answering. Check that Ollama is running."`
- en `unreadableAnswer`: `"The AI model gave an answer that could not be read. Try again."`

The method, following `JiraTaskSource.SendAsync`'s exception handling:

```csharp
public async Task<T> CompleteAsync<T>(string prompt, JsonObject schema, CancellationToken ct = default)
{
    var request = new ChatRequest(
        _options.Model,
        [new ChatMessage("user", prompt)],
        Stream: false,
        Think: false,
        Format: schema,
        Options: new ChatOptions(Temperature: 0));

    HttpResponseMessage response;

    try
    {
        response = await _client.PostAsJsonAsync(UriFor("api/chat"), request, Json, ct);
    }
    catch (HttpRequestException exception)
    {
        throw Unavailable(exception.Message);
    }
    catch (TaskCanceledException)
    {
        throw Unavailable("it did not answer in time");
    }

    using (response)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Ollama's own words, e.g. "model 'x' not found". Never the request: it holds the prompt.
            throw Unavailable($"it answered {(int)response.StatusCode}: {body}");
        }

        return Read<T>(body) ?? throw new SourceException(
            ErrorCodes.AiUnreadableAnswer, "The model's answer could not be read as the requested shape.");
    }
}
```

`Read<T>` deserializes `ChatResponse`, then `message.content` as `T`, and returns `default` on a
`JsonException`. The private records (`ShowRequest`, `ChatRequest`, `ChatMessage`, `ChatOptions`,
`ChatResponse`) are nested in the client. `Json` is `JsonSerializerDefaults.Web`, which writes
`think`, `stream` and `temperature` in the casing Ollama reads.

**Step 4:** run → 10 green. Then `--filter FullyQualifiedName~ErrorCodeTranslation` → green with
the two new keys. **See it fail:** drop `Think: false` (the property defaults to `null`, so it is
omitted) → only `The_request_turns_thinking_off_and_sends_the_schema` fails. Restore.

**Step 5: Commit** — `✨ The host can ask the local model for an answer in a given shape`

---

### Task 4: `GET /api/ai/status`

**Files:**
- Modify: `contracts/openapi.yaml`
- Run: `powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1` (regenerates
  `src/Todo.Contracts/Generated/Contracts.g.cs` and `src/Todo.Web/src/app/api/todo-client.ts`)
- Create: `src/Todo.Host/Endpoints/AiEndpoints.cs`
- Modify: `src/Todo.Host/TodoHost.cs` (typed client and `app.MapAi()`)
- Create: `tests/Todo.Api.Tests/AiEndpointsTests.cs`, `tests/Todo.Api.Tests/OllamaClientRegistrationTests.cs`

**Step 1: The contract**, after `/api/system/open-link`:

```yaml
  /api/ai/status:
    get:
      operationId: getAiStatus
      tags:
        - Ai
      summary: Reports whether the local AI model can be asked right now.
      description: >-
        Never an error. An Ollama that is not running, does not have the configured model or does
        not answer within three seconds is reported as not ready, and the app hides its AI features.
      responses:
        '200':
          description: Whether the model is ready.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AiStatusResponse'
```

```yaml
    AiStatusResponse:
      type: object
      additionalProperties: false
      required:
        - ready
      properties:
        ready:
          type: boolean
```

Run `generate-api.ps1`. Check `ContractDocumentTests`' comments that name an operation and schema
count (21 and 30) and move them to 22 and 31 if the test asserts them; if only the comments name
them, correct the comments.

**Step 2: The failing tests**

```csharp
public class AiEndpointsTests
{
    [Fact]
    public async Task The_status_is_ready_when_ollama_has_the_model()
    {
        await using var fake = await FakeOllama.StartAsync();
        await using var host = await RunningHost.StartAsync("--Ai:BaseUrl", fake.BaseUrl);

        var body = await host.Client.GetStringAsync("/api/ai/status");

        Assert.Equal("""{"ready":true}""", body);
    }

    [Fact]
    public async Task The_status_is_not_ready_when_nothing_answers()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync("/api/ai/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"ready":false}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_status_names_the_configured_model()
    {
        await using var fake = await FakeOllama.StartAsync();
        await using var host = await RunningHost.StartAsync(
            "--Ai:BaseUrl", fake.BaseUrl, "--Ai:Model", "gemma4:e2b");

        Assert.Equal("""{"ready":false}""", await host.Client.GetStringAsync("/api/ai/status"));
    }
}
```

The raw string is the wire-format assertion. The second test is also the end-to-end proof of
Task 1's closed port.

`OllamaClientRegistrationTests` follows `JiraTaskSourceRegistrationTests`: resolve `OllamaClient`
from a scope of the shipped host and assert it is not null.

**Step 3: Implement**

```csharp
builder.Services.AddHttpClient<OllamaClient>(c => c.Timeout = TimeSpan.FromSeconds(60));
```

with a comment saying why 60: the cold first call measured 22 s with thinking on (design
section 1), and the readiness check has its own three-second limit, so startup is not what this
number protects.

```csharp
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ai/status", async (OllamaClient ollama, CancellationToken ct) =>
            TypedResults.Ok(new AiStatusResponse { Ready = await ollama.IsReadyAsync(ct) }))
            .WithName("getAiStatus")
            .WithTags("Ai");

        return app;
    }
}
```

`app.MapAi();` next to `app.MapSystem();`.

**Step 4:** `dotnet test tests\Todo.Api.Tests --filter "FullyQualifiedName~Ai|FullyQualifiedName~Ollama|FullyQualifiedName~ContractDrift|FullyQualifiedName~GeneratedCodeFreshness|FullyQualifiedName~ContractDocument"` → green.

**Step 5: Commit** — `✨ The app can ask whether the local model is ready`

---

### Task 5: `AiStore`, asked once at startup

**Files:**
- Create: `src/Todo.Web/src/app/ai/ai-store.ts`, `src/Todo.Web/src/app/ai/ai-store.spec.ts`
- Modify: `src/Todo.Web/src/app/app.ts`, `src/Todo.Web/src/app/app.spec.ts`

**Step 1: The failing specs**

```ts
describe('AiStore', () => {
  // configure() as in settings-store.spec.ts: provideHttpClient, provideHttpClientTesting, API_BASE_URL ''.

  it('should start not ready, so nothing flashes on before the answer', () => {
    expect(configure().store.ready()).toBe(false);
  });

  it('should be ready when the host says so', async () => {
    const { store, http } = configure();
    const loading = store.load();
    http.expectOne('/api/ai/status').flush(new Blob([JSON.stringify({ ready: true })]));
    await loading;
    expect(store.ready()).toBe(true);
  });

  it('should stay not ready when the host says not ready', ...);

  it('should read a failed status call as not ready rather than as an error', async () => {
    // flush 500 → load() resolves, ready() false
  });
});
```

In `app.spec.ts`:

```ts
it('should ask once at startup whether the AI model is ready', () => {
  TestBed.createComponent(App);
  TestBed.inject(HttpTestingController).expectOne('/api/ai/status');
});
```

**Step 2:** `npm.cmd run test --prefix src/Todo.Web -- --watch=false --include src/app/ai/ai-store.spec.ts`
→ fails to build, `AiStore` does not exist.

**Step 3: Implement**

```ts
/**
 * Whether the local model can be asked. Read once, at startup (the user's choice 2026-10-22): a model
 * that finishes downloading afterwards needs a restart before the AI features appear, and an Ollama
 * that dies mid-session keeps looking ready until a call fails. See the AI foundation design.
 */
@Injectable({ providedIn: 'root' })
export class AiStore {
  private readonly client = inject(AiClient);

  /** Starts false, so a feature never flashes on and off again while the first answer is on its way. */
  readonly ready = signal(false);

  async load(): Promise<void> {
    try {
      this.ready.set((await firstValueFrom(this.client.getAiStatus())).ready);
    } catch {
      // The AI features are an addition, not the app. A status that cannot be read hides them and
      // says nothing.
    }
  }
}
```

In `App`'s constructor, beside `this.review.load()`: `this.ai.load().catch(() => {});`.

**Step 4:** run the two spec files → green. **See it fail:** remove the `try`/`catch` so the
error escapes `load()` → the failure spec fails. Restore.

**Step 5: Commit** — `✨ The frontend knows at startup whether the local model is ready`

---

### Task 6: The whole suite, and the numbers

**Step 1:** `Check.cmd`. Expected, counted from the tasks above: Core **186** (unchanged), Api
**399 + 2 + 10 + 3 + 1 = 415**, E2E **83** (unchanged, now against the closed port), Vitest
**366 + 4 + 1 = 371**. If a number differs, find out why before going on — a different count means a
lost or duplicated test, or this plan miscounted, and either way it gets written down.

**Step 2:** Update "Testtal" in `CLAUDE.md` with the measured numbers and why they moved. Add one
line to the AI section of `docs/HANDOFF.md`: the foundation is delivered, quick entry is next, and
the cold-start cost (about 15 s) is that delivery's to decide.

**Step 3: Commit** — `📝 The AI foundation is delivered and counted`
