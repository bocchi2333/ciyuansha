using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// A simple passive skill that spends a Dodge card automatically.
/// </summary>
public sealed class AutoDodgeSkill : CharacterSkill
{
    public AutoDodgeSkill()
        : base("auto_dodge", "Flowing Dodge", "Automatically spends a Dodge card to answer one attack.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override SkillTriggerPriority TriggerPriority => SkillTriggerPriority.High;

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.DamageTargeting;

    public override bool TryRespondToDamageTargeting(DamageTargetingEventArgs args)
    {
        if (Owner is null || args.DamageInfo.Target != Owner)
        {
            return false;
        }

        if (Owner.FindFirstHandCardOfType(CardType.Dodge) is null || !CanActivate())
        {
            return false;
        }

        string ownerName = string.IsNullOrWhiteSpace(Owner.CharacterName) ? Owner.Name : Owner.CharacterName;
        if (!TryConsumeUse(GameManager.Instance, $"{ownerName} triggers {DisplayName}."))
        {
            return false;
        }

        if (!Owner.TryConsumeFirstHandCardOfType(CardType.Dodge, out _))
        {
            return false;
        }

        GameManager.Instance?.AddBattleLog($"{ownerName} triggers {DisplayName}.");
        args.RespondWithDodge(new DodgeAction(Owner));
        GameManager.Instance?.EmitRuleEvent(RuleEventType.ResponseUsed, Owner, Owner, value: 1, message: $"{ownerName} triggers {DisplayName}.", responseKind: ResponseWindowKind.Dodge, actingPeerId: Owner.OwnerPeerId);
        return true;
    }
}
