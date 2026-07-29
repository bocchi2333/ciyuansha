using System.Linq;
using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Active skill sample with both a card cost and a target: discard one hand card to deal 1 physical damage.
/// </summary>
public sealed class DiscardStrikeSkill : CharacterSkill
{
    public DiscardStrikeSkill()
        : base("discard_strike", "Stored Momentum", "Once per turn during PlayPhase, discard 1 hand card to deal 1 damage to another character.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override bool IsActiveSkill => true;

    public override SkillTargetingMode TargetingMode => SkillTargetingMode.OtherCharacter;

    public override bool RequiresHandCardCost => true;

    public override bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = string.Empty;
        if (Owner is null || context.Source != Owner)
        {
            reason = "Skill owner mismatch.";
            return false;
        }

        if (gameManager.ActionManager is null)
        {
            reason = "ActionManager is not ready.";
            return false;
        }

        if (gameManager.CurrentPhase != TurnPhase.PlayPhase || !gameManager.IsAwaitingPlayerInput)
        {
            reason = $"{DisplayName} can only be activated during your PlayPhase input window.";
            return false;
        }

        if (!CanActivate())
        {
            reason = $"{DisplayName} has already been used this turn.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.CardInstanceId) || Owner.FindHandCard(context.CardInstanceId) is null)
        {
            reason = $"{DisplayName} requires one hand card as cost.";
            return false;
        }

        PlayerCharacter? target = context.Targets.FirstOrDefault();
        if (target is null || target == Owner || !target.IsAlive)
        {
            reason = $"{DisplayName} requires one other alive target.";
            return false;
        }

        return true;
    }

    public override bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        if (Owner is null || gameManager.ActionManager is null)
        {
            return false;
        }

        PlayerCharacter? target = context.Targets.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        if (!TryDiscardActivationCard(gameManager, context, out CardInstance? discardedCard) || discardedCard is null)
        {
            return false;
        }

        if (!TryConsumeUse(gameManager))
        {
            return false;
        }

        DamageInfo damageInfo = new(Owner, target, 1, DamageType.Physical, allowsDodgeResponse: false);
        gameManager.ActionManager.AddToBottom(new DamageAction(gameManager.ActionManager, damageInfo));
        gameManager.AddBattleLog($"{Owner.CharacterName} activates {DisplayName}, discarding {discardedCard.DisplayName} to strike {target.CharacterName}.");
        return true;
    }
}
