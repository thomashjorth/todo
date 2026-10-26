using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Auth;
using Todo.Core.Settings;
using Todo.Host.Helper;
using Todo.Tunnel;

namespace Todo.Host.Endpoints;

public static class HelperEndpoints
{
    public const string KeyHashKey = "Helper:KeyHash";

    /// <summary>
    /// The socket the helper on the PC opens out to the host. Outside the cookie: the helper is not a
    /// browser, and it proves itself with its own key against <see cref="KeyHashKey"/> - the same hash
    /// format as the password. No hash configured means no helper is let in at all.
    /// </summary>
    public static IEndpointRouteBuilder MapHelperTunnel(this IEndpointRouteBuilder app)
    {
        app.MapGet(TunnelProtocol.Path, async (
            HttpContext http, HelperHub hub, IConfiguration configuration, IServiceScopeFactory scopes) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest();
            }

            if (!Authorized(http, configuration[KeyHashKey]))
            {
                return Results.Unauthorized();
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            var ct = http.RequestAborted;

            if (await TunnelProtocol.ReceiveAsync(socket, ct) is not TunnelHello hello)
            {
                return Results.Empty;
            }

            // Refused rather than guessed at: a helper on another version would read every message
            // here a little differently, and the user can fix it by updating the helper.
            if (hello.Version != TunnelProtocol.Version)
            {
                await TunnelProtocol.SendAsync(
                    socket,
                    new TunnelRefused(
                        $"The host speaks version {TunnelProtocol.Version} and the helper {hello.Version}. Update the helper."),
                    ct);
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "version", ct);
                return Results.Empty;
            }

            await StoreBaseUrlsAsync(scopes, hello, ct);

            var session = new HelperSession(socket, hello);
            hub.Attach(session);

            try
            {
                await session.WriteAsync(new TunnelAccepted(), ct);

                while (await TunnelProtocol.ReceiveAsync(socket, ct) is { } message)
                {
                    switch (message)
                    {
                        case TunnelResponse response:
                            session.Complete(response);
                            break;

                        // Again whenever the user changes the helper's settings, so the host's view
                        // follows without a reconnect.
                        case TunnelHello again:
                            session.Hello = again;
                            await StoreBaseUrlsAsync(scopes, again, ct);
                            break;
                    }
                }
            }
            catch (WebSocketException)
            {
                // The helper went away; Detach below fails whatever was still waiting on it.
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                hub.Detach(session);
            }

            // The helper said goodbye; answer it, or its CloseAsync fails on a handshake the host never
            // finished. Measured: without this, every orderly disconnect threw on the helper's side.
            if (socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }

            return Results.Empty;
        })
        // Not part of the API's contract: it is the helper's door, not the browser's.
        .ExcludeFromDescription();

        return app;
    }

    private static bool Authorized(HttpContext http, string? keyHash)
    {
        if (string.IsNullOrWhiteSpace(keyHash))
        {
            return false;
        }

        var header = http.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.Ordinal)
            && PasswordHash.Verify(header["Bearer ".Length..], keyHash);
    }

    /// <summary>
    /// The base URLs the helper reported, kept as rows so links survive a helper that is away and a
    /// host that restarted. A URL it no longer has is removed, because it owns them. The trailing slash
    /// is trimmed here, the one place they are written, or JiraSettings.BrowseUrl would double it.
    /// </summary>
    private static async Task StoreBaseUrlsAsync(IServiceScopeFactory scopes, TunnelHello hello, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        await StoreAsync(db, SettingKeys.JiraBaseUrl, hello.JiraBaseUrl, ct);
        await StoreAsync(db, SettingKeys.AdoBaseUrl, hello.AdoBaseUrl, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task StoreAsync(TodoDbContext db, string key, string? url, CancellationToken ct)
    {
        var value = url?.Trim().TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : null;
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);

        if (value is null)
        {
            if (row is not null)
            {
                db.Settings.Remove(row);
            }
        }
        else if (row is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }
}
