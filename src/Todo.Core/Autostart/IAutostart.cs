namespace Todo.Core.Autostart;

/// <summary>
/// Whether the app starts when the user signs in, and the two ways to change that.
///
/// An interface for one reason: a test must not touch the real thing. The Windows implementation
/// writes under HKCU, and a test that ran it would leave an autostart entry on the machine that ran
/// the suite - the same class of side effect as /api/system/open-link asking Windows to open a real
/// browser window, which is why that one is intercepted in Playwright and faked here.
///
/// Not an abstraction over platforms. There is one implementation and the app is Windows-only; the
/// seam is for the test, and calling it a platform abstraction would invite a second implementation
/// nobody needs.
/// </summary>
public interface IAutostart
{
    /// <summary>
    /// Whether it is on right now, read from wherever the operating system reads it from rather
    /// than from anything this app stored. That is the whole point: remove the entry with another
    /// tool and this has to say so.
    /// </summary>
    bool IsEnabled();

    /// <param name="executablePath">
    /// What the operating system should launch. The caller decides, because the answer differs
    /// between a published exe and a development build, and neither this type nor the endpoint
    /// should be the place that guesses.
    /// </param>
    void Enable(string executablePath);

    void Disable();
}
