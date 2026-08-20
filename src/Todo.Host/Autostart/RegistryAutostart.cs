using System.Runtime.Versioning;
using Microsoft.Win32;
using Todo.Core.Autostart;

namespace Todo.Host.Autostart;

/// <summary>
/// Autostart through the registry key Windows itself reads at sign-in.
/// </summary>
/// <remarks>
/// HKCU rather than HKLM, and that is a decision rather than a default: the per-user key needs no
/// administrator, and this app stores one person's tasks in that person's %APPDATA%. A machine-wide
/// entry would start it for every account on the box.
/// <para>
/// Nothing is cached. <see cref="IsEnabled"/> opens the key every time, because the question is what
/// Windows will do at the next sign-in and the answer can change without this app running - the user
/// can delete the value in regedit, or another tool can. A field holding the last known answer would
/// be the one thing here that could be wrong.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RegistryAutostart : IAutostart
{
    /// <summary>
    /// The key Windows reads at sign-in. Under HKEY_CURRENT_USER, so it applies to this user only.
    /// </summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// The value name, which is what the entry is called in the registry - not the path. Constant
    /// rather than derived from the assembly name, so a rename of the assembly cannot leave an
    /// orphaned entry behind that nothing can find to remove.
    /// </summary>
    private const string ValueName = "MandalorianToDo";

    public bool IsEnabled()
    {
        // Read-only, and a missing key is not an error: on a fresh account Run may not exist yet.
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);

        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);

        // Quoted, because a path with a space in it - "C:\Program Files\..." - is read by Windows
        // as a program name followed by arguments otherwise. The app lives wherever the user put
        // it, so this cannot be assumed away.
        key.SetValue(ValueName, $"\"{executablePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

        // throwOnMissingValue defaults to true, and "already off" is not a failure - the caller
        // asked for a state, not for an event.
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
