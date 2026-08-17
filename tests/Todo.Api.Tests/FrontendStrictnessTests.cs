using System.Text.Json;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Slice 6 turned on `strict` and `strictTemplates`. Nothing else fails if they are turned
/// off again: the guard lives only in `ng build`, and discovering it on someone else's
/// machine is too late. These tests are that guard.
///
/// The child configs get their own case because `extends` lets either of them shadow the
/// base — re-adding `"strict": false` in `tsconfig.app.json` would silently undo the slice
/// while the base file still looks correct.
/// </summary>
public class FrontendStrictnessTests
{
    // tsconfig.json is JSONC: the file opens with two /* */ comment blocks.
    private static readonly JsonDocumentOptions Jsonc = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [Fact]
    public void Base_config_compiles_the_frontend_in_strict_mode()
    {
        using var config = Read("tsconfig.json");

        Assert.True(
            Flag(config, "compilerOptions", "strict") is true,
            "src/Todo.Web/tsconfig.json must set \"strict\": true in compilerOptions. "
                + "Without it the frontend builds without strictNullChecks or noImplicitAny.");

        Assert.True(
            Flag(config, "angularCompilerOptions", "strictTemplates") is true,
            "src/Todo.Web/tsconfig.json must set \"strictTemplates\": true in "
                + "angularCompilerOptions. Without it a wrongly typed input binding compiles.");
    }

    [Theory]
    [InlineData("tsconfig.app.json")]
    [InlineData("tsconfig.spec.json")]
    public void Child_config_does_not_loosen_strictness(string fileName)
    {
        using var config = Read(fileName);

        Assert.False(
            Flag(config, "compilerOptions", "strict") is false,
            $"src/Todo.Web/{fileName} switches \"strict\" back off, which overrides the base config.");

        Assert.False(
            Flag(config, "angularCompilerOptions", "strictTemplates") is false,
            $"src/Todo.Web/{fileName} switches \"strictTemplates\" back off, which overrides the base config.");
    }

    /// <summary>
    /// Null when the section or the flag is absent, so a child config that stays silent
    /// (and therefore inherits) is told apart from one that sets the flag to false.
    /// </summary>
    private static bool? Flag(JsonDocument config, string section, string name)
    {
        if (!config.RootElement.TryGetProperty(section, out var options)
            || !options.TryGetProperty(name, out var flag))
        {
            return null;
        }

        return flag.ValueKind == JsonValueKind.True;
    }

    private static JsonDocument Read(string fileName)
    {
        var path = RepoPaths.WebTsConfigFile(fileName);

        Assert.True(File.Exists(path), $"src/Todo.Web/{fileName} is missing.");

        return JsonDocument.Parse(File.ReadAllText(path), Jsonc);
    }
}
