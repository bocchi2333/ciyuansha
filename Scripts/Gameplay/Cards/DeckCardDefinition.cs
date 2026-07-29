namespace CiyuanSha.Gameplay.Cards;

/// <summary>
/// Immutable card identity used by fixed deck lists.
/// </summary>
public sealed record DeckCardDefinition(
    CardType CardType,
    CardSuit Suit,
    int Rank,
    string DisplayName = "",
    string Description = "",
    EquipmentSlotType EquipmentSlot = EquipmentSlotType.None,
    EquipmentEffectType EquipmentEffect = EquipmentEffectType.None,
    int AttackRangeModifier = 0,
    int DamageReductionValue = 0,
    int AttackDistanceModifier = 0,
    int DefenseDistanceModifier = 0,
    int DamageValue = -1,
    bool IsDelayedTrick = false);
