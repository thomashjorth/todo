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
            WaitingStatuses: SettingList.Read(Value(rows, SettingKeys.JiraWaitingStatuses)),
            // Asymmetric on purpose, and the asymmetry is the writer's: SettingsEndpoints stores on
            // as a row and off as no row at all, because two tests there assert about the whole
            // Settings table. So an absent row must read as off, which is what == "true" says.
            // Writing it as != "false" would read an absent row as on. Measured: that fells two
            // tests in JiraSettingsEndpointsTests, Waiting_issues_are_excluded_until_asked_for and
            // Turning_waiting_back_off_turns_it_off.
            IncludeWaiting: Value(rows, SettingKeys.JiraIncludeWaiting) == "true",
            DutyStatuses: SettingList.Read(Value(rows, SettingKeys.JiraDutyStatuses)),
            // Same asymmetry as IncludeWaiting above, for the same reason, and read the same way.
            OnDuty: Value(rows, SettingKeys.JiraOnDuty) == "true");
    }

    private static string? Value(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
