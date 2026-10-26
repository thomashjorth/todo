using System.Text.Json.Serialization;

namespace Todo.Tunnel;

/// <summary>
/// One message on the tunnel, as JSON text with a "type" discriminator. The helper opens the socket
/// and says hello; the host answers accepted or refused; after that the host sends requests and the
/// helper answers each with a response carrying the same id.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TunnelHello), "hello")]
[JsonDerivedType(typeof(TunnelAccepted), "accepted")]
[JsonDerivedType(typeof(TunnelRefused), "refused")]
[JsonDerivedType(typeof(TunnelRequest), "request")]
[JsonDerivedType(typeof(TunnelResponse), "response")]
public abstract record TunnelMessage;

/// <summary>
/// What the helper can do, sent when it connects and again whenever its settings change. Base URLs
/// and whether a token is held, never the token: the host builds links from the URLs and decides from
/// the flags whether Jira and Azure DevOps are set up.
/// </summary>
public sealed record TunnelHello(
    int Version,
    string? JiraBaseUrl,
    bool HasJiraToken,
    string? AdoBaseUrl,
    bool HasAdoToken) : TunnelMessage;

public sealed record TunnelAccepted : TunnelMessage;

/// <summary>The host will not use this helper, and why - a stale protocol version, typically.</summary>
public sealed record TunnelRefused(string Reason) : TunnelMessage;

/// <summary>
/// One outbound HTTP call for the helper to make. The body is base64, because a JSON string cannot
/// carry arbitrary bytes. Headers never include Authorization: the host has nothing to put there.
/// </summary>
public sealed record TunnelRequest(
    string Id,
    string Method,
    string Url,
    Dictionary<string, string[]> Headers,
    string? Body) : TunnelMessage;

/// <summary>
/// The answer to one request. Error is set when the helper made no call at all - a URL outside its
/// allowlist, or a target that could not be reached - and Status is then 0.
/// </summary>
public sealed record TunnelResponse(
    string Id,
    int Status,
    Dictionary<string, string[]> Headers,
    string? Body,
    string? Error) : TunnelMessage;
