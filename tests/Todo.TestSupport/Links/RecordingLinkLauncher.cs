using Todo.Host.Links;

namespace Todo.TestSupport.Links;

/// <summary>Writes down what it was asked to open instead of opening it.</summary>
public sealed class RecordingLinkLauncher : ILinkLauncher
{
    private readonly List<Uri> _opened = [];

    public IReadOnlyList<Uri> Opened => _opened;

    public void Open(Uri url) => _opened.Add(url);
}
