using Todo.Core.Ai;

namespace Todo.Core.Tests.Ai;

public class AiOptionsTests
{
    /// <summary>
    /// Ollama listens on 127.0.0.1 only, and "localhost" is tried as ::1 first on Windows, where a
    /// refused IPv6 connect is retried for about two seconds before the IPv4 address is tried.
    /// Measured 2026-10-22 with .NET's HttpClient: 2.0-2.3 s for the first call to localhost, 1-2 ms
    /// to 127.0.0.1 - on every new connection, and the pool drops an idle one after about a minute.
    /// </summary>
    [Fact]
    public void The_default_address_is_the_loopback_ip_rather_than_localhost()
    {
        Assert.Equal("127.0.0.1", new Uri(new AiOptions().BaseUrl).Host);
    }

    /// <summary>
    /// Ollama unloads a model after five idle minutes, and loading it again measured about 13 s on
    /// the user's CPU. A workday keeps it in memory instead.
    /// </summary>
    /// <summary>With its tag, as "ollama list" prints it - the install script's approved list is compared with that.</summary>
    [Fact]
    public void The_embedding_model_is_named_with_its_tag()
    {
        Assert.Equal("embeddinggemma:latest", new AiOptions().EmbeddingModel);
    }

    [Fact]
    public void The_model_is_kept_loaded_for_a_workday()
    {
        Assert.Equal("8h", new AiOptions().KeepAlive);
    }
}
