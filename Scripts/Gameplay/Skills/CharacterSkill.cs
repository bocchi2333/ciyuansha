using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Base class for character skills.
/// </summary>
public abstract class CharacterSkill
{
    public string SkillId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public PlayerCharacter? Owner { get; private set; }

    public int UsesThisTurn { get; private set; }

    public virtual SkillUsageScope UsageScope => SkillUsageScope.Unlimited;

    public virtual SkillTriggerPriority TriggerPriority => SkillTriggerPriority.Normal;

    public virtual SkillTriggerTiming TriggerTimings => SkillTriggerTiming.None;

    public virtual bool IsActiveSkill => false;

    public virtual SkillTargetingMode TargetingMode => SkillTargetingMode.None;

    public virtual int MinTargetCount => TargetingMode == SkillTargetingMode.None ? 0 : 1;

    public virtual int MaxTargetCount => TargetingMode == SkillTargetingMode.None ? 0 : 1;

    public virtual bool RequiresHandCardCost => false;

    protected CharacterSkill(string skillId, string displayName, string description)
    {
        SkillId = skillId;
        DisplayName = displayName;
        Description = description;
    }

    public void Attach(PlayerCharacter owner)
    {
        Owner = owner;
        OnAttached();
    }

    protected virtual void OnAttached()
    {
    }

    public bool CanActivate()
    {
        if (Owner is null || Owner.IsDefeated)
        {
            return false;
        }

        return UsageScope switch
        {
            SkillUsageScope.OncePerTurn => UsesThisTurn <= 0,
            _ => true
        };
    }

    public virtual bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = "This skill cannot be activated manually.";
        return false;
    }

    public virtual bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        return false;
    }

    public void ResetTurnUsage()
    {
        UsesThisTurn = 0;
    }

    public void SyncUsesThisTurn(int usesThisTurn)
    {
        UsesThisTurn = System.Math.Max(0, usesThisTurn);
    }

    public void HandleRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (context.EventType == RuleEventType.TurnStarted)
        {
            ResetTurnUsage();
        }

        if (!CanObserveRuleEvent(context))
        {
            return;
        }

        OnRuleEvent(gameManager, context);
    }

    public virtual bool CanObserveRuleEvent(RuleEventContext context)
    {
        SkillTriggerTiming observedTimings = TriggerTimings;
        if (observedTimings == SkillTriggerTiming.None)
        {
            return false;
        }

        SkillTriggerTiming eventTiming = SkillTimingMapper.FromRuleEvent(context.EventType);
        return eventTiming != SkillTriggerTiming.None && (observedTimings & eventTiming) != 0;
    }

    protected bool TryConsumeUse(GameManager? gameManager = null, string message = "")
    {
        if (!CanActivate())
        {
            return false;
        }

        if (UsageScope != SkillUsageScope.Unlimited)
        {
            UsesThisTurn++;
        }

        if (gameManager is not null && Owner is not null)
        {
            gameManager.EmitRuleEvent(new RuleEventContext
            {
                EventType = RuleEventType.SkillTriggered,
                Source = Owner,
                Target = Owner,
                SkillId = SkillId,
                SkillName = DisplayName,
                Value = UsesThisTurn,
                Message = string.IsNullOrWhiteSpace(message) ? $"{Owner.CharacterName} triggers {DisplayName}." : message,
                Phase = gameManager.CurrentPhase,
                ActingPeerId = Owner.OwnerPeerId
            });
        }

        return true;
    }

    protected bool TryDiscardActivationCard(GameManager gameManager, SkillActivationContext context, out CardInstance? discardedCard)
    {
        discardedCard = null;
        if (Owner is null || gameManager is null || context is null || string.IsNullOrWhiteSpace(context.CardInstanceId))
        {
            return false;
        }

        if (!Owner.TryConsumeHandCardByInstanceId(context.CardInstanceId, out discardedCard) || discardedCard is null)
        {
            return false;
        }

        gameManager.EmitRuleEvent(
            RuleEventType.CardsDiscarded,
            Owner,
            Owner,
            discardedCard,
            value: 1,
            message: $"{Owner.CharacterName} discards {discardedCard.DisplayName} for {DisplayName}.");
        return true;
    }

    public virtual void OnTurnStart(GameManager gameManager)
    {
    }

    public virtual bool TryRespondToDamageTargeting(DamageTargetingEventArgs args)
    {
        return false;
    }

    public virtual void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
    }
}
