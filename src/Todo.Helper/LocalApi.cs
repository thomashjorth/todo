using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Todo.Tunnel;

namespace Todo.Helper;

/// <summary>
/// The helper's own API on 127.0.0.1, which delivery 5's microfrontend calls from inside an iframe on
/// the app's settings page. A token goes in and never comes out: the answers say whether one is held.
/// </summary>
public static class LocalApi
{
    /// <summary>
    /// The header every call must carry. A browser will not send a custom header across origins without
    /// a CORS preflight, and nothing here answers one - so a page on another site cannot change where
    /// the helper sends the user's token, which is what changing a base URL would amount to.
    /// </summary>
    public const string RequiredHeader = "X-Todo-Helper";

    public static void MapLocalApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
            Allowed(context.HttpContext.Request)
                ? await next(context)
                : Results.StatusCode(StatusCodes.Status403Forbidden));

        api.MapGet("/connections", (HelperStore store, TunnelClient tunnel) => Status(store.Current, tunnel));

        api.MapPut("/connections/jira", (SourceRequest request, HelperStore store, TunnelClient tunnel) =>
        {
            store.Update(s => s with { Jira = Apply(s.Jira, request) });
            return Status(store.Current, tunnel);
        });

        api.MapPut("/connections/ado", (SourceRequest request, HelperStore store, TunnelClient tunnel) =>
        {
            store.Update(s => s with { Ado = Apply(s.Ado, request) });
            return Status(store.Current, tunnel);
        });

        api.MapPut("/server", (ServerRequest request, HelperStore store, TunnelClient tunnel) =>
        {
            store.Update(s => s with
            {
                ServerUrl = Blank(request.Url)?.TrimEnd('/'),
                HelperKey = Blank(request.Key) ?? s.HelperKey,
            });
            return Status(store.Current, tunnel);
        });

        // A new key, made here and kept here: only its hash leaves, once, for the host's configuration
        // (Helper:KeyHash). Pairing again replaces the key, so the host refuses the helper until it has
        // the new hash - which is the point when the old one may have been seen.
        api.MapPost("/server/pair", (HelperStore store) =>
        {
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            store.Update(s => s with { HelperKey = key });

            return new PairResponse(PasswordHash.Create(key));
        });
    }

    /// <summary>
    /// The custom header, and a Host naming this machine: a page that has rebound its own domain to
    /// 127.0.0.1 still sends its own name as the Host, and is refused on it.
    /// </summary>
    private static bool Allowed(HttpRequest request)
    {
        if (!request.Headers.ContainsKey(RequiredHeader))
        {
            return false;
        }

        var host = request.Host.Host;

        return host is "127.0.0.1" or "localhost";
    }

    /// <summary>
    /// A base URL is replaced as given. A token is replaced when one is sent, cleared when asked, and
    /// otherwise kept - the field in the UI is write-only, so an empty one means "unchanged".
    /// </summary>
    private static SourceSettings Apply(SourceSettings current, SourceRequest request) => new()
    {
        BaseUrl = Blank(request.BaseUrl)?.TrimEnd('/'),
        Token = request.ClearToken ? null : Blank(request.Token) ?? current.Token,
    };

    private static ConnectionsStatus Status(HelperSettings settings, TunnelClient tunnel) => new(
        settings.ServerUrl,
        HasKey: !string.IsNullOrEmpty(settings.HelperKey),
        Tunnel: tunnel.State.ToString(),
        tunnel.Refusal,
        new SourceStatus(settings.Jira.BaseUrl, HasToken: !string.IsNullOrEmpty(settings.Jira.Token)),
        new SourceStatus(settings.Ado.BaseUrl, HasToken: !string.IsNullOrEmpty(settings.Ado.Token)),
        settings.OllamaUrl);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SourceRequest(string? BaseUrl, string? Token, bool ClearToken = false);

public sealed record ServerRequest(string? Url, string? Key);

public sealed record PairResponse(string KeyHash);

public sealed record SourceStatus(string? BaseUrl, bool HasToken);

public sealed record ConnectionsStatus(
    string? ServerUrl,
    bool HasKey,
    string Tunnel,
    string? Refusal,
    SourceStatus Jira,
    SourceStatus Ado,
    string OllamaUrl);
