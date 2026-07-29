using System;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Coarse timing buckets used by skills to subscribe to rule events.
/// </summary>
[Flags]
public enum SkillTriggerTiming
{
    None = 0,
    Match = 1 << 0,
    TurnStarted = 1 << 1,
    TurnEnded = 1 << 2,
    PhaseEntered = 1 << 3,
    PhaseSkipped = 1 << 4,
    CardUseStage = 1 << 5,
    CardUsed = 1 << 6,
    Response = 1 << 7,
    CardsChanged = 1 << 8,
    DamageTargeting = 1 << 9,
    DamageResolving = 1 << 10,
    DamageApplied = 1 << 11,
    Dying = 1 << 12,
    CharacterDefeated = 1 << 13,
    Equipment = 1 << 14,
    Healed = 1 << 15,
    Judgement = 1 << 16,
    SkillTriggered = 1 << 17,
    Any = Match
        | TurnStarted
        | TurnEnded
        | PhaseEntered
        | PhaseSkipped
        | CardUseStage
        | CardUsed
        | Response
        | CardsChanged
        | DamageTargeting
        | DamageResolving
        | DamageApplied
        | Dying
        | CharacterDefeated
        | Equipment
        | Healed
        | Judgement
        | SkillTriggered
}
