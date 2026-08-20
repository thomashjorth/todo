using Microsoft.Extensions.DependencyInjection;
using Todo.Host.Ado;

namespace Todo.Api.Tests;

/// <summary>
/// Every test in AdoTaskSourceTests builds the source through AdoTaskSource.With, because a fake Azure
/// DevOps has no database to read settings out of - so the constructor the shipped app uses, and the
/// typed-client registration that feeds it, are the one path all of those tests never touch. Same
/// position and same reason as JiraTaskSourceRegistrationTests.
///
/// What it would catch is more here than there. The source deliberately has one public constructor and
/// a static factory, because ActivatorUtilities picks a typed client's constructor by counting
/// parameters it can resolve and would find (HttpClient, AdoSettingsReader, IClock) and (HttpClient,
/// AdoSettings, IClock) equally good. And the scopes are mixed - AdoSettingsReader is scoped while
/// IClock is a singleton - so resolving this from the root provider throws, and a clock that was never
/// registered would only show up as a null deadline.
/// </summary>
public class AdoTaskSourceRegistrationTests : ApiTest
{
    [Fact]
    public void The_shipped_app_can_build_an_ado_source()
    {
        using var scope = Host.Services.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<AdoTaskSource>();

        Assert.Equal("ado", source.SourceId);
    }
}
