namespace Todo.Helper;

/// <summary>Where the tunnel stands, for the local API to show.</summary>
public enum TunnelState
{
    /// <summary>No host address or no key yet, so nothing to connect to.</summary>
    NotConfigured,
    Connecting,
    Connected,

    /// <summary>The host said no - another protocol version, typically. <see cref="TunnelClient.Refusal"/> says why.</summary>
    Refused,
    Disconnected,
}
