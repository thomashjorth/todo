using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Ai;
using Todo.Core.Errors;
using Todo.Core.Time;
using Todo.TestSupport.Ai;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

namespace Todo.Api.Tests;

/// <summary>
/// The AI routes against a fake Ollama, the whole chain real: the endpoint reads the task, builds the
/// prompt, the client posts it, and the answer is cleaned on the way back. What the prompts say is
/// measured in AiPromptsTests, and whether the real model answers them well is the eval runner's job
/// (tools\Todo.AiEval) - neither belongs in a suite that must never reach the real model.
/// </summary>
public class AiEndpointsTests : ApiTest
{
    /// <summary>A Thursday, the day the prompts were measured on.</summary>
    private static readonly DateOnly Today = new(2026, 10, 22);

    private FakeOllama _ollama = null!;

    public override async ValueTask InitializeAsync()
    {
        _ollama = await FakeOllama.StartAsync();
        await base.InitializeAsync();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock>(new FixedClock(Today));

        // After the host bound Ai:BaseUrl to RunningHost's closed port, so this is the last word.
        services.Configure<AiOptions>(o => o.BaseUrl = _ollama.BaseUrl);
    }

    [Fact]
    public async Task The_status_is_ready_when_ollama_has_the_model()
    {
        Assert.Equal("""{"ready":true}""", await Client.GetStringAsync("/api/ai/status"));
    }

