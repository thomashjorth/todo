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
    /// The flags above are necessary and not sufficient: nothing in the documented workflow ever
    /// hands the spec files to a type checker. `ng build` compiles tsconfig.app.json, which
    /// excludes `src/**/*.spec.ts`, and `ng test` runs through esbuild, which strips types without
    /// checking them. A `string` assigned to a `number` field inside a spec file therefore stays
    /// green forever — that is exactly how a fixture kept building its id as `${bucket}-1` after
    /// the field became a number. This test is the only thing that runs the compiler.
    ///
    /// `--listFiles` earns its place: a compile of nothing also exits 0, so the guard asserts it
    /// was given spec files rather than trusting a green exit code.
    /// </summary>
    [Fact]
    public async Task Spec_project_passes_the_type_checker()
    {
        // The local compiler, invoked directly. Going through npm/npx would drag in this
        // machine's broken PowerShell shim, where `npm` resolves to the unknown command `pm`.
        var compiler = Path.Combine(RepoPaths.WebRoot, "node_modules", ".bin", "tsc.cmd");

        Assert.True(
            File.Exists(compiler),
            $"The TypeScript compiler is missing at {compiler}. That is an uninstalled "
                + "node_modules, not a type error: run `npm.cmd install --prefix src\\Todo.Web`.");

        // Required: the paths inside tsconfig.spec.json are relative to src/Todo.Web.
        var compile = await ExternalCommand.RunAsync(
            compiler,
            RepoPaths.WebRoot,
            ["-p", "tsconfig.spec.json", "--noEmit", "--listFiles"]);

        // --listFiles prints one existing path per line; a diagnostic never names an existing
        // file, because it carries `(line,col): error TSxxxx: ...` after the path.
        var compiled = compile.Lines.Where(File.Exists).ToList();
        var diagnostics = compile.Lines.Where(line => !File.Exists(line)).ToList();

        Assert.True(
            compile.ExitCode == 0,
            $"tsc -p tsconfig.spec.json --noEmit exited with {compile.ExitCode}. The spec files do "
                + "not type-check:"
                + Environment.NewLine
                + (diagnostics.Count > 0
                    ? string.Join(Environment.NewLine, diagnostics)
                    : "(the compiler printed nothing)"));

        var specFiles = compiled
            .Where(path => path.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            specFiles.Count > 0,
            "tsc succeeded without compiling a single *.spec.ts, so a green exit code proved "
                + "nothing. Check `include` in src/Todo.Web/tsconfig.spec.json. Of the "
                + $"{compiled.Count} files handed to the compiler, these were the project's own:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    compiled.Where(path => !path.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                        .DefaultIfEmpty("(none — every file came from node_modules)")));
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
