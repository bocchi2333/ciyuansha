using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Cards;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Mutable context for a card-use event chain.
/// </summary>
public sealed class CardUseContext
{
    public Node? Source { get; }

    public CardInstance Card { get; }

    public List<Node?> Targets { get; }

    public bool IsCancelled { get; private set; }

    public string CancelReason { get; private set; } = string.Empty;

    public CardUseContext(Node? source, CardInstance card, IEnumerable<Node?>? targets)
    {
        Source = source;
        Card = card?.Clone() ?? new CardInstance();
        Targets = targets?.ToList() ?? new List<Node?>();
    }

    public bool HasTargets => Targets.Count > 0;

    public void Cancel(string reason = "")
    {
        IsCancelled = true;
        CancelReason = reason ?? string.Empty;
    }

    public void RemoveTarget(Node? target)
    {
        Targets.Remove(target);
    }

    public void ClearTargets()
    {
        Targets.Clear();
    }
}