    [Fact]
    public async Task The_status_is_not_ready_when_nothing_answers()
    {
        await _ollama.StopServerAsync();

        var response = await Client.GetAsync("/api/ai/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"ready":false}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_typed_line_becomes_a_suggested_task()
    {
        _ollama.Answer = """{"title":"Ring til Anna om fakturaen","requester":"Anna","deadline":"2026-10-23"}""";

        var suggestion = await ParseAsync("Ring til Anna om fakturaen på fredag");

        Assert.Equal("Ring til Anna om fakturaen", suggestion.Title);
        Assert.Equal("Anna", suggestion.Requester);
        Assert.Equal(new DateOnly(2026, 10, 23), suggestion.Deadline);
    }

    /// <summary>The model cannot know the day, so the host's clock has to reach the prompt.</summary>
    [Fact]
    public async Task The_prompt_carries_the_line_and_the_days_from_the_clock()
    {
        _ollama.Answer = """{"title":"Køb mælk","requester":null,"deadline":null}""";

        await ParseAsync("Køb mælk");

        var prompt = PromptSent();
        Assert.Contains("Today is torsdag 2026-10-22.", prompt);
        Assert.Contains("fredag 2026-10-23", prompt);
        Assert.EndsWith("Køb mælk", prompt);
    }

    [Fact]
    public async Task A_deadline_that_is_not_a_date_is_left_out_rather_than_refused()
    {
        _ollama.Answer = """{"title":"Køb mælk","requester":null,"deadline":"på fredag"}""";

        var suggestion = await ParseAsync("Køb mælk på fredag");

        Assert.Equal("Køb mælk", suggestion.Title);
        Assert.Null(suggestion.Deadline);
    }

    [Fact]
    public async Task A_blank_line_is_refused_without_asking_the_model()
    {
        var response = await Client.PostAsJsonAsync("/api/ai/parse-task", new AiParseTaskRequest { Text = "  " });

        await AssertRefusedAsync(response, ErrorCodes.AiTextRequired);
        Assert.Empty(_ollama.ChatRequests);
    }

    [Fact]
    public async Task A_model_that_cannot_be_asked_is_a_translatable_refusal()
    {
        await _ollama.StopServerAsync();

        var response = await Client.PostAsJsonAsync("/api/ai/parse-task", new AiParseTaskRequest { Text = "Køb mælk" });

        await AssertRefusedAsync(response, ErrorCodes.AiUnavailable);
    }

    [Fact]
    public async Task Splitting_suggests_only_subtasks_the_task_does_not_have()
    {
        var task = new TaskItemBuilder()
            .Titled("Opdatér certifikatet")
            .WithNote("Det ligger i Key Vault.")
            .WithSubTask("Find ejeren")
            .Build();
        await Host.AddAndSaveChangesAsync(task);
        _ollama.Answer = """{"subTasks":["find ejeren","Bestil certifikat"," ","Genstart gatewayen"]}""";

        var response = await Client.PostAsync($"/api/ai/tasks/{task.Id}/split", null);
        var suggestions = await response.Content.ReadFromJsonAsync<AiSubTaskSuggestions>();

        Assert.Equal(["Bestil certifikat", "Genstart gatewayen"], suggestions!.Titles);
        var prompt = PromptSent();
        Assert.Contains("Task: Opdatér certifikatet", prompt);
        Assert.Contains("Note: Det ligger i Key Vault.", prompt);
        Assert.Contains("Existing subtasks: Find ejeren", prompt);
    }

    /// <summary>
    /// The source decides, not the text: each task here is written in the OTHER language, so a rule
    /// that read the text instead would fail all three. Retro and Azure DevOps are English and Jira is
    /// Danish, as the user told it 2026-10-22; only a task made in the app is guessed from its words.
    /// </summary>
    [Theory]
    [InlineData("jira", "Login page times out for every user", "Write in Danish")]
    [InlineData("retro", "Vi skal blive bedre til at teste", "Write in English.")]
    [InlineData("manual", "Prepare the quarterly security review", "Write in English.")]
    public async Task The_answer_language_follows_the_source(string source, string title, string expected)
    {
        var builder = new TaskItemBuilder().Titled(title).WithNote(title);
        var task = (source switch
        {
            "jira" => builder.FromJira("SAAS-1"),
            "retro" => builder.FromRetro("retro-1"),
            _ => builder,
        }).Build();
        await Host.AddAndSaveChangesAsync(task);
        _ollama.Answer = """{"summary":"Kort."}""";

        await Client.PostAsync($"/api/ai/tasks/{task.Id}/summary", null);

        Assert.Contains(expected, PromptSent());
    }

    [Fact]
    public async Task Splitting_a_task_that_does_not_exist_is_a_404()
    {
        var response = await Client.PostAsync("/api/ai/tasks/4242/split", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_ollama.ChatRequests);
    }

    [Fact]
    public async Task A_note_becomes_a_suggested_summary()
    {
        var task = new TaskItemBuilder()
            .Titled("CSV-eksport fejler")
            .WithNote("Kunden oplever at eksporten fejler over 10.000 rækker.\n\nMvh Lene")
            .Build();
        await Host.AddAndSaveChangesAsync(task);
        _ollama.Answer = """{"summary":" Eksporten fejler over 10.000 rækker og skal streame filen. "}""";

        var response = await Client.PostAsync($"/api/ai/tasks/{task.Id}/summary", null);
        var summary = await response.Content.ReadFromJsonAsync<AiNoteSummary>();

        Assert.Equal("Eksporten fejler over 10.000 rækker og skal streame filen.", summary!.Summary);
        Assert.Contains("Mvh Lene", PromptSent());
    }

    [Fact]
    public async Task Summarising_an_empty_note_is_refused_without_asking_the_model()
    {
        var task = new TaskItemBuilder().Titled("Uden note").Build();
        await Host.AddAndSaveChangesAsync(task);

        var response = await Client.PostAsync($"/api/ai/tasks/{task.Id}/summary", null);

        await AssertRefusedAsync(response, ErrorCodes.AiNoteRequired);
        Assert.Empty(_ollama.ChatRequests);
    }

    [Fact]
    public async Task An_empty_summary_is_an_unreadable_answer()
    {
        var task = new TaskItemBuilder().Titled("CSV-eksport fejler").WithNote("Noget fejler.").Build();
        await Host.AddAndSaveChangesAsync(task);
        _ollama.Answer = """{"summary":"  "}""";

        var response = await Client.PostAsync($"/api/ai/tasks/{task.Id}/summary", null);

        await AssertRefusedAsync(response, ErrorCodes.AiUnreadableAnswer);
    }

    [Fact]
    public async Task Summarising_a_task_that_does_not_exist_is_a_404()
    {
        var response = await Client.PostAsync("/api/ai/tasks/4242/summary", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _ollama.DisposeAsync();
    }

    private async Task<AiTaskSuggestion> ParseAsync(string text)
    {
        var response = await Client.PostAsJsonAsync("/api/ai/parse-task", new AiParseTaskRequest { Text = text });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AiTaskSuggestion>())!;
    }

    private string PromptSent()
    {
        var request = Assert.Single(_ollama.ChatRequests);
        var message = Assert.Single(request["messages"]!.AsArray());

        return ((string)message!["content"]!).ReplaceLineEndings("\n");
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(code, error!.Code);
    }
}
