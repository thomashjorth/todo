using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.Api.Tests;

/// <summary>
/// Every 400 answers with a code the frontend can look up in a translation file. The drift
/// test only compares paths and verbs, so the shape of the body has to be pinned here.
/// </summary>
public class ApiErrorTests : ApiTest
{
    [Fact]
    public async Task An_error_is_an_object_with_a_code_and_a_message()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/settings", new SettingsRequest { Language = "klingon" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"code\":\"settings.unknownLanguage\"", json);
        Assert.Contains("\"message\":\"'klingon' is not a supported language.\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creating_a_task_without_a_title_says_the_title_is_required(string title)
    {
        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title }));

        Assert.Equal("task.titleRequired", error.Code);
    }

    [Fact]
    public async Task Creating_a_task_with_an_over_long_title_says_the_title_is_too_long()
    {
        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = new string('x', 501) }));

        Assert.Equal("task.titleTooLong", error.Code);
    }

    [Fact]
    public async Task Updating_a_task_to_an_empty_title_says_the_title_is_required()
    {
        var task = await CreateTaskAsync();

        var error = await BadRequestAsync(Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            new UpdateTodoTaskRequest { Title = " ", Status = TodoStatus.Open }));

        Assert.Equal("task.titleRequired", error.Code);
    }

    [Fact]
    public async Task Adding_a_subtask_without_a_title_says_the_subtask_title_is_required()
    {
        var task = await CreateTaskAsync();

        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks", new CreateSubTaskRequest { Title = " " }));

        Assert.Equal("subTask.titleRequired", error.Code);
    }

    [Fact]
    public async Task Adding_a_subtask_with_an_over_long_title_says_the_subtask_title_is_too_long()
    {
        var task = await CreateTaskAsync();

        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks",
            new CreateSubTaskRequest { Title = new string('x', 501) }));

        Assert.Equal("subTask.titleTooLong", error.Code);
    }

    [Fact]
    public async Task Updating_a_subtask_to_an_empty_title_says_the_subtask_title_is_required()
    {
        var task = await CreateTaskAsync();
        var subTask = await AddSubTaskAsync(task.Id);

        var error = await BadRequestAsync(Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks/{subTask.Id}",
            new UpdateSubTaskRequest { Title = " ", IsDone = false }));

        Assert.Equal("subTask.titleRequired", error.Code);
    }

    [Fact]
    public async Task An_empty_export_says_the_export_is_empty()
    {
        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            "/api/retro/preview", new RetroPreviewRequest { Csv = string.Empty }));

        Assert.Equal("retro.emptyExport", error.Code);
    }

    [Fact]
    public async Task An_export_without_a_content_column_says_the_column_is_missing()
    {
        var csv = """
            "Text","Author","Zone"
            "Buy a whiteboard","Mette Kirkegaard","Actions"
            """;

        var error = await BadRequestAsync(Client.PostAsJsonAsync(
            "/api/retro/preview", new RetroPreviewRequest { Csv = csv }));

        Assert.Equal("retro.missingContentColumn", error.Code);
    }

    [Fact]
    public async Task Importing_a_row_without_a_key_says_the_key_is_required()
    {
        var error = await ImportBadRequestAsync(new RetroImportRow { Key = " ", Title = "Fine" });

        Assert.Equal("retro.rowKeyRequired", error.Code);
    }

    [Fact]
    public async Task Importing_a_row_without_a_title_says_the_title_is_required()
    {
        var error = await ImportBadRequestAsync(new RetroImportRow { Key = "abc", Title = " " });

        Assert.Equal("retro.rowTitleRequired", error.Code);
    }

    [Fact]
    public async Task Importing_a_row_with_an_over_long_title_says_the_title_is_too_long()
    {
        var error = await ImportBadRequestAsync(
            new RetroImportRow { Key = "abc", Title = new string('x', 501) });

        Assert.Equal("retro.rowTitleTooLong", error.Code);
    }

    [Fact]
    public async Task A_repeated_alias_says_which_name_is_listed_twice()
    {
        var error = await BadRequestAsync(Client.PutAsJsonAsync(
            "/api/retro/aliases", new RetroAliasesRequest { Aliases = ["Thomas", "thomas"] }));

        Assert.Equal("retro.duplicateAlias", error.Code);
        Assert.Contains("thomas", error.Message);
    }

    [Fact]
    public async Task An_unknown_language_says_the_language_is_unknown()
    {
        var error = await BadRequestAsync(Client.PutAsJsonAsync(
            "/api/settings", new SettingsRequest { Language = "klingon" }));

        Assert.Equal("settings.unknownLanguage", error.Code);
    }

    private Task<ApiError> ImportBadRequestAsync(RetroImportRow row)
        => BadRequestAsync(Client.PostAsJsonAsync(
            "/api/retro/import", new RetroImportRequest { Rows = [row] }));

    private static async Task<ApiError> BadRequestAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.NotNull(error);
        Assert.NotEmpty(error.Message);
        return error;
    }

    private async Task<TodoTask> CreateTaskAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = "Har en titel" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        return created;
    }

    private async Task<TodoSubTask> AddSubTaskAsync(long taskId)
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/subtasks", new CreateSubTaskRequest { Title = "Delopgave" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(created);
        return created;
    }
}
