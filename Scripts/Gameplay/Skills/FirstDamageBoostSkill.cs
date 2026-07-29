using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Once per turn, increase the first damage you deal by 1.
/// </summary>
public sealed class FirstDamageBoostSkill : CharacterSkill
{
    public FirstDamageBoostSkill()
        : base("first_damage_boost", "Overdrive", "Once per turn when your damage resolves, increase it by 1.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override SkillTriggerPriority TriggerPriority => SkillTriggerPriority.High;

    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.DamageResolving;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.DamageInfo is not DamageInfo damageInfo
            || damageInfo.Source != Owner
            || damageInfo.Target is not PlayerCharacter target
            || target == Owner
            || damageInfo.Value <= 0
            || Owner.IsDefeated
            || !TryConsumeUse(gameManager))
        {
            return;
        }

        damageInfo.Value += 1;
        context.DamageInfo = damageInfo;
        gameManager.AddBattleLog($"{Owner.CharacterName} triggers {DisplayName}: damage +1.");
    }
}
