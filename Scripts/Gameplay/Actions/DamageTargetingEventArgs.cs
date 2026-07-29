using System;
using CiyuanSha.Gameplay.Battle;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// 伤害指定目标时抛出的事件参数。
/// </summary>
public sealed class DamageTargetingEventArgs : EventArgs
{
    private readonly ActionManager _actionManager;

    public DamageAction DamageAction { get; }

    public DamageInfo DamageInfo { get; private set; }

    public bool IsCancelled { get; private set; }

    public bool WasDodged { get; private set; }

    internal bool WaitForInterruptResolution { get; private set; }

    public DamageTargetingEventArgs(DamageAction damageAction, ActionManager actionManager, DamageInfo damageInfo)
    {
        DamageAction = damageAction ?? throw new ArgumentNullException(nameof(damageAction));
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        DamageInfo = damageInfo;
    }

    /// <summary>
    /// 直接修改本次伤害值。
    /// </summary>
    public void SetDamageValue(int value)
    {
        DamageInfo = new DamageInfo(DamageInfo.Source, DamageInfo.Target, value, DamageInfo.Type, DamageInfo.AllowsDodgeResponse, DamageInfo.IsChainTransfer, DamageInfo.CauseCardType);

        if (value <= 0)
        {
            IsCancelled = true;
        }
    }

    /// <summary>
    /// 取消本次伤害。
    /// </summary>
    public void CancelDamage()
    {
        SetDamageValue(0);
    }

    /// <summary>
    /// 将一个高优先级动作插入队列顶部，并等待其先结算。
    /// </summary>
    public void QueueInterrupt(GameAction interruptAction)
    {
        if (interruptAction is null)
        {
            throw new ArgumentNullException(nameof(interruptAction));
        }

        WaitForInterruptResolution = true;
        _actionManager.AddToTop(interruptAction);
    }

    /// <summary>
    /// 以打出【闪】的方式响应本次伤害。
    /// </summary>
    public void RespondWithDodge(GameAction dodgeAction)
    {
        QueueInterrupt(dodgeAction);
        WasDodged = true;
        CancelDamage();
    }
}
