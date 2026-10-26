using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.Core.Tags;
using Todo.TestSupport.Builders;

namespace Todo.Api.Tests;

public class TagEndpointsTests : ApiTest
{
    [Fact]
    public async Task A_tag_is_created_trimmed_and_listed_by_name()
    {
        await CreateAsync("  Privat ");
        await CreateAsync("Backend");

        var tags = await ListAsync();

        Assert.Equal(["Backend", "Privat"], tags.Select(t => t.Name));
    }

    /// <summary>"Kunde" and "kunde" are one tag to a reader, so they are one to the app.</summary>
    [Fact]
    public async Task A_name_taken_whatever_its_case_is_refused()
    {
        await CreateAsync("Kunde");

        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagRequest { Name = "kunde" });

        await AssertRefusedAsync(response, ErrorCodes.TagDuplicate);
    }

    [Theory]
    [InlineData("  ", ErrorCodes.TagNameRequired)]
    [InlineData("123456789012345678901234567890123456789012345678901", ErrorCodes.TagNameTooLong)]
    public async Task A_name_that_cannot_be_a_tag_is_refused(string name, string code)
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagRequest { Name = name });

        await AssertRefusedAsync(response, code);
    }

    /// <summary>The rainbow in the order of creation, on every route a tag reaches the client by.</summary>
    [Fact]
    public async Task Each_new_tag_takes_the_next_colour_and_carries_it_everywhere()
    {
        var task = new TaskItemBuilder().Titled("Køb mælk").Build();
        await Host.AddAndSaveChangesAsync(task);

        var rod = await CreateAsync("Rød");
        var orange = await CreateAsync("Orange");
        await Client.PutAsJsonAsync($"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [orange.Id] });

        Assert.Equal([0, 1], new[] { rod.Color, orange.Color });
        Assert.Equal([1, 0], (await ListAsync()).Select(t => t.Color));
        Assert.Equal(1, Assert.Single(Assert.Single(await TasksAsync()).Tags).Color);
        Assert.Contains("\"color\":1", await Client.GetStringAsync("/api/tags"));
    }

    /// <summary>The user's "frigjort": the deleted tag's colour is the one the next tag gets.</summary>
    [Fact]
    public async Task A_deleted_tag_frees_its_colour_for_the_next()
    {
        await CreateAsync("Rød");
        var orange = await CreateAsync("Orange");
        await CreateAsync("Gul");

        await Client.DeleteAsync($"/api/tags/{orange.Id}");

        Assert.Equal(1, (await CreateAsync("Ny")).Color);
        Assert.Equal(3, (await CreateAsync("Grøn")).Color);
    }

    [Fact]
    public async Task A_task_carries_the_tags_it_is_given_and_shows_them_by_name()
    {
        var task = new TaskItemBuilder().Titled("Køb mælk").Build();
        await Host.AddAndSaveChangesAsync(task);
        var privat = await CreateAsync("Privat");
        var indkob = await CreateAsync("Indkøb");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [privat.Id, indkob.Id] });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = Assert.Single(await TasksAsync());
        Assert.Equal(["Indkøb", "Privat"], listed.Tags.Select(t => t.Name));
    }

    /// <summary>The list is the whole answer: setting fewer takes the rest off.</summary>
    [Fact]
    public async Task Setting_a_task_s_tags_replaces_them()
    {
        var task = new TaskItemBuilder().Titled("Køb mælk").Build();
        await Host.AddAndSaveChangesAsync(task);
        var privat = await CreateAsync("Privat");
        var indkob = await CreateAsync("Indkøb");
        await Client.PutAsJsonAsync($"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [privat.Id, indkob.Id] });

        await Client.PutAsJsonAsync($"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [privat.Id] });

        Assert.Equal(["Privat"], Assert.Single(await TasksAsync()).Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task A_tag_that_does_not_exist_is_refused_and_nothing_changes()
    {
        var task = new TaskItemBuilder().Titled("Køb mælk").Build();
        await Host.AddAndSaveChangesAsync(task);
        var privat = await CreateAsync("Privat");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [privat.Id, 4242] });

        await AssertRefusedAsync(response, ErrorCodes.TagUnknown);
        Assert.Empty(Assert.Single(await TasksAsync()).Tags);
    }

    [Fact]
    public async Task Setting_the_tags_of_a_task_that_does_not_exist_is_a_404()
    {
        var response = await Client.PutAsJsonAsync("/api/tasks/4242/tags", new SetTaskTagsRequest { TagIds = [] });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The cascade on TaskTags: a deleted tag leaves no dangling link behind on any task.</summary>
    [Fact]
    public async Task Deleting_a_tag_takes_it_off_every_task()
    {
        var task = new TaskItemBuilder().Titled("Køb mælk").Build();
        await Host.AddAndSaveChangesAsync(task);
        var privat = await CreateAsync("Privat");
        var indkob = await CreateAsync("Indkøb");
        await Client.PutAsJsonAsync($"/api/tasks/{task.Id}/tags", new SetTaskTagsRequest { TagIds = [privat.Id, indkob.Id] });

        var response = await Client.DeleteAsync($"/api/tags/{privat.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["Indkøb"], Assert.Single(await TasksAsync()).Tags.Select(t => t.Name));
        Assert.Equal(["Indkøb"], (await ListAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task Deleting_a_tag_that_does_not_exist_is_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/tags/4242")).StatusCode);
    }

    /// <summary>The list on the wire: a task without tags says so with an empty list, not a missing field.</summary>
    [Fact]
    public async Task A_task_without_tags_sends_an_empty_list()
    {
        await Host.AddAndSaveChangesAsync(new TaskItemBuilder().Titled("Køb mælk").Build());

        var body = await Client.GetStringAsync("/api/tasks?includeCompleted=false&includeSomeday=true");

        Assert.Contains("\"tags\":[]", body);
    }

    private async Task<TagRef> CreateAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagRequest { Name = name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TagRef>())!;
    }

    private async Task<List<TagRef>> ListAsync() =>
        [.. (await Client.GetFromJsonAsync<TagListResponse>("/api/tags"))!.Items];

    private async Task<List<TodoTask>> TasksAsync() =>
        [.. (await Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks?includeCompleted=false&includeSomeday=true"))!.Items];

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }
}
