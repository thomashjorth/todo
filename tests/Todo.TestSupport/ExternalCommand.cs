using System.Diagnostics;

namespace Todo.TestSupport;

/// <summary>
/// Runs a command line tool and hands back both streams. Three guards need this — the spec
/// project's type checker, the frontend formatting check and the line ending check — and each
/// of them has the same two ways to hang: an undrained pipe and a tool that never exits.
/// </summary>
public static class ExternalCommand
{
    /// <summary>
    /// Generous: the slowest caller is <c>tsc</c>, which takes a couple of seconds.
    /// </summary>
    private static readonly TimeSpan DefaultCeiling = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs <paramref name="executable"/> from <paramref name="workingDirectory"/>. The working
    /// directory is never optional: every caller here passes tool configuration by relative path,
    /// and the tools resolve those paths against the current directory rather than against the
    /// file they were named in.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The tool outlived <paramref name="ceiling"/> and was killed. Thrown rather than returned:
    /// a caller has nothing to assert on, and the message names the command.
    /// </exception>
    public static async Task<ExternalCommandResult> RunAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        TimeSpan? ceiling = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var argumentList = arguments.ToList();

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes while waiting; a full buffer on either one deadlocks the child.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        var limit = ceiling ?? DefaultCeiling;
        using var timeout = new CancellationTokenSource(limit);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw new TimeoutException(
                $"{executable} {string.Join(' ', argumentList)} did not finish within "
                    + $"{limit.TotalMinutes:0} minute(s) and was killed.");
        }

        return new ExternalCommandResult(process.ExitCode, await standardOutput, await standardError);
    }
}
