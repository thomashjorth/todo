using Todo.Core.Ado;

namespace Todo.Core.Tests.Ado;

/// <summary>
/// The computed half of the record, which is the only part of task 2 that is a pure function of
/// values and therefore the only part that belongs here. Everything else about these settings is a
/// round trip through an endpoint and lives in Todo.Api.Tests.
/// </summary>
public class AdoSettingsTests
{
    private static AdoSettings With(string? baseUrl, string? token = "a-token", string? project = "Saas") =>
        new(baseUrl, project, token, WaitingStates: [], IncludeWaiting: false,
            WorkItemTypes: AdoDefaults.WorkItemTypes, DefaultDeadlineDays: AdoDefaults.DeadlineDays);

    [Fact]
    public void A_collection_url_and_a_token_is_configured()
    {
        Assert.True(With("https://ado.example.invalid/Some%20Collection").IsConfigured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_token_nothing_is_configured(string? token)
    {
        Assert.False(With("https://ado.example.invalid/Some%20Collection", token).IsConfigured);
    }

    /// <summary>
    /// The same boundary JiraSettings measured, restated here rather than shared: a blank check says
    /// yes to every one of these, and each would have become an unhandled UriFormatException on the
    /// first request.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https:/ado")]
    [InlineData("https://")]
    [InlineData("ado.example.invalid")]
    [InlineData("not a url")]
    [InlineData("file:///c:/temp")]
    [InlineData("javascript:alert(1)")]
    public void A_collection_url_that_cannot_be_called_is_not_configured(string? baseUrl)
    {
        Assert.False(With(baseUrl).IsConfigured);
        Assert.Null(With(baseUrl).BaseUri);
    }

    /// <summary>
    /// http as well as https, because the fake ADO the source will be measured against is on loopback.
    /// The escaped space is the case worth having: the measured collection name has a space in it, and
    /// the plan warns that three layers can un-escape it. This one says the record is not one of them.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:5000/Some%20Collection")]
    [InlineData("https://ado.example.invalid/Some%20Collection")]
    [InlineData("https://ado.example.invalid")]
    public void An_http_or_https_address_is_callable(string baseUrl)
    {
        Assert.True(With(baseUrl).IsConfigured);
        Assert.NotNull(With(baseUrl).BaseUri);
    }

    /// <summary>
    /// A deliberate omission, pinned so it is not read as one. ADO puts the project in the path, so it
    /// looks like it belongs in IsConfigured - but slice 11 measured that a missing project has to be
    /// its own refusal with its own error code, or the user is told the whole thing is unconfigured
    /// when one field is blank. The check belongs to whatever makes the call.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_project_is_somebody_elses_refusal(string? project)
    {
        Assert.True(With("https://ado.example.invalid", project: project).IsConfigured);
    }

    /// <summary>
    /// The browse URL task 2 refused to guess. Its prefix is measured - the user's own project page is
    /// <c>{collection}/{project}/_queries</c> - and its tail, <c>_workitems/edit/{id}</c>, is Azure
    /// DevOps' documented work item route rather than something anyone has clicked from here. A wrong
    /// tail shows up as a page that does not open; a wrong prefix would show up as the wrong project.
    /// </summary>
    [Fact]
    public void A_browse_url_puts_the_collection_and_the_project_before_the_work_item()
    {
        Assert.Equal(
            "https://ado.example.invalid/Some%20Collection/Saas/_workitems/edit/15664",
            With("https://ado.example.invalid/Some%20Collection").BrowseUrl("15664"));
    }

    /// <summary>
    /// The asymmetry that is easy to get backwards, and the reason this has a test of its own. The base
    /// URL is a URL the user pasted and already carries <c>%20</c>, so escaping it again would give
    /// <c>%2520</c>; the project is a name the user typed, so it has to be escaped here or a project
    /// with a space in it would break the path. Measured against the same record: one string, two
    /// rules.
    /// </summary>
    [Fact]
    public void The_project_name_is_escaped_while_the_pasted_url_is_left_alone()
    {
        Assert.Equal(
            "https://ado.example.invalid/Some%20Collection/Some%20Project/_workitems/edit/15664",
            With("https://ado.example.invalid/Some%20Collection", project: "Some Project")
                .BrowseUrl("15664"));
    }

    /// <summary>
    /// One of the two layers of trailing-slash trimming, the other being SettingsEndpoints on the way
    /// in. Both on purpose - the endpoint owns what is stored, this owns what is emitted - and this is
    /// the one whose absence a user would see, as a double slash in a link that does not open.
    /// </summary>
    [Fact]
    public void A_trailing_slash_on_the_collection_url_does_not_double_up()
    {
        Assert.Equal(
            "https://ado.example.invalid/Some%20Collection/Saas/_workitems/edit/15664",
            With("https://ado.example.invalid/Some%20Collection/").BrowseUrl("15664"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_collection_url_there_is_no_browse_url(string? baseUrl)
    {
        Assert.Null(With(baseUrl).BrowseUrl("15664"));
    }

    /// <summary>
    /// Unlike Jira, whose browse URL needs only the base URL, this one needs the project too - it is a
    /// path segment. A caller that had checked IsConfigured would therefore still be able to get null
    /// here, which is exactly why the project is its own refusal upstream.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_project_there_is_no_browse_url(string? project)
    {
        Assert.Null(
            With("https://ado.example.invalid/Some%20Collection", project: project)
                .BrowseUrl("15664"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_work_item_id_there_is_no_browse_url(string? externalKey)
    {
        Assert.Null(
            With("https://ado.example.invalid/Some%20Collection").BrowseUrl(externalKey!));
    }
}
