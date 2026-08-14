using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core;
using Todo.TestSupport;
using ContractStatus = Todo.Contracts.TodoStatus;

namespace Todo.Api.Tests;

public class RetroEndpointsTests
{
    private const string Header =
        @"""Content"",""Author"",""Created"",""Zone"",""Action Due Date"",""Action Owner""";

    private static readonly string Board = $"""
        {Header}
        "Thomas Hjorth - Write the retro summary","Thomas Hjorth","7/17/26, 1:32 PM","Actions","24.7.2026","Thomas Hjorth"
        "Book a room for the next one","Mette Kirkegaard","7/17/26, 1:33 PM","Actions","","Mette Kirkegaard"
        """;

    private static readonly string BoardWithRatings = $"""
        {Header}
        "8","Mette Kirkegaard","7/17/26, 1:32 PM","Quality","",""
        "9/10","Rasmus Bjerre","7/17/26, 1:33 PM","Mood","",""
        "10 / 10","Sofie Dalgaard","7/17/26, 1:34 PM","Mood","",""
        "The mood was better once the pipeline stopped flaking","Sofie Dalgaard","7/17/26, 1:35 PM","Mood","",""
        """;

    [Fact]
    public async Task Preview_marks_the_rows_owned_by_one_of_my_aliases()
    {
        await using var host = await RunningHost.StartAsync();

        await SetAliasesAsync(host, "thomas hjorth");

        var preview = await PreviewAsync(host, Board);

        var mine = Assert.Single(preview.Rows, r => r.IsMine);
        Assert.Equal("Write the retro summary", mine.Title);
        Assert.Equal("Thomas Hjorth", mine.Owner);
        Assert.Equal(new DateOnly(2026, 7, 24), mine.Deadline);

        var theirs = Assert.Single(preview.Rows, r => !r.IsMine);
        Assert.Equal("Book a room for the next one", theirs.Title);
    }

    [Fact]
    public async Task Preview_owns_nothing_when_no_aliases_are_stored()
    {
        await using var host = await RunningHost.StartAsync();

        var preview = await PreviewAsync(host, Board);

        Assert.All(preview.Rows, row => Assert.False(row.IsMine));
        Assert.Equal("Thomas Hjorth - Write the retro summary", preview.Rows.First().Title);
    }

    [Fact]
    public async Task Preview_stores_nothing()
    {
        await using var host = await RunningHost.StartAsync();

        var preview = await PreviewAsync(host, Board);

        Assert.Equal(2, preview.Rows.Count);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public async Task Preview_reports_how_many_rating_cards_it_dropped()
    {
        await using var host = await RunningHost.StartAsync();

        var preview = await PreviewAsync(host, BoardWithRatings);

        Assert.Equal(3, preview.SkippedRatingCards);
        Assert.Single(preview.Rows);
    }

    [Fact]
    public async Task Import_creates_a_retro_task_with_its_deadline_and_requester()
    {
        await using var host = await RunningHost.StartAsync();

        var preview = await PreviewAsync(host, Board);
        var row = preview.Rows.First(r => r.Title.EndsWith("Write the retro summary"));

        var result = await ImportAsync(host, ToImportRow(row));

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);

        var created = Assert.Single(await ListAsync(host));
        Assert.Equal("retro", created.SourceId);
        Assert.Equal(row.Title, created.Title);
        Assert.Equal(new DateOnly(2026, 7, 24), created.Deadline);
        Assert.Equal("Thomas Hjorth", created.Requester);
        Assert.Equal(ContractStatus.Open, created.Status);
    }

    [Fact]
    public async Task Importing_the_same_rows_twice_creates_nothing_the_second_time()
    {
        await using var host = await RunningHost.StartAsync();

        var rows = (await PreviewAsync(host, Board)).Rows.Select(ToImportRow).ToArray();

        var first = await ImportAsync(host, rows);
        Assert.Equal(2, first.Imported);
        Assert.Equal(0, first.Skipped);

        var second = await ImportAsync(host, rows);
        Assert.Equal(0, second.Imported);
        Assert.Equal(2, second.Skipped);

        Assert.Equal(2, (await ListAsync(host)).Count);
    }

    [Fact]
    public async Task An_imported_row_comes_back_as_already_imported()
    {
        await using var host = await RunningHost.StartAsync();

        var row = (await PreviewAsync(host, Board)).Rows.First();
        await ImportAsync(host, ToImportRow(row));

        var preview = await PreviewAsync(host, Board);

        var imported = Assert.Single(preview.Rows, r => r.AlreadyImported);
        Assert.Equal(row.Key, imported.Key);
    }

    [Fact]
    public async Task An_empty_export_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/api/retro/preview", new RetroPreviewRequest { Csv = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Content", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_export_without_a_content_column_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var csv = """
            "Text","Author","Zone"
            "Buy a whiteboard","Mette Kirkegaard","Actions"
            """;

        var response = await host.Client.PostAsJsonAsync(
            "/api/retro/preview", new RetroPreviewRequest { Csv = csv });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Content", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Aliases_are_trimmed_stored_and_read_back()
    {
        await using var host = await RunningHost.StartAsync();

        var saved = await SetAliasesAsync(host, "  Thomas Hjorth  ", "TH", "   ");

        Assert.Equal(["TH", "Thomas Hjorth"], saved.Aliases);

        var read = await host.Client.GetFromJsonAsync<RetroAliasesResponse>("/api/retro/aliases");

        Assert.NotNull(read);
        Assert.Equal(["TH", "Thomas Hjorth"], read.Aliases);
    }

    [Fact]
    public async Task Aliases_that_differ_only_in_case_are_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PutAsJsonAsync(
            "/api/retro/aliases", new RetroAliasesRequest { Aliases = ["Thomas", "thomas"] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static RetroImportRow ToImportRow(RetroPreviewRow row) => new()
    {
        Key = row.Key,
        Title = row.Title,
        Requester = row.Owner,
        Deadline = row.Deadline,
    };

    private static async Task<RetroPreviewResponse> PreviewAsync(RunningHost host, string csv)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/retro/preview", new RetroPreviewRequest { Csv = csv });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<RetroPreviewResponse>();
        Assert.NotNull(preview);
        return preview;
    }

    private static async Task<RetroImportResponse> ImportAsync(
        RunningHost host, params RetroImportRow[] rows)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/retro/import", new RetroImportRequest { Rows = rows });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RetroImportResponse>();
        Assert.NotNull(result);
        return result;
    }

    private static async Task<RetroAliasesResponse> SetAliasesAsync(
        RunningHost host, params string[] aliases)
    {
        var response = await host.Client.PutAsJsonAsync(
            "/api/retro/aliases", new RetroAliasesRequest { Aliases = aliases });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<RetroAliasesResponse>();
        Assert.NotNull(saved);
        return saved;
    }

    private static async Task<IReadOnlyList<TodoTask>> ListAsync(RunningHost host)
    {
        var body = await host.Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");

        Assert.NotNull(body);
        return [.. body.Items];
    }
}
