using System.Net.Http.Headers;
using System.Text;
using Todo.Tunnel;

namespace Todo.Helper;

/// <summary>
/// Makes one call the host asked for: inside the allowlist only, with this helper's own Authorization
/// header, and whatever the host sent in that header thrown away first.
/// </summary>
public sealed class OutboundCaller(HttpClient client, HelperStore store)
{
    public async Task<TunnelResponse> CallAsync(TunnelRequest request, CancellationToken ct)
    {
        var settings = store.Current;

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
            || Allowlist.For(url, settings) is var target && target == Target.None)
        {
            return Refused(request, $"{request.Url} is not one of the helper's own targets.");
        }

        using var outbound = new HttpRequestMessage(new HttpMethod(request.Method), url);

        if (request.Body is { } body)
        {
            outbound.Content = new ByteArrayContent(Convert.FromBase64String(body));
        }

        foreach (var (name, values) in request.Headers)
        {
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!outbound.Headers.TryAddWithoutValidation(name, values))
            {
                outbound.Content?.Headers.TryAddWithoutValidation(name, values);
            }
        }

        outbound.Headers.Authorization = AuthorizationFor(target, settings);

        try
        {
            using var response = await client.SendAsync(outbound, ct);

            var headers = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

            return new TunnelResponse(
                request.Id,
                (int)response.StatusCode,
                headers,
                Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync(ct)),
                Error: null);
        }
        catch (HttpRequestException exception)
        {
            return Refused(request, exception.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Refused(request, "it did not answer in time");
        }
    }

    /// <summary>
    /// Bearer for Jira Data Center and Basic with an empty user name for Azure DevOps - both measured
    /// against the user's instances before 2.0.0 (JiraTaskSource, AdoTaskSource). Nothing for Ollama.
    /// </summary>
    private static AuthenticationHeaderValue? AuthorizationFor(Target target, HelperSettings settings) => target switch
    {
        Target.Jira when settings.Jira.Token is { } token => new AuthenticationHeaderValue("Bearer", token),
        Target.Ado when settings.Ado.Token is { } token => new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"))),
        _ => null,
    };

    private static TunnelResponse Refused(TunnelRequest request, string error) =>
        new(request.Id, 0, [], Body: null, error);
}
