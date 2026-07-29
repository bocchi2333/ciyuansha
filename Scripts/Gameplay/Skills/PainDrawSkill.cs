using System;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Draws one card after the owner takes damage.
/// </summary>
public sealed class PainDrawSkill : CharacterSkill
{
    public PainDrawSkill()
        : base("pain_draw", "Ripple Recall", "After you take damage, draw 1 card.")
    {
    }

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.DamageApplied;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.EventType != RuleEventType.DamageApplied
            || context.Target != Owner
            || context.Value <= 0
            || Owner.IsDefeated)
        {
            return;
        }

        string ownerName = string.IsNullOrWhiteSpace(Owner.CharacterName) ? Owner.Name : Owner.CharacterName;
        gameManager.DrawCardsForEffect(
            Owner,
            1,
            $"{ownerName} triggers {DisplayName} and draws {{count}} card(s) after taking damage.",
            $"{ownerName} triggers {DisplayName} after taking damage.");
    }
}
