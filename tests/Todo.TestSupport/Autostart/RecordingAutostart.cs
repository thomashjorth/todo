using Todo.Core.Autostart;

namespace Todo.TestSupport.Autostart;

/// <summary>
/// Stands in for the registry, and remembers what it was asked.
/// </summary>
/// <remarks>
/// The real implementation writes under HKCU, so a test that used it would leave an autostart entry
/// on whatever machine ran the suite - and would find one already there on the developer's own
/// machine, which is worse: the test would pass or fail depending on a setting the user had chosen.
/// Same class of side effect as /api/system/open-link asking Windows to open a real browser, and
/// RecordingLinkLauncher is the same answer to it.
/// <para>
/// It records the path rather than only the fact, because the path is the part that can be wrong in a
/// way nobody notices: an entry pointing at the wrong file starts nothing at sign-in, and the switch
/// still reads on.
/// </para>
/// </remarks>
public sealed class RecordingAutostart : IAutostart
{
    private bool _enabled;

    /// <param name="enabled">The state to start in, so a test can arrange "already on".</param>
    public RecordingAutostart(bool enabled = false) => _enabled = enabled;

    /// <summary>Every path Enable was called with, in order.</summary>
    public List<string> EnabledPaths { get; } = [];

    /// <summary>How many times Disable was called, so "already off" can be told from "not asked".</summary>
    public int DisableCount { get; private set; }

    /// <summary>Set to throw from Enable, for the locked-down-registry branch.</summary>
    public Exception? EnableThrows { get; set; }

    public bool IsEnabled() => _enabled;

    public void Enable(string executablePath)
    {
        if (EnableThrows is not null)
        {
            throw EnableThrows;
        }

        EnabledPaths.Add(executablePath);
        _enabled = true;
    }

    public void Disable()
    {
        DisableCount++;
        _enabled = false;
    }
}
