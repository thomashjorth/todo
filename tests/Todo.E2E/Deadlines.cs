using System.Globalization;

namespace Todo.E2E;

/// <summary>
/// The deadline text the app writes. Both sides read the same CLDR data, so the patterns here
/// are the browser's own — spelling them out keeps a test from asserting on an ISO string the
/// user never sees.
/// </summary>
internal static class Deadlines
{
    private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

    public static string InDanish(DateOnly date) => date.ToString("d. MMM yyyy", Danish);
}
