using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Todo.Core.Ai;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The shipped default is the user's own Ollama, and it answers on the machine the suite runs on.
/// Without RunningHost's closed port, every E2E test would ask the real model at startup and the
/// result would depend on the machine: ready on one, not ready in CI.
/// </summary>
public partial class AiIsolationTests : ApiTest
{
    [Fact]
    public void A_test_host_never_points_at_the_real_ollama()
    {
        var options = Host.Services.GetRequiredService<IOptions<AiOptions>>().Value;
        var address = new Uri(options.BaseUrl);

        Assert.Equal("127.0.0.1", address.Host);
        Assert.NotEqual(new Uri(new AiOptions().BaseUrl).Port, address.Port);
    }

    /// <summary>
    /// Todo.cmd installs one model and the host asks for one model, and the two are written in two
    /// languages in two files. A drift would leave the app asking for a model nobody installed.
    /// </summary>
    [Fact]
    public void The_host_asks_for_the_model_the_install_script_installs()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "install-ai-model.ps1"));
        var match = ScriptModel().Match(script);

        Assert.True(match.Success, "install-ai-model.ps1 no longer declares [string]$Model = '...'.");
        Assert.Equal(match.Groups[1].Value, new AiOptions().Model);
    }

    /// <summary>
    /// The same for the embedding model, and sharper: the script deletes every model not on its
    /// approved list, so a drift here would have Todo.cmd delete the model the app asks for.
    /// </summary>
    [Fact]
    public void The_host_asks_for_the_embedding_model_the_install_script_installs()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "install-ai-model.ps1"));
        var match = ScriptEmbeddingModel().Match(script);

        Assert.True(match.Success, "install-ai-model.ps1 no longer declares [string]$EmbeddingModel = '...'.");
        Assert.Equal(match.Groups[1].Value, new AiOptions().EmbeddingModel);
    }

    [GeneratedRegex(@"\[string\]\$Model\s*=\s*'([^']+)'")]
    private static partial Regex ScriptModel();

    [GeneratedRegex(@"\[string\]\$EmbeddingModel\s*=\s*'([^']+)'")]
    private static partial Regex ScriptEmbeddingModel();
}
