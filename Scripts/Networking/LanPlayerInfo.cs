namespace CiyuanSha.Networking;

/// <summary>
/// 联机大厅中的玩家信息。
/// </summary>
public class LanPlayerInfo
{
    public int PeerId { get; set; }

    public int TransportPeerId { get; set; }

    public string PlayerName { get; set; } = "Player";

    public string CharacterId { get; set; } = string.Empty;

    public bool IsReady { get; set; }

    public bool IsHost { get; set; }

    public bool IsConnected { get; set; } = true;
}
