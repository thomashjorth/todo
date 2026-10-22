using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Todo.Core.Ai;
using Todo.Core.Errors;
using Todo.Core.Sources;

namespace Todo.Host.Ai;

/// <summary>
/// Talks to the local Ollama. Only the host does: localhost:11434 is another origin than the app's
/// own, and ADR 18 forbids a served page to call out. The shapes and the timings that decide the
/// details were measured against Ollama 0.40.8 on 2026-10-22 - see the AI foundation design.
/// </summary>
public sealed class OllamaClient
{
    /// <summary>
    /// How long readiness may take. Its own limit rather than the client's timeout, which is sized
    /// for a cold model load: an Ollama that hangs must not hold up the question "is it there".
    /// </summary>
    public static readonly TimeSpan ReadinessLimit = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _client;
    private readonly AiOptions _options;

    /// <summary>The one constructor DI sees.</summary>
    public OllamaClient(HttpClient client, IOptions<AiOptions> options)
        : this(client, options.Value)
    {
    }

    private OllamaClient(HttpClient client, AiOptions options)
    {
        _client = client;
        _options = options;
    }

    /// <summary>
    /// For a caller that already holds the options. A second public constructor would be ambiguous
    /// to ActivatorUtilities, for the same reason as JiraTaskSource.With.
    /// </summary>
    public static OllamaClient With(HttpClient client, AiOptions options) => new(client, options);

    /// <summary>
    /// Whether Ollama answers and has the configured model. Never throws: every failure is "not
    /// ready", because the answer only decides whether the AI features are shown at all.
    /// <c>/api/show</c> rather than <c>/api/tags</c>, so Ollama resolves the name itself - the same
    /// question install-ai-model.ps1 asks with <c>ollama show</c>.
    /// </summary>
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

    /// <summary>
    /// Asks the model for an answer in the shape <paramref name="schema"/> describes, and reads it
    /// as <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// <c>think: false</c> is a requirement, not a tuning: Gemma 4 thinks unless told not to, and the
    /// measured call took 22 s with thinking against under 1 s without. <c>temperature: 0</c> because
    /// a suggestion should not change between two clicks on the same text.
    /// </remarks>
    public async Task<T> CompleteAsync<T>(string prompt, JsonObject schema, CancellationToken ct = default)
    {
        var request = new ChatRequest(
            _options.Model,
            [new ChatMessage("user", prompt)],
            Stream: false,
            Think: false,
            Format: schema,
            Options: new ChatOptions(Temperature: 0),
            KeepAlive: _options.KeepAlive);

        HttpResponseMessage response;

        try
        {
            response = await _client.PostAsJsonAsync(UriFor("api/chat"), request, Json, ct);
        }
        catch (HttpRequestException exception)
        {
            // The message names the host and the socket error, never the request: it holds the prompt.
            throw Unavailable(exception.Message);
        }
        catch (TaskCanceledException)
        {
            // HttpClient throws this both for its own timeout and for a cancelled call, and
            // "A task was canceled" tells the user nothing they can act on.
            throw Unavailable("it did not answer in time");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Ollama's own words, such as "model 'x' not found". They never echo the prompt.
                throw Unavailable($"it answered {(int)response.StatusCode}: {body}");
            }

            return Read<T>(body) ?? throw new SourceException(
                ErrorCodes.AiUnreadableAnswer,
                "The model's answer could not be read as the requested shape.");
        }
    }

    /// <summary>
    /// Loads the model, so the first suggestion does not pay for it: an empty prompt to
    /// <c>/api/generate</c> is Ollama's own way of loading a model without asking it anything.
    /// Measured 2026-10-22 on the user's CPU, the load is about 13 s of a cold call's 15. Never throws,
    /// for the same reason as readiness - it only makes the features faster, never possible.
    /// </summary>
    public async Task WarmAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(
                UriFor("api/generate"), new WarmRequest(_options.Model, _options.KeepAlive), Json, ct);
        }
        catch (HttpRequestException)
        {
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// The answer is JSON inside a JSON string: <c>message.content</c> is text, so it is read twice.
    /// A wrong shape at either level is the same thing to the user - an answer that cannot be used.
    /// </summary>
    private static T? Read<T>(string body)
    {
        try
        {
            var content = JsonSerializer.Deserialize<ChatResponse>(body, Json)?.Message?.Content;

            return content is null ? default : JsonSerializer.Deserialize<T>(content, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private Uri UriFor(string path) => new($"{_options.BaseUrl.TrimEnd('/')}/{path}");

    private static SourceException Unavailable(string reason) =>
        new(ErrorCodes.AiUnavailable, $"The local AI model could not be asked: {reason}");

    private sealed record ShowRequest(string Model);

    private sealed record ChatRequest(
        string Model,
        ChatMessage[] Messages,
        bool Stream,
        bool? Think,
        JsonObject Format,
        ChatOptions Options,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    /// <summary>Ollama spells it with an underscore, so the web naming policy would send "keepAlive".</summary>
    private sealed record WarmRequest(
        string Model,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatOptions(double Temperature);

    private sealed record ChatResponse(ChatMessage? Message);
}
