using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Core.Review;

public sealed class ReviewSettingsReader(TodoDbContext db)
{
    public async Task<ReviewSettings> ReadAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings
            .Where(s => s.Key.StartsWith("review."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new ReviewSettings(
            Weekday: Weekday(Value(rows, SettingKeys.ReviewWeekday)),
            TimeOfDay: TimeOfDay(Value(rows, SettingKeys.ReviewTimeOfDay)),
            LastCompletedAt: LastCompletedAt(Value(rows, SettingKeys.ReviewLastCompletedAt)));
    }

    /// <summary>
    /// The Friday default lives here rather than in deserialisation, because the wire cannot carry
    /// it: System.Text.Json gives 0 for an absent int, and 0 is <em>Sunday</em> - a real weekday, so
    /// an absent field and a deliberate Sunday arrive identical. The absence of a <em>row</em> is
    /// what means "not configured", and that distinction only exists at this layer.
    ///
    /// A stored 0 therefore reads back as Sunday rather than being folded into Friday: a user who
    /// moved the review must not find it back where it started. An unparseable or out-of-range value
    /// reads as the default, the same rule SettingList.Read applies to corrupt JSON - an unreadable
    /// setting must not stop the app from opening - and the range is checked here as well as on the
    /// way in, because a hand-edited row never went through the endpoint.
    /// </summary>
    private static DayOfWeek Weekday(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
            && day >= 0
            && day <= 6
            ? (DayOfWeek)day
            : ReviewDefaults.Weekday;

    private static TimeOnly TimeOfDay(string? value)
        => TimeOnly.TryParseExact(value, ReviewDefaults.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : ReviewDefaults.TimeOfDay;

    /// <summary>
    /// Round-trip parsing, so the value written by the completion route comes back bit for bit and
    /// with its kind intact: the stored text carries the Z, and RoundtripKind is what turns that into
    /// <see cref="DateTimeKind.Utc"/> rather than Local. Without it the comparison in ReviewSchedule
    /// would be off by the machine's offset.
    ///
    /// RoundtripKind <em>alone</em> - it cannot be combined with AdjustToUniversal, which throws
    /// ArgumentException at runtime rather than failing to compile. Measured: it answered 500 on
    /// every read of GET /api/settings.
    /// </summary>
    private static DateTime? LastCompletedAt(string? value)
        => DateTime.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;

    private static string? Value(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
