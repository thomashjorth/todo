namespace Todo.Core.Ai;

/// <summary>One retro note made an action. <see cref="Note"/> is the note's number in the prompt, counted from one.</summary>
public sealed record RetroActionAnswer(int Note, string? Title, string? Owner);
