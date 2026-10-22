using Microsoft.Extensions.DependencyInjection;
using Todo.Host.Ai;

namespace Todo.Api.Tests;

/// <summary>
/// Every test in OllamaClientTests builds the client through OllamaClient.With, so the constructor
/// the shipped app uses, and the typed-client registration that feeds it, are the one path those
/// tests never touch. Same position, and same reason, as JiraTaskSourceRegistrationTests.
/// </summary>
public class OllamaClientRegistrationTests : ApiTest
{
    [Fact]
    public void The_shipped_app_can_build_an_ollama_client()
    {
        using var scope = Host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OllamaClient>());
    }
}
