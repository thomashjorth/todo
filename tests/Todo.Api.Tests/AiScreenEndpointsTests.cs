using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Ai;
using Todo.Core.Errors;
using Todo.Core.Settings;
using Todo.Core.Time;
using Todo.TestSupport.Ai;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

namespace Todo.Api.Tests;

/// <summary>
/// The second round of AI routes against a fake Ollama. The fake's vectors count shared words
/// (<see cref="FakeOllama.VectorOf"/>), so "close in meaning" here means "sharing words": these tests
/// measure the app around the model, and how well the real one ranks is the eval runner's job.
/// </summary>
public class AiScreenEndpointsTests : ApiTest
{
    /// <summary>A Thursday at noon, so a week back and ninety days back are both exact.</summary>
    private static readonly FixedClock Clock = new(new DateOnly(2026, 10, 22), new TimeOnly(12, 0));

    private FakeOllama _ollama = null!;

    public override async ValueTask InitializeAsync()
    {
        _ollama = await FakeOllama.StartAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _ollama.DisposeAsync();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock>(Clock);
        services.Configure<AiOptions>(o =>
        {
            o.BaseUrl = _ollama.BaseUrl;
            o.EmbeddingModel = FakeOllama.EmbeddingModel;
        });
    }

    [Fact]
    public async Task Retro_notes_get_actions_matched_back_by_number()
    {
        _ollama.Answer = """
            {"actions":[
              {"note":2,"title":"Fix the flaky build","owner":"Mads"},
              {"note":1,"title":"Write integration tests before refactoring","owner":null}]}
            """;

        var rows = await RetroActionsAsync(
            new AiRetroNote { Key = "a", Text = "We should test more" },
            new AiRetroNote { Key = "b", Text = "The build is flaky again, Mads?" });

        Assert.Equal(["a", "b"], rows.Select(r => r.Key));
        Assert.Equal("Write integration tests before refactoring", rows[0].Title);
        Assert.Equal("Mads", rows[1].Owner);
        Assert.Contains("1. We should test more\n2. The build is flaky again, Mads?", PromptSent());
    }

    /// <summary>
    /// The guess is checked against the same aliases the preview uses for "isMine", so a row can say
    /// "probably you" - and a board that names an owner keeps it, whatever the model guessed.
    /// </summary>
    [Fact]
    public async Task A_guessed_owner_is_checked_against_the_aliases_and_never_beats_the_board()
    {
        await Host.AddAndSaveChangesAsync(new UserAlias { Value = "Thomas" });
        _ollama.Answer = """
            {"actions":[{"note":1,"title":"Renew the certificate","owner":"Thomas"},
                        {"note":2,"title":"Book the room","owner":"Thomas"}]}
            """;

        var rows = await RetroActionsAsync(
            new AiRetroNote { Key = "a", Text = "Thomas: the certificate expires" },
            new AiRetroNote { Key = "b", Text = "Book a room", Owner = "Bo" });

        Assert.Equal("Thomas", rows[0].Owner);
        Assert.True(rows[0].OwnerIsMine);
        Assert.Null(rows[1].Owner);
        Assert.False(rows[1].OwnerIsMine);
    }

    [Fact]
    public async Task No_retro_notes_are_refused_without_asking_the_model()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/ai/retro-actions", new AiRetroActionsRequest { Rows = [new AiRetroNote { Key = "a", Text = " " }] });

