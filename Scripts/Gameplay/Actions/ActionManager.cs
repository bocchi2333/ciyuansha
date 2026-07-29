using System.Collections.Generic;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// 负责驱动和调度游戏动作队列。
/// </summary>
public partial class ActionManager : Node
{
    private readonly LinkedList<GameAction> _actionQueue = new();

    public int ActionCount => _actionQueue.Count;

    public bool HasPendingActions => _actionQueue.Count > 0;

    /// <summary>
    /// 将动作加入队列底部，按常规顺序执行。
    /// </summary>
    public void AddToBottom(GameAction action)
    {
        if (action is null)
        {
            GD.PushWarning("Tried to add a null GameAction to the bottom of the queue.");
            return;
        }

        _actionQueue.AddLast(action);
    }

    /// <summary>
    /// 将动作插入队列顶部，用于高优先级打断。
    /// </summary>
    public void AddToTop(GameAction action)
    {
        if (action is null)
        {
            GD.PushWarning("Tried to add a null GameAction to the top of the queue.");
            return;
        }

        _actionQueue.AddFirst(action);
    }

    public override void _Process(double delta)
    {
        if (CiyuanSha.Gameplay.Core.GameManager.Instance is { } gameManager
            && (gameManager.HasPendingResponseWindow
                || gameManager.HasPendingHarvestSelection
                || gameManager.HasPendingTargetCardSelection
                || gameManager.HasPendingHandCardSelection))
        {
            return;
        }

        LinkedListNode<GameAction>? currentNode = _actionQueue.First;
        if (currentNode is null)
        {
            return;
        }

        GameAction currentAction = currentNode.Value;
        currentAction.Update();

        if (currentAction.IsDone)
        {
            _actionQueue.Remove(currentNode);
        }
    }
}
