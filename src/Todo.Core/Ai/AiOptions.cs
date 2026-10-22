namespace Todo.Core.Ai;

/// <summary>
/// Where the local model lives. Configuration rather than a setting on the settings page: there is
/// one value today, and a switch of model is an edit of a file or an argument (the AI foundation
/// design's section 2, the user's choice 2026-10-22).
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Must match the default in scripts\install-ai-model.ps1, or the app asks for a model nobody
    /// installed. AiIsolationTests holds the two together.
    /// </summary>
    public string Model { get; set; } = "gemma4:e4b";
}
