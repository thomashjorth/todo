using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Core.Ado;

public sealed class AdoSettingsReader(TodoDbContext db)
{
    public async Task<AdoSettings> ReadAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings
            .Where(s => s.Key.StartsWith("ado."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new AdoSettings(
            BaseUrl: Value(rows, SettingKeys.AdoBaseUrl),
            Project: Value(rows, SettingKeys.AdoProject),
            Token: Value(rows, SettingKeys.AdoToken),
            WaitingStates: SettingList.Read(Value(rows, SettingKeys.AdoWaitingStates)),
            // Asymmetric on purpose, and the asymmetry is the writer's: SettingsEndpoints stores on as
            // a row and off as no row at all, because two tests there assert about the whole Settings
            // table. So an absent row must read as off, which is what == "true" says. Writing it as
            // != "false" would read an absent row as on. Measured on the Jira pair, and measured again
            // here: it fells Waiting_work_items_are_excluded_until_asked_for.
            IncludeWaiting: Value(rows, SettingKeys.AdoIncludeWaiting) == "true",
            // Plain SettingList.Read, unlike WorkItemTypes below: an absent or unreadable row reads
            // as an empty list and an empty list means no suggestions, so there is no default to
            // fall back to and nothing is lost by a corrupt row beyond the offer itself.
            DoneStates: SettingList.Read(Value(rows, SettingKeys.AdoDoneStates)),
            WorkItemTypes: WorkItemTypes(Value(rows, SettingKeys.AdoWorkItemTypes)),
            DefaultDeadlineDays: DeadlineDays(Value(rows, SettingKeys.AdoDefaultDeadlineDays)));
    }

    /// <summary>
    /// An absent row is the default, not an empty filter. Nothing can store an empty list - the writer
    /// rejects one and serialises an empty result as no row - so an empty read means the row was never
    /// there or would not parse, and both read as the default. Reading it as empty instead would import
    /// nothing at all, which is not what emptying a list means to the person doing it.
    /// </summary>
    private static IReadOnlyList<string> WorkItemTypes(string? json)
        => SettingList.Read(json) is { Count: > 0 } types ? types : AdoDefaults.WorkItemTypes;

    /// <summary>
    /// The 3-day default lives here rather than in deserialisation, because the wire cannot carry it:
    /// System.Text.Json gives 0 for an absent int and 0 is a meaningful value here - "no deadline" - so
    /// an absent field and a deliberate 0 arrive identical. The absence of a <em>row</em> is what means
    /// "not configured", and that distinction only exists at this layer.
    ///
    /// A stored 0 therefore reads back as 0 rather than being folded into 3: a user who turned the
    /// deadline off must not find it back on. An unparseable or out-of-range value reads as the default,
    /// the same rule SettingList.Read applies to corrupt JSON - unreadable settings must not stop the
    /// app from opening, and the range is checked here as well as on the way in because a hand-edited
    /// negative row would otherwise import every work item already overdue.
    /// </summary>
    private static int DeadlineDays(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            && days >= 0
            && days <= AdoDefaults.DeadlineDaysMax
            ? days
            : AdoDefaults.DeadlineDays;

    private static string? Value(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
