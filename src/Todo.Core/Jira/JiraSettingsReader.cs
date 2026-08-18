using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Core.Jira;

public sealed class JiraSettingsReader(TodoDbContext db)
{
    public async Task<JiraSettings> ReadAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings
            .Where(s => s.Key.StartsWith("jira."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new JiraSettings(
            BaseUrl: Value(rows, SettingKeys.JiraBaseUrl),
            ProjectKey: Value(rows, SettingKeys.JiraProjectKey),
            Token: Value(rows, SettingKeys.JiraToken),
            WaitingStatuses: ReadList(Value(rows, SettingKeys.JiraWaitingStatuses)),
            IncludeWaiting: Value(rows, SettingKeys.JiraIncludeWaiting) == "true");
    }

    private static string? Value(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// The list is one row of JSON. A corrupt value reads as an empty list rather than throwing:
    /// unreadable settings must not stop the app from opening, and an empty waiting list is the
    /// safe reading — it means nothing is treated as waiting.
    /// </summary>
    private static IReadOnlyList<string> ReadList(string? json)
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
}
