namespace Todo.TestSupport;

/// <summary>
/// What an external tool answered: its exit code and both streams, already drained.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
public sealed record ExternalCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Both streams, one trimmed line per entry, blank lines dropped. Tools that print their
    /// findings one per line — <c>tsc --listFiles</c>, <c>prettier --list-different</c>,
    /// <c>git ls-files</c> — are read through this.
    /// </summary>
    public IReadOnlyList<string> Lines { get; } =
        string.Concat(StandardOutput, "\n", StandardError)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>Both streams as one block, for a failure message.</summary>
    public string Combined =>
        string.Join(
            Environment.NewLine,
            new[] { StandardOutput.TrimEnd(), StandardError.TrimEnd() }
                .Where(stream => stream.Length > 0)
                .DefaultIfEmpty("(the command printed nothing)"));
}
