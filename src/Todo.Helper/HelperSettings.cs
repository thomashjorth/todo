namespace Todo.Helper;

/// <summary>
/// Everything the helper knows, as the user typed it. The tokens and the helper's key are in here in
/// cleartext only in memory; on disk <see cref="HelperStore"/> keeps them encrypted with DPAPI.
/// </summary>
public sealed record HelperSettings
{
    /// <summary>Where Ollama answers, on this PC. Configurable so a test can point it at a closed port.</summary>
    public const string DefaultOllamaUrl = "http://127.0.0.1:11434";

    /// <summary>The host's address - http://nas:8080 on the LAN, or http://127.0.0.1:5180 under Todo.cmd.</summary>
    public string? ServerUrl { get; init; }

    /// <summary>The key the host knows the hash of, which lets this helper through the tunnel's door.</summary>
    public string? HelperKey { get; init; }

    public SourceSettings Jira { get; init; } = new();

    public SourceSettings Ado { get; init; } = new();

    public string OllamaUrl { get; init; } = DefaultOllamaUrl;
}

/// <summary>One source's address and token.</summary>
public sealed record SourceSettings
{
    public string? BaseUrl { get; init; }

    public string? Token { get; init; }
}
