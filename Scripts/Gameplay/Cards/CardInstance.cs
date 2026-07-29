namespace CiyuanSha.Gameplay.Cards;

/// <summary>
/// A concrete card instance in a player's hand, draw pile, or discard pile.
/// </summary>
public class CardInstance
{
    public string InstanceId { get; set; } = string.Empty;

    public CardType CardType { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public CardSuit Suit { get; set; } = CardSuit.None;

    public int Rank { get; set; }

    public int DamageValue { get; set; } = 1;

    public EquipmentSlotType EquipmentSlot { get; set; } = EquipmentSlotType.None;

    public EquipmentEffectType EquipmentEffect { get; set; } = EquipmentEffectType.None;

    public int AttackRangeModifier { get; set; }

    public int DamageReductionValue { get; set; }

    public int AttackDistanceModifier { get; set; }

    public int DefenseDistanceModifier { get; set; }

    public bool IsDelayedTrick { get; set; }

    public CardInstance Clone()
    {
        return new CardInstance
        {
            InstanceId = InstanceId,
            CardType = CardType,
            DisplayName = DisplayName,
            Description = Description,
            Suit = Suit,
            Rank = Rank,
            DamageValue = DamageValue,
            EquipmentSlot = EquipmentSlot,
            EquipmentEffect = EquipmentEffect,
            AttackRangeModifier = AttackRangeModifier,
            DamageReductionValue = DamageReductionValue,
            AttackDistanceModifier = AttackDistanceModifier,
            DefenseDistanceModifier = DefenseDistanceModifier,
            IsDelayedTrick = IsDelayedTrick
        };
    }
}
