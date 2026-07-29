using System;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Event payload for a specific card-use stage.
/// </summary>
public sealed class CardUseStageEventArgs : EventArgs
{
    public CardUseContext UseContext { get; }

    public CardUseStage Stage { get; }

    public Node? CurrentTarget { get; }

    public CardUseStageEventArgs(CardUseContext useContext, CardUseStage stage, Node? currentTarget = null)
    {
        UseContext = useContext;
        Stage = stage;
        CurrentTarget = currentTarget;
    }
}
