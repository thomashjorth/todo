using System.Collections.Concurrent;
using System.Net.WebSockets;
using Todo.Tunnel;

namespace Todo.Host.Helper;

/// <summary>
/// The one helper the host talks through, if any. A singleton, because the socket outlives every
/// request that uses it. A newer connection replaces an older one - the helper reconnecting after a
/// drop must not be refused by its own ghost - and every call waiting on the old one fails at once
/// rather than after the client's 30 or 60 second timeout.
/// </summary>
public sealed class HelperHub
{
    private HelperSession? _session;

    public bool Online => _session is not null;

    /// <summary>What the connected helper last said about itself, or null when none is connected.</summary>
    public TunnelHello? Hello => _session?.Hello;

    public Task<TunnelResponse> SendAsync(TunnelRequest request, CancellationToken ct)
    {
        var session = _session ?? throw new HelperOfflineException();

        return session.SendAsync(request, ct);
    }

    internal void Attach(HelperSession session) => Interlocked.Exchange(ref _session, session)?.Fail();

    internal void Detach(HelperSession session)
    {
        Interlocked.CompareExchange(ref _session, null, session);
        session.Fail();
    }
}

/// <summary>
/// One connected helper: its socket, what it said in its hello, and the calls waiting for an answer.
/// </summary>
internal sealed class HelperSession(WebSocket socket, TunnelHello hello)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TunnelResponse>> _pending = new();

    // A WebSocket takes one send at a time, and several requests can be in flight at once.
    private readonly SemaphoreSlim _sending = new(1, 1);

    public TunnelHello Hello { get; set; } = hello;

    public async Task<TunnelResponse> SendAsync(TunnelRequest request, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<TunnelResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = answer;

        using var cancellation = ct.Register(() =>
        {
            if (_pending.TryRemove(request.Id, out var waiting))
            {
                waiting.TrySetCanceled(ct);
            }
        });

        try
        {
            await WriteAsync(request, ct);
        }
        catch (WebSocketException)
        {
            _pending.TryRemove(request.Id, out _);
            throw new HelperOfflineException();
        }

        return await answer.Task;
    }

    public async Task WriteAsync(TunnelMessage message, CancellationToken ct)
    {
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

    public void Complete(TunnelResponse response)
    {
        if (_pending.TryRemove(response.Id, out var waiting))
        {
            waiting.TrySetResult(response);
        }
    }

    public void Fail()
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var waiting))
            {
                waiting.TrySetException(new HelperOfflineException());
            }
        }
    }
}
