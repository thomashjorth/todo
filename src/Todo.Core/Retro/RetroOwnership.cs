namespace Todo.Core.Retro;

public static class RetroOwnership
{
    private const string Marker = " - ";

    // SQLite's unique index on UserAlias.Value is case-sensitive, so "Thomas" and "thomas" can
    // both be stored. Matching has to ignore case here rather than lean on the index.
    public static bool IsOwnedBy(string? owner, IReadOnlyCollection<string> aliases)
        => !string.IsNullOrWhiteSpace(owner)
           && aliases.Any(alias => string.Equals(alias?.Trim(), owner.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string StripOwnerPrefix(string title, IReadOnlyCollection<string> aliases)
    {
        var marker = title.IndexOf(Marker, StringComparison.Ordinal);

        if (marker <= 0)
        {
            return title;
        }

        return IsOwnedBy(title[..marker], aliases)
            ? title[(marker + Marker.Length)..].TrimStart()
            : title;
    }
}
