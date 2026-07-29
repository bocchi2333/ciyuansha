using System;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Grants one extra draw at turn start.
/// </summary>
public sealed class BonusDrawSkill : CharacterSkill
{
    public BonusDrawSkill()
        : base("bonus_draw", "Tactical Insight", "Draw 1 extra card at the start of your turn.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.TurnStarted;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.EventType != RuleEventType.TurnStarted
            || context.ActingPeerId != Owner.OwnerPeerId
            || !TryConsumeUse(gameManager))
        {
            return;
        }

        string ownerName = string.IsNullOrWhiteSpace(Owner.CharacterName) ? Owner.Name : Owner.CharacterName;
        int drawnCount = gameManager.DrawCardsForEffect(
            Owner,
            1,
            $"{ownerName} triggers {DisplayName} and draws {{count}} extra card(s).",
            $"{ownerName} triggers {DisplayName}.");

        if (drawnCount <= 0)
        {
            return;
        }
    }
}
