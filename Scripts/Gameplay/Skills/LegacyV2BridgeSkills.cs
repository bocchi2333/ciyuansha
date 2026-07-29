using System.Linq;
using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Godot-facing compatibility implementations for the five new V2 skill
/// families. Canonical metadata lives in Data/Packs/ciyuansha-generals.
/// </summary>
public sealed class BladeConversionSkill : CharacterSkill
{
    public BladeConversionSkill()
        : base("blade_conversion", "Refraction", "Once per turn, use a non-basic hand card as Slash.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;
    public override bool IsActiveSkill => true;
    public override SkillTargetingMode TargetingMode => SkillTargetingMode.OtherCharacter;
    public override bool RequiresHandCardCost => true;

    public override bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = string.Empty;
        PlayerCharacter? target = context.Targets.FirstOrDefault();
        CardInstance? cost = Owner?.FindHandCard(context.CardInstanceId);
        if (Owner is null || context.Source != Owner || gameManager.ActionManager is null)
        {
            reason = "Skill owner or action manager is unavailable.";
            return false;
        }
        if (gameManager.CurrentPhase != TurnPhase.PlayPhase || !gameManager.IsAwaitingPlayerInput || !CanActivate())
        {
            reason = "Refraction can only be used once during your PlayPhase.";
            return false;
        }
        if (cost is null || IsBasic(cost.CardType))
        {
            reason = "Refraction requires one non-basic hand card.";
            return false;
        }
        if (target is null || target == Owner || !gameManager.CanUseSlashOnTarget(Owner, target, out reason))
        {
            return false;
        }
        return true;
    }

    public override bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        if (Owner is null || gameManager.ActionManager is null || context.Targets.FirstOrDefault() is not { } target)
        {
            return false;
        }
        if (!Owner.TryConsumeHandCardByInstanceId(context.CardInstanceId, out CardInstance? sourceCard) || sourceCard is null || !TryConsumeUse(gameManager))
        {
            return false;
        }

        gameManager.DiscardCardFromEffect(sourceCard);
        gameManager.EmitCardsLost(Owner, Owner, sourceCard, 1, $"{Owner.CharacterName} converts {sourceCard.DisplayName} with {DisplayName}.");
        CardInstance virtualSlash = new()
        {
            InstanceId = $"virtual-refraction-{sourceCard.InstanceId}",
            CardType = CardType.Slash,
            DisplayName = $"{DisplayName} Slash",
            Description = "Virtual Slash created by Refraction.",
            Suit = sourceCard.Suit,
            Rank = sourceCard.Rank,
            DamageValue = 1
        };
        gameManager.ActionManager.AddToBottom(new SlashUseAction(gameManager.ActionManager, Owner, target, virtualSlash, DamageType.Physical));
        return true;
    }

    private static bool IsBasic(CardType type) => type is CardType.Slash or CardType.FireSlash or CardType.ThunderSlash or CardType.Dodge or CardType.Peach or CardType.Wine;
}

public sealed class MirrorJudgementSkill : CharacterSkill
{
    public MirrorJudgementSkill()
        : base("mirror_judgement", "Reverse Reflection", "Once per turn after a judgement, draw 1 card as the compatibility form of judgement replacement.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;
    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.Judgement;
    public override SkillTriggerPriority TriggerPriority => SkillTriggerPriority.High;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null || context.EventType != RuleEventType.JudgementPerformed || !TryConsumeUse(gameManager))
        {
            return;
        }
        gameManager.DrawCardsForEffect(Owner, 1, $"{Owner.CharacterName} triggers {DisplayName} and draws {{count}} card(s).");
    }
}

public sealed class ElementalResonanceSkill : CharacterSkill
{
    public ElementalResonanceSkill()
        : base("elemental_resonance", "Chain Resonance", "Once per turn after elemental damage involving you, toggle the damaged character's chained state.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;
    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.DamageApplied;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (Owner is null
            || context.DamageInfo is not DamageInfo damage
            || damage.Type == DamageType.Physical
            || context.Target is not PlayerCharacter target
            || (context.Source != Owner && context.Target != Owner)
            || !target.IsAlive
            || !TryConsumeUse(gameManager))
        {
            return;
        }
        target.ToggleChained();
        gameManager.AddBattleLog($"{Owner.CharacterName} triggers {DisplayName}: {target.CharacterName} is {(target.IsChained ? "chained" : "unchained")}.");
    }
}

