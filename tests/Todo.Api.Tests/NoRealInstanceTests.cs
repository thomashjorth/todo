using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The suite starts a fake Jira on loopback. Nothing stops a future test from pasting the real
/// instance in "just to check", and that test would then talk to production Jira on every CI run
/// and on every machine that clones the repo — with a real token if one happens to be configured.
///
/// The guard is a text search rather than a network policy on purpose: a network policy would have
/// to be installed in every test host, and one forgotten registration would make it silent. A
/// hostname cannot hide from a file scan.
///
/// What it does not cover, so nobody reads it as more than it is: markdown is not scanned. Prose
/// naming the instance is a documentation decision, not code that calls out, and scanning docs
/// would make this slice's own plan its first offender — the plan quotes this file's own host list
/// and its break-the-guard steps, measured 2026-08-19. It also cannot see a hostname the user types
/// into the settings page at runtime — that is the whole point of the setting, and the guard is
/// about what ships in the repository. Measured 2026-08-19: zero scanned files name the host today,
/// so this guard protects against a future paste rather than having found anything.
/// </summary>
public class NoRealInstanceTests
{
    /// <summary>
    /// Split so a match names which one it hit. Add a host here rather than loosening the test.
    /// </summary>
    private static readonly string[] ForbiddenHosts = ["edora.dk", "atlassian.net"];

    private static readonly string[] SearchedExtensions =
        [".cs", ".ts", ".html", ".json", ".yaml", ".yml", ".ps1", ".cmd"];

    private static readonly string[] SkippedDirectories =
        ["node_modules", "bin", "obj", ".git", "wwwroot", "dist", ".angular"];

    /// <summary>
    /// This file has to spell the hostnames out to look for them, so it would otherwise be its own
    /// first offender. Skipping it means a hostname could hide in exactly one file in the
    /// repository — this one — and that is the cheapest honest trade available. The alternative,
    /// assembling the strings from fragments so the literal never appears, buys nothing and makes
    /// the list unreadable.
    /// </summary>
    private static readonly string ThisFile = $"{nameof(NoRealInstanceTests)}.cs";

    /// <summary>
    /// A file count cannot tell a whole-repository scan from a partial one: measured 2026-08-19 the
    /// scan reaches 172 files, but <c>src</c> alone holds 110 of them, so a recursion that never
    /// left <c>src</c> would clear any threshold low enough to be safe from ordinary file churn.
    /// Naming the areas the scan must reach is the assertion the count cannot make — <c>contracts</c>
    /// is one file, and it is the one most likely to hold a base URL.
    /// </summary>
    private static readonly string[] ReachedAreas = ["src", "tests", "contracts"];

    [Fact]
    public void No_source_file_names_a_real_jira_instance()
    {
        var offenders = new List<string>();
        var areas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var path in Files(RepoPaths.Root))
        {
            scanned++;
            areas.Add(
                Path.GetRelativePath(RepoPaths.Root, path)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);

            var text = File.ReadAllText(path);

            offenders.AddRange(
                from host in ForbiddenHosts
                where text.Contains(host, StringComparison.OrdinalIgnoreCase)
                select $"{Path.GetRelativePath(RepoPaths.Root, path)} names {host}");
        }

        // A scan that reached nothing also finds nothing. Without this, a wrong root or a typo in
        // SearchedExtensions turns the guard into a test that always passes.
        Assert.True(
            scanned > 100,
            $"The scan only reached {scanned} files, so a green result proves nothing. Check "
                + $"RepoPaths.Root ({RepoPaths.Root}) and SearchedExtensions.");

        var missing = ReachedAreas.Except(areas, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.True(
            missing.Length == 0,
            $"The scan never reached {string.Join(", ", missing)}, so it covered only part of the "
                + "repository and a hostname pasted into the part it missed would go unseen. "
                + $"Reached: {string.Join(", ", areas.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))}.");

        Assert.True(
            offenders.Count == 0,
            "A real Jira host is named in the repository. Tests must talk to FakeJira on "
                + "loopback, and a hostname in source is how a test suite ends up calling "
                + "production:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (SearchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), ThisFile, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var path in Files(child))
                {
                    yield return path;
                }
            }
        }
    }
}
