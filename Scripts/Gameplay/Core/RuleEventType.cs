namespace CiyuanSha.Gameplay.Core;

/// <summary>
/// High-level rule events emitted by the authoritative game flow.
/// </summary>
public enum RuleEventType
{
    None = 0,
    MatchStarted = 1,
    MatchEnded = 2,
    PhaseEntered = 3,
    TurnStarted = 4,
    CardUseStage = 5,
    ResponseWindowOpened = 6,
    ResponseWindowClosed = 7,
    ResponseUsed = 8,
    ResponseDeclined = 9,
    CardsDrawn = 10,
    CardsDiscarded = 11,
    DamageApplied = 12,
    DyingEntered = 13,
    DyingResolved = 14,
    CharacterDefeated = 15,
    EquipmentEquipped = 16,
    Healed = 17,
    EquipmentLost = 18,
    CardUsed = 19,
    PhaseSkipped = 20,
    JudgementPerformed = 21,
    TurnEnded = 22,
    CardRecast = 23,
    DamageResolving = 24,
    CardsLost = 25,
    CardsGained = 26,
    CardMoved = 27,
    SkillTriggered = 28,
    DamageTargeting = 29
}
