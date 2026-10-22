namespace Todo.Core.Ai;

/// <summary>
/// Decides the language an answer is written in, so the prompt can name it instead of asking the
/// model to work it out. Measured with the eval runner 2026-10-22: asked to write "in the language of
/// the task", gemma4:e4b gave "Flyt til nyt kontor" English subtasks; told to fall back on Danish when
/// unsure, it gave "Prepare the quarterly security review" Danish ones.
/// </summary>
/// <remarks>
/// An imported task does not come here at all: its source decides, because the user knows how each
/// one is written (2026-10-22) - retro boards and Azure DevOps in English, Jira in Danish. That rule
/// lives beside the source ids in AiEndpoints. What is left is the user's own typing - the quick-entry
/// line and a task made in the app - where a short title is too little for the model to tell, and
/// enough for a count of function words.
/// <para>
/// Danish is the default, because the app's user is Danish and a line without a single marker word -
/// "Ryd op i backloggen" - is far more likely Danish than English. Only unambiguous words count:
/// "for" is both, and on either list it decided an eval case wrongly.
/// </para>
/// </remarks>
public static class AiLanguage
{
    public const string Danish = "Danish";

    public const string English = "English";

    private static readonly HashSet<string> DanishWords =
        ["og", "til", "på", "af", "med", "skal", "der", "det", "et", "ved", "fra", "om", "som", "nyt", "nye", "ikke", "ud", "efter", "i", "en", "at"];

    private static readonly HashSet<string> EnglishWords =
        ["the", "and", "to", "of", "with", "should", "is", "a", "an", "by", "from", "about", "as", "on", "in", "new", "when", "we", "it"];

    public static string Of(string text)
    {
        var words = text.ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '\n', '\r', '(', ')', '"', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        var danish = words.Count(DanishWords.Contains) + text.Count(c => "æøåÆØÅ".Contains(c));
        var english = words.Count(EnglishWords.Contains);

        return english > danish ? English : Danish;
    }
}
