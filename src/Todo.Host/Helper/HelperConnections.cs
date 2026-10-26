using Todo.Core.Sources;

namespace Todo.Host.Helper;

/// <summary>
/// The credentials as the connected helper reports them: never a token - it keeps those - only
/// whether it holds one, which is what makes a source count as set up.
/// </summary>
public sealed class HelperConnections(HelperHub hub) : ISourceConnections
{
    public bool Online => hub.Online;

    public SourceConnection Jira => new(Token: null, AuthorizedElsewhere: hub.Hello?.HasJiraToken == true);

    public SourceConnection Ado => new(Token: null, AuthorizedElsewhere: hub.Hello?.HasAdoToken == true);
}
