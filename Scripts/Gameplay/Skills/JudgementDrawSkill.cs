using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Once per turn, draw a card after your judgement is performed.
/// </summary>
public sealed class JudgementDrawSkill : CharacterSkill
{
    public JudgementDrawSkill()
        : base("judgement_draw", "Star Reading", "Once per turn after your judgement, draw 1 card.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.Judgement;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.Target != Owner
            || Owner.IsDefeated
            || !TryConsumeUse(gameManager))
        {
            return;
        }

        gameManager.DrawCardsForEffect(
            Owner,
            1,
            $"{Owner.CharacterName} triggers {DisplayName} and draws {{count}} card(s).",
            $"{Owner.CharacterName} triggers {DisplayName} after judgement.");
    }
}
