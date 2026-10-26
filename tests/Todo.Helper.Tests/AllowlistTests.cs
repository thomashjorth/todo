namespace Todo.Helper.Tests;

/// <summary>
/// The helper calls its own three targets and nothing else, so a host that is not ours cannot use it
/// as a way into the corporate network with the user's token on the call.
/// </summary>
public class AllowlistTests
{
    private static readonly HelperSettings Settings = new()
    {
        Jira = new SourceSettings { BaseUrl = "https://jira.example/jira" },
        Ado = new SourceSettings { BaseUrl = "https://ado.example/Min%20Samling" },
        OllamaUrl = "http://127.0.0.1:11434",
    };

    [Theory]
    [InlineData("https://jira.example/jira/rest/api/2/myself", Target.Jira)]
    [InlineData("https://jira.example/jira", Target.Jira)]
    [InlineData("https://ado.example/Min%20Samling/_apis/wit/wiql", Target.Ado)]
    [InlineData("http://127.0.0.1:11434/api/chat", Target.Ollama)]
    public void A_url_under_one_of_its_own_bases_is_that_target(string url, Target expected)
    {
        Assert.Equal(expected, Allowlist.For(new Uri(url), Settings));
    }

    [Theory]
    [InlineData("https://intranet.example/jira/rest/api/2/myself")]   // another host
    [InlineData("http://jira.example/jira/rest/api/2/myself")]        // another scheme
    [InlineData("https://jira.example:8443/jira/rest/api/2/myself")]  // another port
    [InlineData("https://jira.example/jiraevil/rest")]                // a prefix that is not a segment
    [InlineData("https://jira.example/other")]                        // outside the context path
    [InlineData("http://127.0.0.1:8080/api/tasks")]                    // this PC, another port
    public void Anything_else_is_no_target(string url)
    {
        Assert.Equal(Target.None, Allowlist.For(new Uri(url), Settings));
    }

    [Fact]
    public void A_source_without_a_base_url_lets_nothing_through()
    {
        var bare = new HelperSettings { OllamaUrl = "http://127.0.0.1:1" };

        Assert.Equal(Target.None, Allowlist.For(new Uri("https://jira.example/rest"), bare));
    }
}
