namespace Todo.Helper;

/// <summary>Which of the helper's own targets a URL belongs to.</summary>
public enum Target
{
    None,
    Jira,
    Ado,
    Ollama,
}

/// <summary>
/// The only places the helper calls: its own Jira, its own Azure DevOps collection and Ollama on this
/// PC - built from the helper's settings, never from what the host sends. Were the host the one to
/// name the targets, a host that was not ours could use the helper as a way into the corporate network,
/// with the user's token on the call.
/// </summary>
public static class Allowlist
{
    public static Target For(Uri url, HelperSettings settings)
    {
        if (Matches(url, settings.Jira.BaseUrl))
        {
            return Target.Jira;
        }

        if (Matches(url, settings.Ado.BaseUrl))
        {
            return Target.Ado;
        }

        return Matches(url, settings.OllamaUrl) ? Target.Ollama : Target.None;
    }

    /// <summary>
    /// Same scheme, host and port, and a path under the base's - on a whole segment, so a base of
    /// /jira does not let /jiraevil through.
    /// </summary>
    private static bool Matches(Uri url, string? baseUrl)
    {
        if (baseUrl is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var root))
        {
            return false;
        }

        if (!string.Equals(url.Scheme, root.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(url.Host, root.Host, StringComparison.OrdinalIgnoreCase)
            || url.Port != root.Port)
        {
            return false;
        }

        var prefix = root.AbsolutePath.TrimEnd('/');
        var path = url.AbsolutePath;

        return prefix.Length == 0
            || string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
