using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

public static class SkillTimingMapper
{
    public static SkillTriggerTiming FromRuleEvent(RuleEventType eventType)
    {
        return eventType switch
        {
            RuleEventType.MatchStarted or RuleEventType.MatchEnded => SkillTriggerTiming.Match,
            RuleEventType.TurnStarted => SkillTriggerTiming.TurnStarted,
            RuleEventType.TurnEnded => SkillTriggerTiming.TurnEnded,
            RuleEventType.PhaseEntered => SkillTriggerTiming.PhaseEntered,
            RuleEventType.PhaseSkipped => SkillTriggerTiming.PhaseSkipped,
            RuleEventType.CardUseStage => SkillTriggerTiming.CardUseStage,
            RuleEventType.CardUsed or RuleEventType.CardRecast => SkillTriggerTiming.CardUsed,
            RuleEventType.ResponseWindowOpened
                or RuleEventType.ResponseWindowClosed
                or RuleEventType.ResponseUsed
                or RuleEventType.ResponseDeclined => SkillTriggerTiming.Response,
            RuleEventType.CardsDrawn
                or RuleEventType.CardsDiscarded
                or RuleEventType.CardsLost
                or RuleEventType.CardsGained
                or RuleEventType.CardMoved => SkillTriggerTiming.CardsChanged,
            RuleEventType.DamageTargeting => SkillTriggerTiming.DamageTargeting,
            RuleEventType.DamageResolving => SkillTriggerTiming.DamageResolving,
            RuleEventType.DamageApplied => SkillTriggerTiming.DamageApplied,
            RuleEventType.DyingEntered or RuleEventType.DyingResolved => SkillTriggerTiming.Dying,
            RuleEventType.CharacterDefeated => SkillTriggerTiming.CharacterDefeated,
            RuleEventType.EquipmentEquipped or RuleEventType.EquipmentLost => SkillTriggerTiming.Equipment,
            RuleEventType.Healed => SkillTriggerTiming.Healed,
            RuleEventType.JudgementPerformed => SkillTriggerTiming.Judgement,
            RuleEventType.SkillTriggered => SkillTriggerTiming.SkillTriggered,
            _ => SkillTriggerTiming.None
        };
    }
}
