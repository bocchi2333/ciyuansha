namespace CiyuanSha.Networking;

/// <summary>
/// Replicated battle log entry shared between host and clients.
/// </summary>
public class NetworkBattleLogEntry
{
    public int Sequence { get; set; }

    public string Message { get; set; } = string.Empty;
}
