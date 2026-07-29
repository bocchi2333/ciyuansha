using CiyuanSha.Gameplay.Cards;
using Godot;

namespace CiyuanSha.Gameplay.Battle;

/// <summary>
/// 用于描述一次伤害结算所需的数据。
/// </summary>
public struct DamageInfo
{
    public Node? Source { get; set; }

    public Node? Target { get; set; }

    public int Value { get; set; }

    public DamageType Type { get; set; }

    public bool AllowsDodgeResponse { get; set; }

    public bool IsChainTransfer { get; set; }

    public CardType CauseCardType { get; set; }

    public DamageInfo(Node? source, Node? target, int value, DamageType type, bool allowsDodgeResponse = true, bool isChainTransfer = false, CardType causeCardType = CardType.None)
    {
        Source = source;
        Target = target;
        Value = value;
        Type = type;
        AllowsDodgeResponse = allowsDodgeResponse;
        IsChainTransfer = isChainTransfer;
        CauseCardType = causeCardType;
    }
}
