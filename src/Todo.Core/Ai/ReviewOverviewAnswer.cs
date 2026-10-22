namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.ReviewOverviewSchema"/> asks the model for.</summary>
public sealed record ReviewOverviewAnswer(string?[]? Points);
