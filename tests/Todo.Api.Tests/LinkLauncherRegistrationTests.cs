using Microsoft.Extensions.DependencyInjection;
using Todo.Host.Links;

namespace Todo.Api.Tests;

/// <summary>
/// The rest of the link tests stand a recorder in for the launcher, so the registration the
/// shipped app runs with is only ever seen by a test that leaves it alone.
/// </summary>
public class LinkLauncherRegistrationTests : ApiTest
{
    [Fact]
    public void The_shipped_app_launches_links_through_the_shell()
    {
        using var scope = Host.Services.CreateScope();

        Assert.IsType<ShellLinkLauncher>(scope.ServiceProvider.GetRequiredService<ILinkLauncher>());
    }
}
