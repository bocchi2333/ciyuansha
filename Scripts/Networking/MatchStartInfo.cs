using System.Collections.Generic;

namespace CiyuanSha.Networking;

/// <summary>
/// 开局时同步给所有客户端的基础对局信息。
/// </summary>
public class MatchStartInfo
{
    public int StartingPeerId { get; set; } = 1;

    public List<int> TurnOrder { get; set; } = new();
}
