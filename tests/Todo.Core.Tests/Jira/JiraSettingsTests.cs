using Todo.Core.Jira;

namespace Todo.Core.Tests.Jira;

/// <summary>
/// Task 3 shipped this record with no test on it, because nothing read it yet. Slice 11's task 4 is
/// the first caller that acts on the answers, so the boundaries belong here now.
/// </summary>
public class JiraSettingsTests
{
    private static JiraSettings With(string? baseUrl, string? token = "a-token") =>
        new(baseUrl, ProjectKey: "SAAS", token, WaitingStatuses: [], IncludeWaiting: false,
            DutyStatuses: [], OnDuty: false, DoneStatuses: []);

    [Fact]
    public void A_base_url_and_a_token_is_configured()
    {
        Assert.True(With("https://jira.example.invalid").IsConfigured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_token_nothing_is_configured(string? token)
    {
        Assert.False(With("https://jira.example.invalid", token).IsConfigured);
    }

    /// <summary>
    /// The case that made the name dishonest before this task. A blank check says yes to all of
    /// these, and every one of them would have become an unhandled UriFormatException on the first
    /// request — <c>https:/jira</c> because http and https require an authority, the bare host
    /// because it has no scheme at all, and the last two because a scheme being absolute does not
    /// make it callable.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https:/jira")]
    [InlineData("https://")]
    [InlineData("jira.example.invalid")]
    [InlineData("not a url")]
    [InlineData("file:///c:/temp")]
    [InlineData("javascript:alert(1)")]
    public void A_base_url_that_cannot_be_called_is_not_configured(string? baseUrl)
    {
        Assert.False(With(baseUrl).IsConfigured);
        Assert.Null(With(baseUrl).BaseUri);
    }

    /// <summary>
    /// http as well as https, because the fake Jira the source is measured against is on loopback,
    /// and a self-hosted Jira behind a reverse proxy on the same machine can be too.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("https://jira.example.invalid")]
    [InlineData("https://jira.example.invalid/jira")]
    public void An_http_or_https_address_is_callable(string baseUrl)
    {
        Assert.True(With(baseUrl).IsConfigured);
        Assert.NotNull(With(baseUrl).BaseUri);
    }

    [Fact]
    public void The_browse_url_points_at_the_item()
    {
        Assert.Equal(
            "https://jira.example.invalid/browse/SAAS-1",
            With("https://jira.example.invalid").BrowseUrl("SAAS-1"));
    }

    /// <summary>
    /// One of the two layers of trailing-slash trimming, and the one no test reached before. The
    /// other is in SettingsEndpoints on the way in, and it has a test of its own now — see
    /// JiraSettingsEndpointsTests.A_trailing_slash_is_trimmed_off_the_base_url. Measured: both
    /// arrived in the same commit, so no stored value has ever carried a slash, and this trim
    /// therefore cannot fire for anything the shipped app wrote. It stays because this method cannot
    /// see whether its caller came through that endpoint, and because a doubled slash is the one of
    /// the two omissions the user would actually see.
    /// </summary>
    [Fact]
    public void A_trailing_slash_does_not_double_up_in_the_browse_url()
    {
        Assert.Equal(
            "https://jira.example.invalid/browse/SAAS-1",
            With("https://jira.example.invalid/").BrowseUrl("SAAS-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_base_url_there_is_nowhere_to_browse_to(string? baseUrl)
    {
        Assert.Null(With(baseUrl).BrowseUrl("SAAS-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_key_there_is_nothing_to_browse_to(string? externalKey)
    {
        Assert.Null(With("https://jira.example.invalid").BrowseUrl(externalKey!));
    }
}
