namespace CiyuanSha.Networking;

/// <summary>
/// 网络同步用的单张手牌数据。
/// </summary>
public class NetworkHandCardState
{
    public string InstanceId { get; set; } = string.Empty;

    public string CardType { get; set; } = "None";

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Suit { get; set; } = "None";

    public int Rank { get; set; }

    public int DamageValue { get; set; } = 1;

    public string EquipmentSlot { get; set; } = "None";

    public string EquipmentEffect { get; set; } = "None";

    public int AttackRangeModifier { get; set; }

    public int DamageReductionValue { get; set; }

    public int AttackDistanceModifier { get; set; }

    public int DefenseDistanceModifier { get; set; }

    public bool IsDelayedTrick { get; set; }
}
