using System.Diagnostics;
using System.Text.Json.Nodes;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.Host.Ai;
using Todo.TestSupport.Ai;

namespace Todo.Api.Tests;

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

    [Fact]
    public async Task The_answer_is_read_out_of_the_message_content()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.Answer = "{\n  \"svar\": 5\n}";

        var answer = await fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema());

        Assert.Equal(5, answer.Svar);
    }

    /// <summary>
    /// The thinking flag is this class's tooth. Without it the measured call took 22 s instead of
    /// under one, and nothing else would notice it missing until a user sat waiting.
    /// </summary>
    [Fact]
    public async Task The_request_turns_thinking_off_and_sends_the_schema()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.Answer = "{\"svar\": 5}";

        await fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema());

        var request = Assert.Single(fake.ChatRequests);
        Assert.Equal(FakeOllama.Model, (string?)request["model"]);
        Assert.False((bool?)request["stream"]);
        Assert.False((bool?)request["think"]);
        Assert.Equal(0, (double?)request["options"]?["temperature"]);
        Assert.True(JsonNode.DeepEquals(SumSchema(), request["format"]));
        var message = Assert.Single(request["messages"]!.AsArray());
        Assert.Equal("user", (string?)message!["role"]);
        Assert.Equal("Hvad er 2+3?", (string?)message["content"]);
    }

    /// <summary>
    /// With an underscore, as Ollama spells it: the web naming policy would send "keepAlive", which
    /// Ollama ignores, and the model would unload after its own five minutes - silently, because
    /// every answer still arrives, only 13 s later.
    /// </summary>
    [Fact]
    public async Task A_request_asks_ollama_to_keep_the_model_loaded()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.Answer = "{\"svar\": 5}";

        await fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema());

        Assert.Equal("8h", (string?)Assert.Single(fake.ChatRequests)["keep_alive"]);
    }

    [Fact]
    public async Task Warming_loads_the_model_without_a_prompt()
    {
        await using var fake = await FakeOllama.StartAsync();

        await fake.ClientFor().WarmAsync();

        var request = Assert.Single(fake.GenerateRequests);
        Assert.Equal(FakeOllama.Model, (string?)request["model"]);
        Assert.Equal("8h", (string?)request["keep_alive"]);
        Assert.Null(request["prompt"]);
        Assert.Empty(fake.ChatRequests);
    }

    [Fact]
    public async Task Warming_with_nothing_listening_is_not_an_error()
    {
        await using var fake = await FakeOllama.StartAsync();
        var client = fake.ClientFor();
        await fake.StopServerAsync();

        await client.WarmAsync();
    }

    [Fact]
    public async Task Search_is_ready_only_when_the_embedding_model_is_there_too()
    {
        await using var fake = await FakeOllama.StartAsync();
        var client = fake.ClientFor();

        Assert.True(await client.IsSearchReadyAsync());

        fake.WithoutEmbeddingModel = true;

        Assert.False(await client.IsSearchReadyAsync());
        Assert.True(await client.IsReadyAsync());
    }

    [Fact]
    public async Task Texts_come_back_as_one_vector_each_in_order()
    {
        await using var fake = await FakeOllama.StartAsync();

        var vectors = await fake.ClientFor().EmbedAsync(["Køb mælk", "Forny certifikatet"]);

        Assert.Equal(2, vectors.Length);
        Assert.Equal(FakeOllama.VectorOf("Køb mælk"), vectors[0]);
        Assert.Equal(FakeOllama.VectorOf("Forny certifikatet"), vectors[1]);
        Assert.Equal(["Køb mælk", "Forny certifikatet"], fake.EmbeddedTexts);
    }

    [Fact]
    public async Task A_missing_embedding_model_is_unavailable()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.WithoutEmbeddingModel = true;

        var exception = await Assert.ThrowsAsync<SourceException>(() => fake.ClientFor().EmbedAsync(["Køb mælk"]));

        Assert.Equal(ErrorCodes.AiUnavailable, exception.Code);
    }

    [Fact]
    public async Task A_missing_model_is_unavailable()
    {
        await using var fake = await FakeOllama.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => fake.ClientFor("gemma4:e2b").CompleteAsync<Sum>("Hvad er 2+3?", SumSchema()));

        Assert.Equal(ErrorCodes.AiUnavailable, exception.Code);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task Nothing_listening_is_unavailable()
    {
        await using var fake = await FakeOllama.StartAsync();
        var client = fake.ClientFor();
        await fake.StopServerAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => client.CompleteAsync<Sum>("Hvad er 2+3?", SumSchema()));

        Assert.Equal(ErrorCodes.AiUnavailable, exception.Code);
    }

    [Fact]
    public async Task An_answer_in_the_wrong_shape_is_unreadable()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.Answer = "fem";

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => fake.ClientFor().CompleteAsync<Sum>("Hvad er 2+3?", SumSchema()));

        Assert.Equal(ErrorCodes.AiUnreadableAnswer, exception.Code);
    }

    /// <summary>The prompt carries the user's own task text, and an error message ends up in a 400 body.</summary>
    [Fact]
    public async Task The_error_never_carries_the_prompt()
    {
        await using var fake = await FakeOllama.StartAsync();
        fake.Answer = "fem";

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => fake.ClientFor().CompleteAsync<Sum>("Ring til Anna om fakturaen", SumSchema()));

        Assert.DoesNotContain("Anna", exception.Message);
    }

    private sealed record Sum(int Svar);

    private static JsonObject SumSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["svar"] = new JsonObject { ["type"] = "integer" } },
        ["required"] = new JsonArray("svar"),
    };
}
