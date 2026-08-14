namespace Todo.Core.Retro;

public sealed class RetroFormatException(string code, string message) : FormatException(message)
{
    public string Code { get; } = code;
}
