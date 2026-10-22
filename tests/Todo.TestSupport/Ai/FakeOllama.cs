using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todo.Core.Ai;
using Todo.Host.Ai;

namespace Todo.TestSupport.Ai;

/// <summary>
/// An Ollama on loopback, answering the two routes <see cref="OllamaClient"/> asks for. Real HTTP
/// for the same reason as FakeJira: the request body, the status codes and the JSON reading are the
/// whole of what can go wrong between here and the real server.
///
/// The shapes were measured against Ollama 0.40.8 on 2026-10-22 (the AI foundation design's
/// section 1). The one that matters most: the answer arrives as a JSON <em>string</em> inside
/// <c>message.content</c>, not as an object, so this fake sends a string too.
/// </summary>
public sealed class FakeOllama : IAsyncDisposable
{
    /// <summary>The one model this Ollama has. Any other name answers 404, as the real one does.</summary>
    public const string Model = "gemma4:e4b";

    private const string Loopback = "http://127.0.0.1:0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = new();

    private WebApplication? _app;

    private FakeOllama()
    {
    }

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>What <c>message.content</c> carries in the next chat answer: the model's JSON, as text.</summary>
    public string Answer { get; set; } = "{}";

    /// <summary>
    /// Makes <c>/api/show</c> wait until the caller gives up. A real Ollama does this while it is
    /// busy loading a model, and readiness must not wait with it.
    /// </summary>
    public bool HangOnShow { get; set; }

    /// <summary>Every chat request body, so a test can assert what was sent.</summary>
    public List<JsonObject> ChatRequests { get; } = [];

    /// <summary>Every <c>/api/generate</c> body: the warm-up, which loads the model without a prompt.</summary>
    public List<JsonObject> GenerateRequests { get; } = [];

    public static async Task<FakeOllama> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls(Loopback);

        // A fake answering requests has nothing to say on the test runner's console.
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var fake = new FakeOllama();

        fake.MapRoutes(app);

        await app.StartAsync();

        fake._app = app;
        fake.BaseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return fake;
    }

    /// <summary>
    /// A real <see cref="OllamaClient"/> over a real <see cref="HttpClient"/>, pointed at this fake.
    /// Not the DI constructor, because there is no configuration here to read options out of.
    /// </summary>
    public OllamaClient ClientFor(string? model = null) =>
        OllamaClient.With(_client, new AiOptions { BaseUrl = BaseUrl, Model = model ?? Model });

    /// <summary>
    /// Stops answering while keeping <see cref="BaseUrl"/>. Nothing is listening on the port
    /// afterwards, so the connection is refused rather than left to time out.
    /// </summary>
    public async Task StopServerAsync()
    {
        if (_app is not { } app)
        {
            return;
        }

        _app = null;

        await app.StopAsync();
        await app.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopServerAsync();

        _client.Dispose();
    }

    private void MapRoutes(WebApplication app)
    {
        app.MapPost("/api/show", async (HttpRequest request) =>
        {
            var model = (string?)(await ReadAsync(request))["model"];

            if (HangOnShow)
            {
                await Task.Delay(Timeout.Infinite, request.HttpContext.RequestAborted);
            }

            // The real body is several kilobytes of model metadata nobody here reads.
            return model == Model ? Results.Json(new JsonObject(), Json) : NotFound(model);
        });

        // The real answer to an empty prompt is {"model":...,"response":"","done":true,"done_reason":"load"}.
        app.MapPost("/api/generate", async (HttpRequest request) =>
        {
            var body = await ReadAsync(request);

            GenerateRequests.Add(body);

            return Results.Json(
                new JsonObject { ["model"] = (string?)body["model"], ["response"] = "", ["done"] = true, ["done_reason"] = "load" },
                Json);
        });

        app.MapPost("/api/chat", async (HttpRequest request) =>
        {
            var body = await ReadAsync(request);
            var model = (string?)body["model"];

            ChatRequests.Add(body);

            return model != Model
                ? NotFound(model)
                : Results.Json(
                    new JsonObject
                    {
                        ["model"] = model,
                        ["created_at"] = "2026-10-22T14:57:19.7221206Z",
                        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = Answer },
                        ["done"] = true,
                        ["done_reason"] = "stop",
                    },
                    Json);
        });
    }

    private static async Task<JsonObject> ReadAsync(HttpRequest request) =>
        (await JsonNode.ParseAsync(request.Body))?.AsObject() ?? [];

    private static IResult NotFound(string? model) => Results.Json(
        new JsonObject { ["error"] = $"model '{model}' not found" },
        Json,
        statusCode: StatusCodes.Status404NotFound);
}
