namespace Todo.Tunnel;

/// <summary>Where the helper listens on the user's PC, known to both sides.</summary>
public static class HelperAddress
{
    /// <summary>Fixed, because the app's settings page embeds the helper's page from this address.</summary>
    public const int DefaultPort = 47600;

    /// <summary>The Connections page, which the host offers the settings page unless configured otherwise.</summary>
    public const string DefaultPageUrl = "http://127.0.0.1:47600/connections";
}
