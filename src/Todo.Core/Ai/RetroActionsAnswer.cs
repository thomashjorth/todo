namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.RetroActionsSchema"/> asks the model for.</summary>
public sealed record RetroActionsAnswer(RetroActionAnswer?[]? Actions);
