namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.SuggestTagsSchema"/> asks the model for: tag names from the enum.</summary>
public sealed record TagSuggestionAnswer(string?[]? Tags);
