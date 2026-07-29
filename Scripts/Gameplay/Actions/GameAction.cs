namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// 所有游戏动作的基础抽象类。
/// </summary>
public abstract class GameAction
{
    /// <summary>
    /// 标记当前动作是否已经执行完成。
    /// </summary>
    public bool IsDone { get; protected set; }

    /// <summary>
    /// 逐帧推进动作的具体逻辑。
    /// </summary>
    public abstract void Update();

    /// <summary>
    /// 重置动作状态，便于复用。
    /// </summary>
    public virtual void Reset()
    {
        IsDone = false;
    }

    /// <summary>
    /// 供子类在动作完成时调用。
    /// </summary>
    protected void MarkDone()
    {
        IsDone = true;
    }
}
