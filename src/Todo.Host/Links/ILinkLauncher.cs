namespace Todo.Host.Links;

/// <summary>
/// Opens a link outside the app window. It is an interface so a test can stand in for it:
/// the real one starts a browser, which a test run must never do.
/// </summary>
public interface ILinkLauncher
{
    void Open(Uri url);
}
