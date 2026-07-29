using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Once per turn, draw after losing cards outside your own turn.
/// </summary>
public sealed class OffTurnLossDrawSkill : CharacterSkill
{
    public OffTurnLossDrawSkill()
        : base("off_turn_loss_draw", "Resource Echo", "Once per turn after you lose cards outside your turn, draw 1 card.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.CardsChanged;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.EventType != RuleEventType.CardsLost
            || context.Target != Owner
            || context.Value <= 0
            || context.ActingPeerId == Owner.OwnerPeerId
            || Owner.IsDefeated
            || !TryConsumeUse(gameManager))
        {
            return;
        }

        gameManager.DrawCardsForEffect(
            Owner,
            1,
            $"{Owner.CharacterName} triggers {DisplayName} and draws {{count}} card(s).",
            $"{Owner.CharacterName} triggers {DisplayName} after losing cards.");
    }
}
