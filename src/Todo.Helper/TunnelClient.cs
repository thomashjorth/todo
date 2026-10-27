using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Todo.Tunnel;

namespace Todo.Helper;

/// <summary>
/// The helper's half of the tunnel: it opens the socket out to the host - so the PC never has to accept
/// a connection - says hello, and answers the calls the host sends, each with its own task so a slow
/// Azure DevOps answer does not hold up the next. Reconnects with a backoff from one to thirty seconds,
/// and at once when the user changes where to connect.
/// </summary>
public sealed class TunnelClient(
    HelperStore store, IServiceProvider services, ILogger<TunnelClient> log) : BackgroundService
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private ClientWebSocket? _socket;
    private (string? Server, string? Key) _connectedWith;

    public TunnelState State { get; private set; } = TunnelState.NotConfigured;

    public string? Refusal { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        store.Changed += OnChanged;
        var delay = TimeSpan.FromSeconds(1);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = store.Current;

                if (settings.ServerUrl is null || settings.HelperKey is null)
                {
                    State = TunnelState.NotConfigured;
                    await WaitAsync(Timeout.InfiniteTimeSpan, stoppingToken);
                    continue;
                }

                try
                {
                    State = TunnelState.Connecting;

                    if (await RunAsync(settings, stoppingToken))
                    {
                        delay = TimeSpan.FromSeconds(1);
                    }
                }
                catch (Exception exception) when (exception is WebSocketException or HttpRequestException or InvalidOperationException)
                {
                    log.LogInformation("The tunnel to {Server} is down: {Reason}", settings.ServerUrl, exception.Message);
                }

                if (State != TunnelState.Refused)
                {
                    State = TunnelState.Disconnected;
                }

                await WaitAsync(delay, stoppingToken);
                delay = delay * 2 > MaxDelay ? MaxDelay : delay * 2;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            store.Changed -= OnChanged;
        }
    }

    /// <summary>One connection, from hello to close. True when the host accepted it.</summary>
    private async Task<bool> RunAsync(HelperSettings settings, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {settings.HelperKey}");

        await socket.ConnectAsync(TunnelUri(settings.ServerUrl!), ct);

        _socket = socket;
        _connectedWith = (settings.ServerUrl, settings.HelperKey);

        try
        {
            await SendAsync(HelloFrom(settings), ct);

            switch (await TunnelProtocol.ReceiveAsync(socket, ct))
            {
                case TunnelAccepted:
                    State = TunnelState.Connected;
                    Refusal = null;
                    break;

                case TunnelRefused refused:
                    State = TunnelState.Refused;
                    Refusal = refused.Reason;
                    return false;

                default:
                    return false;
            }

            while (await TunnelProtocol.ReceiveAsync(socket, ct) is { } message)
            {
                if (message is TunnelRequest request)
                {
                    _ = AnswerAsync(request, ct);
                }
            }

            return true;
        }
        finally
        {
            _socket = null;
        }
    }

    private async Task AnswerAsync(TunnelRequest request, CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var answer = await scope.ServiceProvider.GetRequiredService<OutboundCaller>().CallAsync(request, ct);

            await SendAsync(answer, ct);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // The socket went while the call was out; the host fails the call on its side.
        }
    }

    private async Task SendAsync(TunnelMessage message, CancellationToken ct)
    {
        var socket = _socket ?? throw new WebSocketException("The tunnel is not open.");

        await _sending.WaitAsync(ct);

        try
        {
            await TunnelProtocol.SendAsync(socket, message, ct);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>
    /// A new address or key means a new connection; anything else is a new hello on the one there is,
    /// so the host learns of a token or a base URL without the tunnel dropping.
    /// </summary>
    private void OnChanged(HelperSettings settings)
    {
        if (_socket is { State: WebSocketState.Open } socket
            && (settings.ServerUrl, settings.HelperKey) == _connectedWith)
        {
            _ = SendAsync(HelloFrom(settings), CancellationToken.None).ContinueWith(
                t => log.LogInformation("The hello did not reach the host: {Reason}", t.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
            return;
        }

        _socket?.Abort();
        _wake.Release();
    }

    /// <summary>The delay, or until the user changes something - whichever comes first.</summary>
    private Task WaitAsync(TimeSpan delay, CancellationToken ct) => _wake.WaitAsync(delay, ct);

    public static TunnelHello HelloFrom(HelperSettings settings) => new(
        TunnelProtocol.Version,
        settings.Jira.BaseUrl,
        HasJiraToken: !string.IsNullOrEmpty(settings.Jira.Token),
        settings.Ado.BaseUrl,
        HasAdoToken: !string.IsNullOrEmpty(settings.Ado.Token));

    private static Uri TunnelUri(string serverUrl)
    {
        var builder = new UriBuilder(serverUrl.TrimEnd('/') + TunnelProtocol.Path);
        builder.Scheme = builder.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";

        return builder.Uri;
    }
}
