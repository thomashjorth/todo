using Todo.Core.Autostart;
using Todo.Core.Errors;
using Todo.Core.Sources;

namespace Todo.Host.Autostart;

/// <summary>
/// What the app registers when it is not running on Windows.
/// </summary>
/// <remarks>
/// It exists because the registry APIs are annotated Windows-only while the target framework is
/// net10.0, so <see cref="RegistryAutostart"/> cannot be registered without asking first - and the
/// honest other branch of that question is a type that says it cannot do this, rather than one that
/// pretends to.
/// <para>
/// Deliberately not a silent no-op. Reporting "off" is true - nothing will start this app at sign-in
/// on a platform where the entry cannot be written - but answering a request to turn it <em>on</em>
/// with a cheerful 200 would leave the switch showing a state it never reached. So the switch reads
/// off and stays off, and the attempt says why.
/// </para>
/// <para>
/// Unreachable on the machine this ships to, and that is worth saying out loud rather than writing a
/// test that pretends otherwise: nothing here can be measured on Windows, and the app has no other
/// platform. It is the compiler's branch, not the user's.
/// </para>
/// </remarks>
public sealed class UnsupportedAutostart : IAutostart
{
    public bool IsEnabled() => false;

    public void Enable(string executablePath) =>
        throw new SourceException(
            ErrorCodes.AutostartUnsupported,
            "Autostart is only implemented for Windows.");

    public void Disable()
    {
        // Nothing to remove, and asking for a state the app is already in is not a failure - the
        // same reason the Windows one passes throwOnMissingValue: false.
    }
}