public sealed class AllySupplySkill : CharacterSkill
{
    public AllySupplySkill()
        : base("ally_supply", "Resonant Supply", "Once per turn, give another character a hand card, then both draw 1 card.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;
    public override bool IsActiveSkill => true;
    public override SkillTargetingMode TargetingMode => SkillTargetingMode.OtherCharacter;
    public override bool RequiresHandCardCost => true;

    public override bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = string.Empty;
        PlayerCharacter? target = context.Targets.FirstOrDefault();
        if (Owner is null || context.Source != Owner || gameManager.CurrentPhase != TurnPhase.PlayPhase || !gameManager.IsAwaitingPlayerInput || !CanActivate())
        {
            reason = "Resonant Supply can only be used once during your PlayPhase.";
            return false;
        }
        if (Owner.FindHandCard(context.CardInstanceId) is null)
        {
            reason = "Resonant Supply requires one hand card.";
            return false;
        }
        if (target is null || target == Owner || !target.IsAlive)
        {
            reason = "Resonant Supply requires one other alive character.";
            return false;
        }
        return true;
    }

    public override bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        if (Owner is null || context.Targets.FirstOrDefault() is not { } target || !TryConsumeUse(gameManager))
        {
            return false;
        }
        if (!Owner.TakeHandCardByInstanceId(context.CardInstanceId, out CardInstance? movedCard) || movedCard is null)
        {
            return false;
        }
        target.AddCard(movedCard);
        gameManager.EmitCardMoved(Owner, Owner, target, movedCard, $"{Owner.CharacterName} gives {movedCard.DisplayName} to {target.CharacterName} with {DisplayName}.");
        gameManager.DrawCardsForEffect(Owner, 1, $"{Owner.CharacterName} draws {{count}} card(s) for {DisplayName}.");
        gameManager.DrawCardsForEffect(target, 1, $"{target.CharacterName} draws {{count}} card(s) for {DisplayName}.");
        return true;
    }
}

public sealed class LimitBreakSkill : CharacterSkill
{
    private bool _usedThisMatch;

    public LimitBreakSkill()
        : base("limit_break", "Boundary Break", "Limited: draw 2 cards and your next Slash damage this turn +2.")
    {
    }

    public override bool IsActiveSkill => true;
    public override SkillTargetingMode TargetingMode => SkillTargetingMode.Self;
    public override SkillTriggerTiming TriggerTimings => SkillTriggerTiming.Match;

    public override void OnRuleEvent(GameManager gameManager, RuleEventContext context)
    {
        if (context.EventType == RuleEventType.MatchStarted)
        {
            _usedThisMatch = false;
        }
    }

    public override bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = string.Empty;
        if (Owner is null || context.Source != Owner || _usedThisMatch)
        {
            reason = "Boundary Break has already been used or its owner is unavailable.";
            return false;
        }
        if (gameManager.CurrentPhase != TurnPhase.PlayPhase || !gameManager.IsAwaitingPlayerInput)
        {
            reason = "Boundary Break can only be used during your PlayPhase.";
            return false;
        }
        return true;
    }

    public override bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        if (Owner is null || _usedThisMatch)
        {
            return false;
        }
        _usedThisMatch = true;
        Owner.AddPendingSlashDamageBonus(2);
        gameManager.DrawCardsForEffect(Owner, 2, $"{Owner.CharacterName} activates {DisplayName} and draws {{count}} card(s).");
        gameManager.EmitRuleEvent(new RuleEventContext
        {
            EventType = RuleEventType.SkillTriggered,
            Source = Owner,
            Target = Owner,
            SkillId = SkillId,
            SkillName = DisplayName,
            Message = $"{Owner.CharacterName} activates limited skill {DisplayName}.",
            Phase = gameManager.CurrentPhase,
            ActingPeerId = Owner.OwnerPeerId,
        });
        return true;
    }
}
