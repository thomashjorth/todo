using System.Reflection;
using System.Text.Json;
using Todo.Core.Errors;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Every code the API can put on a 400 needs a sentence in both language files, or the user is
/// shown the raw string — <c>jira.statusNameInvalid</c> rather than a message.
///
/// The frontend's parity spec cannot see this. It compares da.json with en.json and nothing else,
/// so a key missing from <em>both</em> is something the two files agree about: exactly the shape
/// <c>jira.statusNameInvalid</c> had while it went untranslated in both. Same blind spot as a
/// before/after comparison over a migration whose "before" picture is made by the migration's own
/// <c>Down</c> — the two sides agree on the wrong thing. This guard is therefore stronger than
/// parity and does not replace it: parity catches a key that exists in only one file, this catches
/// one that is missing from both.
///
/// It lives in C# because only reflection can enumerate the constants. A TypeScript test would have
/// to repeat the list of codes by hand, and that list is the very thing that would go stale.
/// </summary>
public class ErrorCodeTranslationTests
{
    /// <summary>
    /// Named once on purpose: pointing this at another type is the single edit that makes the
    /// "constants were found" assertion below fail, which is how it was seen to fail.
    /// </summary>
    private static readonly Type CodeHolder = typeof(ErrorCodes);

    /// <summary>The object every code hangs under, so the guard walks the same path the app does.</summary>
    private const string Section = "errors";

    [Theory]
    [InlineData("da.json")]
    [InlineData("en.json")]
    public void Every_error_code_has_a_message(string fileName)
    {
        var path = RepoPaths.WebI18nFile(fileName);

        Assert.True(File.Exists(path), $"The translation file is missing at {path}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var codes = Codes();

        // A reflection that found nothing would make every claim below pass on an empty list. Said
        // out loud for the same reason the spec type-checker asserts on --listFiles, and
        // NoRealInstanceTests on how many files it scanned.
        Assert.True(
            codes.Count > 0,
            $"No public const string was found on {CodeHolder.FullName}, so this guard just "
                + "compared an empty list against the translations and passed on nothing.");

        var missing = codes
            .Where(code => Message(document.RootElement, code) is null)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} of {codes.Count} error code(s) have no message under "
                + $"\"{Section}\" in src/Todo.Web/public/i18n/{fileName}, so the API's answer "
                + $"reaches the user as the raw code:{Environment.NewLine}"
                + string.Join(
                    Environment.NewLine,
                    missing.Select(code => $"  {code}  (add {Section}.{code} to {fileName})")));
    }

    /// <summary>
    /// The values of the <c>public const string</c> fields, which is what the wire carries — the
    /// field names are C#'s business and never leave the process.
    /// </summary>
    private static List<string> Codes() => [.. CodeHolder
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        // IsLiteral and not IsInitOnly is what tells a const from a static readonly: only a const
        // has a raw constant value to read here.
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The sentence for one code, or null. The codes are dotted and the JSON is nested — Transloco
    /// reads <c>errors.jira.refused</c> as errors → jira → refused — so the lookup walks the parts
    /// rather than asking for one flat key. An object or an empty string counts as absent: neither
    /// is a message anybody could read.
    /// </summary>
    private static string? Message(JsonElement root, string code)
    {
        if (!root.TryGetProperty(Section, out var node))
        {
            return null;
        }

        foreach (var part in code.Split('.'))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node))
            {
                return null;
            }
        }

        return node.ValueKind == JsonValueKind.String && node.GetString() is { Length: > 0 } message
            ? message
            : null;
    }
}
