namespace Todo.Core.Sources;

/// <summary>
/// How this process reaches Jira's and Azure DevOps' credentials. On the NAS - and under Todo.cmd -
/// it is the helper on the PC, which holds the tokens and puts them on each call itself, so the host
/// never has one. In the tests it is a fake that hands the token over directly.
/// </summary>
public interface ISourceConnections
{
    /// <summary>
    /// Whether anything is there to make the calls. False means the helper is not connected, which
    /// the endpoints say as helper.offline before they ever ask whether a source is set up.
    /// </summary>
    bool Online { get; }

    SourceConnection Jira { get; }

    SourceConnection Ado { get; }
}

/// <summary>
/// One source's credentials as this process knows them. <see cref="Token"/> is the token this process
/// puts on a call - null when the helper does it instead, which <see cref="AuthorizedElsewhere"/> then
/// says.
/// </summary>
public sealed record SourceConnection(string? Token, bool AuthorizedElsewhere)
{
    public static readonly SourceConnection None = new(null, false);
}

/// <summary>Nothing to call with: what a host without a helper has.</summary>
public sealed class NoSourceConnections : ISourceConnections
{
    public bool Online => false;

    public SourceConnection Jira => SourceConnection.None;

    public SourceConnection Ado => SourceConnection.None;
}
