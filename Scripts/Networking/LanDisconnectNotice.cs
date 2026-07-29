namespace CiyuanSha.Networking;

public enum LanDisconnectReason
{
    ConnectionFailed,
    HostDisconnected
}

/// <summary>
/// Structured, player-facing context for an unexpected LAN interruption.
/// </summary>
public sealed record LanDisconnectNotice(
    LanDisconnectReason Reason,
    string Message,
    string Address,
    int Port,
    bool WasInMatch,
    bool CanReconnect);
