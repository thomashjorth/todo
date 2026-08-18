namespace Todo.Core.Sources;

/// <summary>
/// Something outside the process said no. Carries an error code so the endpoint can turn it into a
/// 400 the frontend can translate, rather than a 500 with a stack trace the user cannot act on.
/// </summary>
public sealed class SourceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
