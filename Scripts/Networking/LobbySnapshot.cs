using System.Collections.Generic;

namespace CiyuanSha.Networking;

/// <summary>
/// 用于在大厅内同步完整玩家列表。
/// </summary>
public class LobbySnapshot
{
    public int HostPeerId { get; set; } = 1;

    public List<LanPlayerInfo> Players { get; set; } = new();
}
