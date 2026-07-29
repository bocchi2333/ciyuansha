using System.Collections.Generic;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Networking;

namespace CiyuanSha.Networking;

/// <summary>
/// 用于在大厅内同步完整玩家列表。
/// </summary>
public class LobbySnapshot
{
    public int ProtocolVersion { get; set; } = ProtocolV2.Version;

    public string EngineApiVersion { get; set; } = ProtocolV2.EngineApiVersion;

    public int HostPeerId { get; set; } = 1;

    public string ModeId { get; set; } = "duel";

    public List<ContentPackReference> ContentPacks { get; set; } = new();

    public List<LanPlayerInfo> Players { get; set; } = new();
}
