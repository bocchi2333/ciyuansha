using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Gameplay.Characters;

/// <summary>
/// 最小玩家响应示例。
/// 当被指定为伤害目标时，可自动打出【闪】。
/// </summary>
public partial class PlayerResponder : PlayerCharacter
{
    [Export]
    public bool AutoDodgeEnabled { get; set; } = true;

    [Export]
    public bool ConsumeAutoDodgeAfterUse { get; set; } = true;

    /// <summary>
    /// 将本响应器挂接到一条 DamageAction 上。
    /// </summary>
    public void AttachToDamageAction(DamageAction damageAction)
    {
        if (damageAction is null)
        {
            GD.PushWarning("Cannot attach PlayerResponder to a null DamageAction.");
            return;
        }

        damageAction.OnDamageTargeting += HandleDamageTargeting;
    }

    private void HandleDamageTargeting(object? sender, DamageTargetingEventArgs e)
    {
        if (e.DamageInfo.Target != this)
        {
            return;
        }

        if (TryRespondToDamageTargeting(e))
        {
            return;
        }

        if (!AutoDodgeEnabled)
        {
            return;
        }

        if (!TryConsumeFirstHandCardOfType(CardType.Dodge, out CardInstance? dodgeCard))
        {
            return;
        }

        DodgeAction dodgeAction = new(this);
        e.RespondWithDodge(dodgeAction);
        GameManager.Instance?.EmitRuleEvent(RuleEventType.ResponseUsed, this, this, value: 1, message: $"{CharacterName} automatically responds with Dodge.", responseKind: ResponseWindowKind.Dodge, actingPeerId: OwnerPeerId);

        if (ConsumeAutoDodgeAfterUse && dodgeCard is not null)
        {
            AutoDodgeEnabled = false;
        }
    }
}
