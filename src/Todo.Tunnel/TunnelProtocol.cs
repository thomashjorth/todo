using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Todo.Tunnel;

public static class TunnelProtocol
{
    /// <summary>
    /// Raised when either side changes what a message means. The host refuses a helper on another
    /// version rather than guessing, and the app tells the user to update the helper.
    /// </summary>
    public const int Version = 1;

    /// <summary>The route on the host the helper connects to.</summary>
    public const string Path = "/api/helper/tunnel";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>One message as one WebSocket text message, however many frames it takes.</summary>
    public static async Task SendAsync(WebSocket socket, TunnelMessage message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);

        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <summary>
    /// The next message, or null when the other side closed. A WebSocket message can arrive in many
    /// frames - an Azure DevOps answer runs to megabytes - so frames are gathered until EndOfMessage.
    /// </summary>
    public static async Task<TunnelMessage?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return JsonSerializer.Deserialize<TunnelMessage>(
            Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), Json);
    }
}
