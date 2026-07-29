using System.Collections.Generic;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Networking;

namespace CiyuanSha.Networking;

/// <summary>
/// 客户端提交给服务端的出牌请求。
/// </summary>
public class NetworkPlayCommand
{
    public int ProtocolVersion { get; set; } = ProtocolV2.Version;

    public string EngineApiVersion { get; set; } = ProtocolV2.EngineApiVersion;

    public long StateRevision { get; set; }

    public string RequestId { get; set; } = string.Empty;

    public string ReconnectToken { get; set; } = string.Empty;

    public ChoiceResult? ChoiceResult { get; set; }

    public NetworkPlayCommandType CommandType { get; set; }

    public int TargetPeerId { get; set; }

    public List<int> TargetPeerIds { get; set; } = new();

    public int DamageValue { get; set; } = 1;

    public string DamageType { get; set; } = "Physical";

    public string CardInstanceId { get; set; } = string.Empty;

    public string SkillId { get; set; } = string.Empty;

    public string GeneralId { get; set; } = string.Empty;

    public bool IsReady { get; set; }
}
