using System.Text.Json;

namespace Todo.Core.Settings;

/// <summary>
/// A setting whose value is a list of strings, stored as one row of JSON. Extracted when the second
/// caller appeared: the parse had lived privately in JiraSettingsReader, and a copy in
/// SettingsEndpoints would have been the third place in this repo where the same rule existed twice.
/// </summary>
public static class SettingList
{
    /// <summary>
    /// A corrupt value reads as an empty list rather than throwing: unreadable settings must not stop
    /// the app from opening, and empty is the safe reading for every list this holds — nothing is
    /// treated as waiting, nothing is pulled in as a duty status, and nobody is suggested.
    /// </summary>
    public static IReadOnlyList<string> Read(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Null for an empty list, so the row is removed rather than stored as "[]". Slice 11 measured why
    /// that matters: two existing tests assert Assert.Empty/Assert.Single on the whole Settings table,
    /// and a leftover row makes them red.
    ///
    /// Trims, drops blanks and dedupes case-insensitively — the same rule RetroEndpoints applies to
    /// the alias list, because these are names a person typed twice in two spellings. Note that this
    /// is <em>not</em> the rule for the Jira status lists: JiraStatusRoles compares them ordinally on
    /// purpose, so folding two statuses Jira keeps apart would be a bug rather than tidiness. They
    /// keep their own writer for that reason.
    /// </summary>
    public static string? Write(IEnumerable<string?> values)
    {
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var name = value?.Trim();

            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            kept.Add(name);
        }

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }
}
