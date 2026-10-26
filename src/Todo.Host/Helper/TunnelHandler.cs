using System.Net;
using Todo.Tunnel;

namespace Todo.Host.Helper;

/// <summary>
/// The primary handler of the Jira, Azure DevOps and Ollama clients when the host runs with a helper:
/// every call they make goes through the helper's socket instead of out of the host. Transient, because
/// the client factory disposes a handler when its lifetime ends, and a shared one would be disposed
/// under every other client.
/// </summary>
public sealed class TunnelHandler(HelperHub hub) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Authorization never travels: the host has no token, and the helper sets its own anyway.
        var headers = request.Headers
            .Where(h => !string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        string? body = null;

        if (request.Content is { } content)
        {
            foreach (var header in content.Headers)
            {
                headers[header.Key] = [.. header.Value];
            }

            body = Convert.ToBase64String(await content.ReadAsByteArrayAsync(cancellationToken));
        }

        // AbsoluteUri, never ToString(): ToString unescapes, and the collection name's %20 would reach
        // the helper as a space - the measured trap in CLAUDE.md, one layer further out.
        var answer = await hub.SendAsync(
            new TunnelRequest(
                Guid.NewGuid().ToString("N"),
                request.Method.Method,
                request.RequestUri!.AbsoluteUri,
                headers,
                body),
            cancellationToken);

        if (answer.Error is { } error)
        {
            // The helper made no call - a URL outside its allowlist, or a target it could not reach -
            // which to the source is the same as a host it could not reach.
            throw new HttpRequestException($"The helper made no call: {error}");
        }

        var response = new HttpResponseMessage((HttpStatusCode)answer.Status)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(answer.Body is null ? [] : Convert.FromBase64String(answer.Body)),
        };

        foreach (var (name, values) in answer.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(name, values))
            {
                response.Content.Headers.TryAddWithoutValidation(name, values);
            }
        }

        return response;
    }
}
