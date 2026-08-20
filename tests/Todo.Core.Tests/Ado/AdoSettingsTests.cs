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
}
