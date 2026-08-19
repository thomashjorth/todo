using Microsoft.Extensions.DependencyInjection;
using Todo.Host.Jira;

namespace Todo.Api.Tests;

/// <summary>
/// Every test in JiraTaskSourceTests builds the source through JiraTaskSource.With, because a fake
/// Jira has no database to read settings out of — so the constructor the shipped app uses, and the
/// typed-client registration that feeds it, are the one path those ten tests never touch. Same
/// position as LinkLauncherRegistrationTests, and the same reason for existing.
///
/// What it would have caught: the source deliberately has one public constructor and a static
/// factory rather than two constructors, because ActivatorUtilities picks a typed client's
/// constructor by counting parameters it can resolve and would have found (HttpClient,
/// JiraSettingsReader) and (HttpClient, JiraSettings) equally good. The scope matters too —
/// JiraSettingsReader is scoped, so resolving this from the root provider throws.
/// </summary>
public class JiraTaskSourceRegistrationTests : ApiTest
{
    [Fact]
    public void The_shipped_app_can_build_a_jira_source()
    {
        using var scope = Host.Services.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<JiraTaskSource>();

        Assert.Equal("jira", source.SourceId);
    }
}
