namespace Todo.Core.Ai;

/// <summary>
/// Where the local model lives. Configuration rather than a setting on the settings page: there is
/// one value today, and a switch of model is an edit of a file or an argument (the AI foundation
/// design's section 2, the user's choice 2026-10-22).
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>
    /// The IP, not "localhost": Ollama listens on 127.0.0.1 only, and on Windows "localhost" is tried
    /// as ::1 first, where a refused connect costs about two seconds before IPv4 is tried. Measured
    /// with .NET's HttpClient 2026-10-22: 2.0-2.3 s against 1-2 ms, on every new connection.
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>
    /// How long Ollama keeps the model loaded after a call, sent with every request. Its own default
    /// is five minutes, and loading the model again measured about 13 s on the user's CPU - most of
    /// the "it locks" the user saw. Eight hours is a workday; longer would hold the memory after the
    /// app has closed. Claude's choice 2026-10-22, for review.
    /// </summary>
    public string KeepAlive { get; set; } = "8h";

    /// <summary>
    /// Must match the default in scripts\install-ai-model.ps1, or the app asks for a model nobody
    /// installed. AiIsolationTests holds the two together.
    /// </summary>
    public string Model { get; set; } = "gemma4:e4b";
}
