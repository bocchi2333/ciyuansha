using System;
using System.Linq;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Base action that standardizes card-use stages before executing the concrete effect.
/// </summary>
public abstract class CardUseAction : GameAction
{
    protected readonly ActionManager ActionManager;

    protected CardUseAction(ActionManager actionManager, CardUseContext useContext)
    {
        ActionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        UseContext = useContext ?? throw new ArgumentNullException(nameof(useContext));
    }

    public CardUseContext UseContext { get; }

    public event EventHandler<CardUseStageEventArgs>? OnCardUseStage;

    protected virtual bool RequiresTarget => true;

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        ExecuteStage(CardUseStage.SelectingTargets);
        if (ShouldAbortBeforeResolve())
        {
            return;
        }

        ExecuteStage(CardUseStage.UsingCard);
        if (ShouldAbortBeforeResolve())
        {
            return;
        }

        foreach (var target in UseContext.Targets.ToList())
        {
            ExecuteStage(CardUseStage.TargetSpecified, target);
            if (ShouldAbortBeforeResolve())
            {
                return;
            }

            ExecuteStage(CardUseStage.BecameTarget, target);
            if (ShouldAbortBeforeResolve())
            {
                return;
            }
        }

        ExecuteStage(CardUseStage.TargetsConfirmed);
        if (ShouldAbortBeforeResolve())
        {
            return;
        }

        ExecuteStage(CardUseStage.ResolveCardEffect);
        if (ShouldAbortBeforeResolve(finishUseFlow: true))
        {
            return;
        }

        ResolveEffect();
        ExecuteStage(CardUseStage.ResolveFinished);
        MarkDone();
    }

    protected abstract void ResolveEffect();

    protected virtual void OnUseCancelled()
    {
    }

    private void ExecuteStage(CardUseStage stage, Godot.Node? target = null)
    {
        CardUseStageEventArgs args = new(UseContext, stage, target);
        OnCardUseStage?.Invoke(this, args);
        GameManager.Instance?.NotifyCardUseStage(UseContext, stage, target);
    }

    private bool ShouldAbortBeforeResolve(bool finishUseFlow = false)
    {
        if (!UseContext.IsCancelled && (!RequiresTarget || UseContext.HasTargets))
        {
            return false;
        }

        if (!UseContext.IsCancelled && RequiresTarget && !UseContext.HasTargets)
        {
            UseContext.Cancel("The card no longer has legal targets.");
        }

        OnUseCancelled();

        if (finishUseFlow)
        {
            ExecuteStage(CardUseStage.ResolveFinished);
        }

        MarkDone();
        return true;
    }
}
