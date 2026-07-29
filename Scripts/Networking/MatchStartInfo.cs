using System.Collections.Generic;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Networking;

namespace CiyuanSha.Networking;

/// <summary>
/// 开局时同步给所有客户端的基础对局信息。
/// </summary>
public class MatchStartInfo
{
    public int ProtocolVersion { get; set; } = ProtocolV2.Version;

    public string EngineApiVersion { get; set; } = ProtocolV2.EngineApiVersion;

    public string MatchId { get; set; } = string.Empty;

    public string ModeId { get; set; } = "duel";

    public ulong Seed { get; set; }

    public int StartingPeerId { get; set; } = 1;

    public List<int> TurnOrder { get; set; } = new();

    public List<ContentPackReference> ContentPacks { get; set; } = new();
}
