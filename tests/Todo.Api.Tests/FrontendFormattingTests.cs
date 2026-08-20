using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Prettier has been in the repo since slice 1 and nothing ever ran it, because a full run was
/// unusable: <c>.prettierrc</c> did not set <c>endOfLine</c>, so the default <c>lf</c> made every
/// file in this CRLF working copy a style issue and a real finding drowned. Measured before this
/// class was written: <c>--end-of-line crlf --check .</c> named 28 files, <c>--end-of-line auto</c>
/// named 10 — so 18 of the 28 were line ending noise and ten files had real deviations. With
/// <c>"endOfLine": "auto"</c> in the config the check is line ending agnostic, and that is what
/// makes this guard portable: CI runs on windows-latest, but a <c>crlf</c> setting would fail on
/// any LF checkout, and a guard that depends on <c>core.autocrlf</c> fails for the wrong reason.
///
/// It has cost twice without a guard: the delegation delivery left four real deviations in three
/// files, and the accordion delivery twelve comment lines over 100 characters. No test could see
/// either.
/// </summary>
public class FrontendFormattingTests
{
    /// <summary>
    /// The generated client, which <c>.prettierignore</c> holds out: formatting it would be undone
    /// by the next <c>scripts\generate-api.ps1</c>, and the guard would then be red on a file
    /// nobody edits. Nothing else notices — measured, not assumed:
    /// <see cref="GeneratedCodeFreshnessTests"/> hashes <c>contracts/openapi.yaml</c> rather than
    /// the generator's output, and stays green while this file is reformatted.
    /// Written with forward slashes because that is how Prettier prints paths on Windows too.
    /// </summary>
    private const string GeneratedClient = "src/app/api/todo-client.ts";

    /// <summary>The extensions Prettier owns under <c>src/</c>: 47 .ts, 9 .html, 1 .css today.</summary>
    private static readonly string[] FrontendExtensions = [".ts", ".html", ".css"];

    [Fact]
    public async Task The_frontend_is_formatted()
    {
        var check = await RunPrettierAsync("--check", ".");

        Assert.True(
            check.ExitCode == 0,
            $"prettier --check . exited with {check.ExitCode}. Run "
                + "`.\\node_modules\\.bin\\prettier.cmd --write <the files below>` from src\\Todo.Web:"
                + Environment.NewLine
                + check.Combined);
    }

    /// <summary>
    /// The check above is necessary and not sufficient, in exactly the way
    /// <see cref="FrontendStrictnessTests.Spec_project_passes_the_type_checker"/> is: measured on a
    /// throwaway directory, a <c>.prettierignore</c> holding <c>*</c> makes <c>--check .</c> print
    /// "All matched files use Prettier code style!" and exit <b>0</b> on nothing at all. (An empty
    /// directory is the kinder case — that one exits 2 with "No supported files were found".)
    ///
    /// Prettier has no <c>--listFiles</c>, so the file set is asked for the other way round:
    /// <c>--end-of-line cr</c> asks for CR-only line endings, which no file in this repo has, so
    /// every file Prettier looked at comes back from <c>--list-different</c>. That is one process
    /// launch for the whole scope, and it says the same thing on a CRLF and an LF checkout — where
    /// probing with <c>lf</c> or <c>crlf</c> would only work on one of them.
    /// </summary>
    [Fact]
    public async Task Prettier_sees_every_hand_written_frontend_file()
    {
        var probe = await RunPrettierAsync("--end-of-line", "cr", "--list-different", ".");

        var seen = probe.Lines
            .Select(line => line.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            seen.Count > 0,
            "prettier --end-of-line cr --list-different . named no file at all, so a green "
                + "--check proved nothing. Either the working directory is wrong or "
                + "src\\Todo.Web\\.prettierignore excludes everything. Prettier said:"
                + Environment.NewLine
                + probe.Combined);

        var sourceRoot = Path.Combine(RepoPaths.WebRoot, "src");

        var expected = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => FrontendExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .Select(path => "src/" + Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .Where(path => !string.Equals(path, GeneratedClient, StringComparison.OrdinalIgnoreCase))
            .Order()
            .ToList();

        // A floor as well as a set comparison: two empty sets are equal, and an enumeration that
        // found nothing would otherwise pass. Only the .css file is a single of its kind.
        Assert.True(
            expected.Count > 10,
            $"Only {expected.Count} .ts/.html/.css files were found under {sourceRoot}. That is a "
                + "broken enumeration, not a formatting problem.");

        var missing = expected.Where(path => !seen.Contains(path)).ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} hand-written frontend file(s) are outside Prettier's scope, so "
                + "--check never looked at them. Check src\\Todo.Web\\.prettierignore — it is "
                + "meant to hold out generated files only:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing));

        Assert.False(
            seen.Contains(GeneratedClient),
            $"{GeneratedClient} is inside Prettier's scope. It is generated by "
                + "scripts\\generate-api.ps1, so the next generator run would undo the formatting "
                + "and leave this guard red on a file nobody edits. Put it back in "
                + "src\\Todo.Web\\.prettierignore.");
    }

    private static async Task<ExternalCommandResult> RunPrettierAsync(params string[] arguments)
    {
        // The local binary, invoked directly. Going through npm/npx would drag in this machine's
        // broken PowerShell shim, where `npm` resolves to the unknown command `pm`.
        var prettier = Path.Combine(RepoPaths.WebRoot, "node_modules", ".bin", "prettier.cmd");

        Assert.True(
            File.Exists(prettier),
            $"Prettier is missing at {prettier}. That is an uninstalled node_modules, not a "
                + "formatting problem: run `npm.cmd install --prefix src\\Todo.Web`.");

        // Required: .prettierrc and .prettierignore are resolved against the current directory.
        return await ExternalCommand.RunAsync(prettier, RepoPaths.WebRoot, arguments);
    }
}
