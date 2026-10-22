namespace Todo.AiEval;

/// <summary>
/// A guess good enough to catch the mistakes the prompts have actually made: an English answer to a
/// Danish note, and - the one that matters most with Gemma - <b>Norwegian</b> for Danish. The first
/// run of the eval had "Ring tannlegen" for "Call the dentist" and "Bestill flyttingstjenester" as a
/// Danish subtask, and a guess that only knew Danish and English passed the second one.
/// </summary>
/// <remarks>
/// Only unambiguous words count. "for" is both, and on either list it decided a case wrongly: on the
/// Danish one it made "Scan for open vulnerabilities" Danish, on the English one it made "Sikre
/// transportmuligheder for alle" English. A text with no marker at all is <see langword="null"/>, not a failure:
/// "Gather access rights reports" carries no function word in either language.
/// </remarks>
public static class Languages
{
    private static readonly string[] Danish =
        ["og", "til", "på", "af", "med", "skal", "der", "det", "et", "ved", "fra", "om", "som", "nyt", "ikke", "ud", "efter"];

    private static readonly string[] English =
        ["the", "and", "to", "of", "with", "should", "is", "a", "an", "by", "from", "about", "as", "on", "in", "new"];

    /// <summary>Norwegian spellings of words a Danish answer would spell otherwise.</summary>
    private static readonly string[] Norwegian =
        ["inn", "ut", "utstyr", "bestill", "rydd", "tannlege", "tannlegen", "flytting", "mulighet", "muligheter", "ikkje", "etter", "gjennomfør", "sjekk"];

    public static Language? Guess(string text)
    {
        var words = text.ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '\n', '(', ')', '"'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Any(w => Norwegian.Contains(w) || w.EndsWith("tjenester") || w.StartsWith("flytting")))
        {
            return Language.Norwegian;
        }

        var danish = words.Count(Danish.Contains) + text.Count(c => "æøåÆØÅ".Contains(c));
        var english = words.Count(English.Contains);

        return danish == english ? null : danish > english ? Language.Danish : Language.English;
    }
}
