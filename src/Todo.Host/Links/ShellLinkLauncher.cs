using System.Diagnostics;

namespace Todo.Host.Links;

public sealed class ShellLinkLauncher : ILinkLauncher
{
    public void Open(Uri url)
        => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
