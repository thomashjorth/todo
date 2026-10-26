using Todo.Core.Sources;

namespace Todo.TestSupport;

/// <summary>
/// The tests' stand-in for the helper: it hands the token straight to the source, which then puts it
/// on the call itself - the one way the shipped host never works, and the only way a test can reach a
/// FakeJira without a helper in between. Online from the start, so a test that sets nothing up meets
/// "not set up" rather than "the helper is offline".
/// </summary>
public sealed class FakeSourceConnections : ISourceConnections
{
    public bool Online { get; set; } = true;

    public SourceConnection Jira { get; set; } = SourceConnection.None;

    public SourceConnection Ado { get; set; } = SourceConnection.None;
}