        await AssertRefusedAsync(response, ErrorCodes.AiTextRequired);
        Assert.Empty(_ollama.ChatRequests);
    }

    /// <summary>
    /// The fake's vectors count shared words, so the closest task is the one sharing the most: the
    /// exact title first, the one sharing one word of three far enough behind to be left out by the
    /// margin, and the one sharing none not at all.
    /// </summary>
    [Fact]
    public async Task Search_finds_the_closest_task_and_leaves_out_one_far_behind()
    {
        var certificate = new TaskItemBuilder().Titled("Forny certifikatet på testserveren").Build();
        var milk = new TaskItemBuilder().Titled("Køb mælk").Build();
        var exact = new TaskItemBuilder().Titled("Certifikatet").Build();
        await Host.AddAndSaveChangesAsync(certificate, milk, exact);

        var hits = await SearchAsync("certifikatet");

        Assert.Equal([exact.Id], hits.Select(h => h.Id));
    }

    /// <summary>
    /// Done tasks are searched too - 37 of the user's 44 were done when the search was measured - and
    /// a hit carries what the screen shows without the task being loaded: title, status, and the link
    /// for an imported one.
    /// </summary>
    [Fact]
    public async Task A_done_imported_task_is_found_with_its_status_and_link()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Forny licensen").FromJira("SAAS-9").Done().Build(),
            new Setting { Key = SettingKeys.JiraBaseUrl, Value = "https://jira.test" });

        var hit = Assert.Single(await SearchAsync("forny licensen"));

        Assert.Equal("Forny licensen", hit.Title);
        Assert.Equal(TodoStatus.Done, hit.Status);
        Assert.Equal("https://jira.test/browse/SAAS-9", hit.ExternalUrl);
    }

    /// <summary>
    /// Titles only: a long note pulled the right task down on the user's data (0.34 on the title
    /// against 0.23 with the note). A word only in the note is therefore not a match.
    /// </summary>
    [Fact]
    public async Task A_word_only_in_the_note_does_not_make_a_hit()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Ret fejlen").WithNote("certifikatet udløber").Build());

        Assert.Empty(await SearchAsync("certifikatet udløber"));
    }

    /// <summary>
    /// The second search embeds only the query: the tasks are in the cache, and a task is embedded
    /// again only when its text changes.
    /// </summary>
    [Fact]
    public async Task A_task_is_embedded_once_until_its_text_changes()
    {
        var task = new TaskItemBuilder().Titled("Forny certifikatet").Build();
        await Host.AddAndSaveChangesAsync(task);

        await SearchAsync("certifikatet");
        await SearchAsync("certifikatet");

        Assert.Single(_ollama.EmbeddedTexts, t => t == "Forny certifikatet");

        await Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTodoTaskRequest
        {
            Title = "Forny certifikatet i dag",
            Status = TodoStatus.Open,
        });
        await SearchAsync("certifikatet");

        Assert.Contains("Forny certifikatet i dag", _ollama.EmbeddedTexts);
    }

    [Fact]
    public async Task A_blank_search_is_refused()
    {
        var response = await Client.PostAsJsonAsync("/api/ai/search", new AiSearchRequest { Query = " " });

        await AssertRefusedAsync(response, ErrorCodes.AiTextRequired);
    }

    [Fact]
    public async Task Search_without_the_embedding_model_is_a_translatable_refusal()
    {
        await Host.AddAndSaveChangesAsync(new TaskItemBuilder().Titled("Køb mælk").Build());
        _ollama.WithoutEmbeddingModel = true;

        var response = await Client.PostAsJsonAsync("/api/ai/search", new AiSearchRequest { Query = "mælk" });

        await AssertRefusedAsync(response, ErrorCodes.AiUnavailable);
    }

    /// <summary>
    /// Only tasks from another source are candidates: a Jira row that resembles a Jira task is the
    /// import's own business, and it says "imported earlier" already.
    /// </summary>
    [Fact]
    public async Task A_row_resembling_a_task_from_another_source_is_reported()
    {
        var manual = new TaskItemBuilder().Titled("Ret login timeout for mange roller").Build();
        var jira = new TaskItemBuilder().Titled("Ret login timeout for mange roller").FromJira("SAAS-1").Build();
        await Host.AddAndSaveChangesAsync(manual, jira);

        var response = await Client.PostAsJsonAsync("/api/ai/duplicates", new AiDuplicatesRequest
        {
            SourceId = "jira",
            Rows =
            [
                new AiDuplicateCandidate { Key = "SAAS-2", Title = "Login timeout for mange roller" },
                new AiDuplicateCandidate { Key = "SAAS-3", Title = "Køb mælk" },
            ],
        });
        var matches = (await response.Content.ReadFromJsonAsync<AiDuplicatesResponse>())!.Matches;

        var match = Assert.Single(matches);
        Assert.Equal("SAAS-2", match.Key);
        Assert.Equal(manual.Id, match.TaskId);
        Assert.Equal("Ret login timeout for mange roller", match.TaskTitle);
    }

    [Fact]
    public async Task No_tasks_from_another_source_means_no_matches_and_no_model_call()
    {
        await Host.AddAndSaveChangesAsync(new TaskItemBuilder().Titled("Login").FromJira("SAAS-1").Build());

        var response = await Client.PostAsJsonAsync("/api/ai/duplicates", new AiDuplicatesRequest
        {
            SourceId = "jira",
            Rows = [new AiDuplicateCandidate { Key = "SAAS-2", Title = "Login" }],
        });

        Assert.Empty((await response.Content.ReadFromJsonAsync<AiDuplicatesResponse>())!.Matches);
        Assert.Empty(_ollama.EmbeddedTexts);
    }

    /// <summary>
    /// The facts come from the database, not from the model, and each boundary is pinned by a task on
    /// either side of it: completed six days ago against eight, created a hundred days ago against
    /// sixty. The waiting tasks are seeded newest first, so an unsorted list would show it.
    /// </summary>
    [Fact]
    public async Task The_review_overview_is_written_from_the_facts_the_host_gathers()
    {
        var builder = () => new TaskItemBuilder(Clock);
        var recent = builder().Titled("Send referatet").Done().Build();
        recent.CompletedAt = Clock.UtcNow.AddDays(-6);
        var old = builder().Titled("Gammel sag").Done().Build();
        old.CompletedAt = Clock.UtcNow.AddDays(-8);
        var staleSomeday = builder().Titled("Lær harmonika").Someday().Build();
        staleSomeday.CreatedAt = Clock.UtcNow.AddDays(-100);
        var freshSomeday = builder().Titled("Læs om typografi").Someday().Build();
        freshSomeday.CreatedAt = Clock.UtcNow.AddDays(-60);
        await Host.AddAndSaveChangesAsync(
            recent,
            old,
            builder().Titled("Svar Bo").WaitingFor("Bo", Clock.UtcNow.AddDays(-3)).Build(),
            builder().Titled("Svar revisoren").WaitingFor("Revisoren", Clock.UtcNow.AddDays(-21)).Build(),
            builder().Titled("Betal regningen").Overdue().Build(),
            staleSomeday,
            freshSomeday);
        _ollama.Answer = """{"points":[" Giv Betal regningen en ny dato. ","","Ryk revisoren, der har ventet 21 dage."]}""";

        var response = await Client.PostAsync("/api/ai/review-overview", null);
        var overview = await response.Content.ReadFromJsonAsync<AiReviewOverview>();

        Assert.Equal(["Giv Betal regningen en ny dato.", "Ryk revisoren, der har ventet 21 dage."], overview!.Points);
        var prompt = PromptSent();

        // The done tasks are not in the prompt at all: the user found the recap useless (2026-10-22).
        Assert.DoesNotContain("Send referatet", prompt);
        Assert.DoesNotContain("Gammel sag", prompt);
        Assert.Contains("- Svar revisoren - waiting on Revisoren for 21 days\n- Svar Bo - waiting on Bo for 3 days", prompt);
        Assert.Contains("- Betal regningen - 1 days overdue", prompt);
        Assert.Contains("- Lær harmonika - created 100 days ago", prompt);
        Assert.DoesNotContain("Læs om typografi", prompt);
    }

    [Fact]
    public async Task A_review_overview_without_points_is_an_unreadable_answer()
    {
        _ollama.Answer = """{"points":[" "]}""";

        var response = await Client.PostAsync("/api/ai/review-overview", null);

        await AssertRefusedAsync(response, ErrorCodes.AiUnreadableAnswer);
    }

    private async Task<List<AiRetroAction>> RetroActionsAsync(params AiRetroNote[] notes)
    {
        var response = await Client.PostAsJsonAsync("/api/ai/retro-actions", new AiRetroActionsRequest { Rows = notes });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return [.. (await response.Content.ReadFromJsonAsync<AiRetroActionsResponse>())!.Rows];
    }

    private async Task<List<AiSearchHit>> SearchAsync(string query)
    {
        var response = await Client.PostAsJsonAsync("/api/ai/search", new AiSearchRequest { Query = query });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return [.. (await response.Content.ReadFromJsonAsync<AiSearchResponse>())!.Hits];
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
