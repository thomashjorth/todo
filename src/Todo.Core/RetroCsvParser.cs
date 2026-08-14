using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace Todo.Core;

public static partial class RetroCsvParser
{
    private const string ContentColumn = "Content";
    private const string AuthorColumn = "Author";
    private const string CreatedColumn = "Created";
    private const string ZoneColumn = "Zone";
    private const string DueDateColumn = "Action Due Date";
    private const string OwnerColumn = "Action Owner";

    private static readonly string[] DueDateFormats = ["d.M.yyyy", "dd.MM.yyyy"];

    private static readonly string[] CreatedFormats = ["M/d/yy, h:mm tt", "M/d/yyyy, h:mm tt"];

    public static RetroParseResult Parse(string csv)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated = null,
            DetectDelimiter = false,
        };

        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, configuration);

        if (!csvReader.Read() || !csvReader.ReadHeader())
        {
            throw new FormatException($"The retro export is empty. It needs a header row with a '{ContentColumn}' column.");
        }

        var headers = csvReader.HeaderRecord ?? [];
        var columns = IndexColumns(headers);

        if (!columns.TryGetValue(ContentColumn, out var contentColumn))
        {
            throw new FormatException(
                $"The retro export has no '{ContentColumn}' column. Columns found: {string.Join(", ", headers)}.");
        }

        var rows = new List<RetroRow>();
        var skippedRatingCards = 0;

        while (csvReader.Read())
        {
            var content = Read(csvReader, contentColumn);

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (RatingCard().IsMatch(content.Trim()))
            {
                skippedRatingCards++;
                continue;
            }

            var author = Read(csvReader, Column(columns, AuthorColumn));
            var created = Read(csvReader, Column(columns, CreatedColumn));
            var zone = Read(csvReader, Column(columns, ZoneColumn));

            rows.Add(new RetroRow(
                Title: Collapse(content),
                Owner: Trimmed(Read(csvReader, Column(columns, OwnerColumn))),
                Author: Trimmed(author),
                Zone: Collapse(zone ?? string.Empty),
                DueDate: ParseDueDate(Read(csvReader, Column(columns, DueDateColumn))),
                Created: ParseCreated(created),
                DedupKey: DedupKey(content, zone, author, created)));
        }

        return new RetroParseResult(rows, skippedRatingCards);
    }

    private static Dictionary<string, int> IndexColumns(string[] headers)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i]?.Trim();

            if (!string.IsNullOrEmpty(header))
            {
                columns.TryAdd(header, i);
            }
        }

        return columns;
    }

    private static int? Column(Dictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out var index) ? index : null;

    private static string? Read(IReaderRow row, int? column)
        => column is int index && row.TryGetField<string>(index, out var value) ? value : null;

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Collapse(string value) => Whitespace().Replace(value.Trim(), " ");

    private static DateOnly? ParseDueDate(string? value)
        => Trimmed(value) is { } text
            && DateOnly.TryParseExact(text, DueDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static DateTime? ParseCreated(string? value)
        => Trimmed(value) is { } text
            && DateTime.TryParseExact(text, CreatedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)
            ? created
            : null;

    // Keyed on the raw content rather than the prefix-stripped title: stripping depends on the
    // user's alias list, and the key has to survive the user editing that list.
    private static string DedupKey(string? content, string? zone, string? author, string? created)
    {
        var key = string.Join('|', Normalise(content), Normalise(zone), Normalise(author), Normalise(created));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static string Normalise(string? value)
        => value is null ? string.Empty : Collapse(value).ToLowerInvariant();

    [GeneratedRegex(@"^\d+(\s*/\s*\d+)?$")]
    private static partial Regex RatingCard();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
