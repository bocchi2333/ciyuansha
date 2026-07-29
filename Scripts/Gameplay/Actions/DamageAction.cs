using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// 处理一次伤害结算，可在目标指定阶段被【闪】等效果打断。
/// </summary>
public class DamageAction : GameAction
{
    private readonly ActionManager _actionManager;
    private readonly DamageInfo _initialDamageInfo;
    private bool _hasRaisedTargetingEvent;
    private bool _waitingForInterruptResolution;
    private bool _waitingForDyingResolution;
    private DamageTargetingEventArgs? _pendingTargetingArgs;

    public DamageInfo DamageInfo { get; private set; }

    public bool WasCancelled { get; private set; }

    public bool WasDodged { get; private set; }

    public event EventHandler<DamageTargetingEventArgs>? OnDamageTargeting;

    public DamageAction(ActionManager actionManager, DamageInfo damageInfo)
    {
        _actionManager = actionManager ?? throw new ArgumentNullException(nameof(actionManager));
        _initialDamageInfo = damageInfo;
        DamageInfo = damageInfo;
    }

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        if (!_hasRaisedTargetingEvent)
        {
            RaiseTargetingEvent();

            if (_waitingForInterruptResolution)
            {
                return;
            }
        }

        if (_waitingForInterruptResolution)
        {
            if (GameManager.Instance?.HasPendingResponseForAction(this) == true)
            {
                return;
            }

            SyncFromPendingArgs();
            _waitingForInterruptResolution = false;
            TryForceDodgedDamageWithStoneAxe();
            TryFollowUpSlashWithGreenDragonBlade();
        }

        if (_waitingForDyingResolution)
        {
            if (DamageInfo.Target is PlayerCharacter dyingCharacter
                && GameManager.Instance?.HasPendingDyingResolution(dyingCharacter) == true)
            {
                return;
            }

            _waitingForDyingResolution = false;
            MarkDone();
            return;
        }

        if (WasCancelled || DamageInfo.Value <= 0)
        {
            WasCancelled = true;
            DamageInfo = new DamageInfo(DamageInfo.Source, DamageInfo.Target, 0, DamageInfo.Type, DamageInfo.AllowsDodgeResponse, DamageInfo.IsChainTransfer, DamageInfo.CauseCardType);
            MarkDone();
            return;
        }

        bool waitingForDyingResolution = ApplyDamage(DamageInfo);
        if (waitingForDyingResolution)
        {
            _waitingForDyingResolution = true;
            return;
        }

        MarkDone();
    }

    /// <summary>
    /// 实际执行伤害效果。后续接入玩家血量系统时可在此扩展。
    /// </summary>
    protected virtual bool ApplyDamage(DamageInfo damageInfo)
    {
        RuleEventContext resolvingContext = new()
        {
            EventType = RuleEventType.DamageResolving,
            Source = damageInfo.Source,
            Target = damageInfo.Target,
            DamageInfo = damageInfo,
            Value = damageInfo.Value,
            Message = "Damage is resolving.",
            ActingPeerId = GameManager.Instance?.CurrentTurnPeerId ?? 0,
            Phase = GameManager.Instance?.CurrentPhase ?? TurnPhase.TurnStart
        };
        GameManager.Instance?.EmitRuleEvent(resolvingContext);
        damageInfo = resolvingContext.DamageInfo ?? damageInfo;
        DamageInfo = damageInfo;

        if (TryReplaceSlashDamageWithIceSword(ref damageInfo))
        {
            DamageInfo = damageInfo;
            return false;
        }

        string sourceName = damageInfo.Source?.Name ?? "UnknownSource";
        string targetName = damageInfo.Target?.Name ?? "UnknownTarget";

        if (damageInfo.Value <= 0)
        {
            GameManager.Instance?.AddBattleLog($"{targetName}'s damage is prevented.");
            GameManager.Instance?.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo.Source, damageInfo.Target, damageInfo: new DamageInfo(damageInfo.Source, damageInfo.Target, 0, damageInfo.Type, damageInfo.AllowsDodgeResponse, damageInfo.IsChainTransfer, damageInfo.CauseCardType), value: 0, message: $"{targetName}'s damage is prevented.");
            return false;
        }

        if (damageInfo.Target is PlayerCharacter targetCharacter)
        {
            bool ignoresArmor = DamageIgnoresArmor(damageInfo);
            if (!ignoresArmor && TryPreventDamageWithVineArmor(damageInfo, targetCharacter))
            {
                return false;
            }

            if (TryApplyGudingBladeBonus(ref damageInfo, targetCharacter))
            {
                DamageInfo = damageInfo;
            }

            int preventedDamage = ignoresArmor ? 0 : targetCharacter.GetDamageReduction(damageInfo.Type);
            int finalDamage = Math.Max(0, damageInfo.Value - preventedDamage);
            if (!ignoresArmor && targetCharacter.HasVineArmor && damageInfo.Type == DamageType.Fire)
            {
                finalDamage += 1;
                GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName}'s Vine Armor increases fire damage by 1.");
            }

            if (!ignoresArmor && targetCharacter.HasSilverLion && finalDamage > 1)
            {
                finalDamage = 1;
                GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName}'s Silver Lion reduces damage to 1.");
            }

            if (preventedDamage > 0)
            {
                string armorName = targetCharacter.EquippedArmor?.DisplayName ?? "Armor";
                GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName}'s {armorName} reduces damage by {preventedDamage}.");
            }
            else if (ignoresArmor && targetCharacter.EquippedArmor is not null)
            {
                GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName}'s armor is ignored.");
            }

            GD.Print($"{sourceName} deals {finalDamage} {damageInfo.Type} damage to {targetName}.");
            targetCharacter.TakeDamage(finalDamage);
            GameManager.Instance?.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo.Source, targetCharacter, damageInfo: new DamageInfo(damageInfo.Source, targetCharacter, finalDamage, damageInfo.Type, damageInfo.AllowsDodgeResponse, damageInfo.IsChainTransfer, damageInfo.CauseCardType), value: finalDamage, message: $"{sourceName} deals {finalDamage} {damageInfo.Type} damage to {targetName}.");
            TryTriggerKylinBow(damageInfo, targetCharacter, finalDamage);
            TryPropagateChainDamage(damageInfo, targetCharacter, finalDamage);
            return GameManager.Instance?.ResolveDyingCharacter(targetCharacter, damageInfo.Source) == true;
        }

        GD.Print($"{sourceName} deals {damageInfo.Value} {damageInfo.Type} damage to {targetName}.");
        GameManager.Instance?.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo.Source, damageInfo.Target, damageInfo: damageInfo, value: damageInfo.Value, message: $"{sourceName} deals {damageInfo.Value} {damageInfo.Type} damage to {targetName}.");
        return false;
    }

    private static bool TryPreventDamageWithVineArmor(DamageInfo damageInfo, PlayerCharacter targetCharacter)
    {
        if (!targetCharacter.HasVineArmor)
        {
            return false;
        }

        bool isNormalSlash = damageInfo.CauseCardType == CardType.Slash && damageInfo.Type == DamageType.Physical;
        bool isMassTrick = damageInfo.CauseCardType is CardType.Barbarians or CardType.ArrowBarrage;
        if (!isNormalSlash && !isMassTrick)
        {
            return false;
        }

        GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName}'s Vine Armor prevents damage from {FormatCauseCardName(damageInfo.CauseCardType)}.");
        GameManager.Instance?.EmitRuleEvent(
            RuleEventType.DamageApplied,
            damageInfo.Source,
            targetCharacter,
            damageInfo: new DamageInfo(damageInfo.Source, targetCharacter, 0, damageInfo.Type, damageInfo.AllowsDodgeResponse, damageInfo.IsChainTransfer, damageInfo.CauseCardType),
            value: 0,
            message: $"{targetCharacter.CharacterName}'s Vine Armor prevents damage.");
        return true;
    }

    private static bool DamageIgnoresArmor(DamageInfo damageInfo)
    {
        return CardRules.IsSlash(damageInfo.CauseCardType)
            && damageInfo.Source is PlayerCharacter sourceCharacter
            && sourceCharacter.IgnoresTargetArmor;
    }

    private static string FormatCauseCardName(CardType cardType)
    {
        return cardType switch
        {
            CardType.Slash => "Slash",
            CardType.Barbarians => "Barbarians",
            CardType.ArrowBarrage => "Arrow Barrage",
            _ => cardType.ToString()
        };
    }

    private void TryPropagateChainDamage(DamageInfo damageInfo, PlayerCharacter damagedCharacter, int finalDamage)
    {
        if (finalDamage <= 0
            || damageInfo.IsChainTransfer
            || !damagedCharacter.IsChained
            || damageInfo.Type == DamageType.Physical
            || GameManager.Instance?.ActionManager is not { } actionManager)
        {
            return;
        }

        damagedCharacter.SetChained(false);
        GameManager.Instance.AddBattleLog($"{damagedCharacter.CharacterName}'s chain conducts {damageInfo.Type} damage.");

        foreach (PlayerCharacter chainedTarget in GameManager.Instance.GetAliveCharacters()
            .Where(character => character != damagedCharacter && character.IsChained)
            .ToList())
        {
            chainedTarget.SetChained(false);
            DamageInfo chainedDamage = new(damageInfo.Source, chainedTarget, finalDamage, damageInfo.Type, allowsDodgeResponse: false, isChainTransfer: true, causeCardType: damageInfo.CauseCardType);
            actionManager.AddToBottom(new DamageAction(actionManager, chainedDamage));
            GameManager.Instance.AddBattleLog($"{chainedTarget.CharacterName} receives chained {damageInfo.Type} damage.");
        }
    }

    public override void Reset()
    {
        base.Reset();
        _hasRaisedTargetingEvent = false;
        _waitingForInterruptResolution = false;
        _waitingForDyingResolution = false;
        _pendingTargetingArgs = null;
        DamageInfo = _initialDamageInfo;
        WasCancelled = false;
        WasDodged = false;
    }

    private void RaiseTargetingEvent()
    {
        _hasRaisedTargetingEvent = true;

        DamageTargetingEventArgs args = new(this, _actionManager, DamageInfo);
        _pendingTargetingArgs = args;
        GameManager.Instance?.EmitRuleEvent(RuleEventType.DamageTargeting, DamageInfo.Source, DamageInfo.Target, damageInfo: DamageInfo, value: DamageInfo.Value, message: "Damage target is chosen.");
        OnDamageTargeting?.Invoke(this, args);
        SyncFromPendingArgs();

        if (DamageInfo.Target is PlayerCharacter targetCharacter
            && !WasCancelled
            && !WasDodged
            && DamageInfo.AllowsDodgeResponse
            && GameManager.Instance?.TryBeginManualDamageResponse(this, targetCharacter, args) == true)
        {
            _waitingForInterruptResolution = true;
            return;
        }

        GameManager.Instance?.ClearResponseWindow();
        _pendingTargetingArgs = null;
    }

    private void SyncFromPendingArgs()
    {
        if (_pendingTargetingArgs is null)
        {
            return;
        }

        DamageInfo = _pendingTargetingArgs.DamageInfo;
        WasCancelled = _pendingTargetingArgs.IsCancelled;
        WasDodged = _pendingTargetingArgs.WasDodged;
        _waitingForInterruptResolution = _pendingTargetingArgs.WaitForInterruptResolution;
    }

    private void TryForceDodgedDamageWithStoneAxe()
    {
        if (!WasDodged
            || !CardRules.IsSlash(DamageInfo.CauseCardType)
            || DamageInfo.Source is not PlayerCharacter sourceCharacter
            || DamageInfo.Target is not PlayerCharacter targetCharacter
            || !sourceCharacter.CanUseStoneAxe
            || sourceCharacter.HandCardCount < 2
            || GameManager.Instance is not { } gameManager)
        {
            return;
        }

        List<CiyuanSha.Gameplay.Cards.CardInstance> discardedCards = sourceCharacter.HandCards
            .Take(2)
            .ToList();
        int actualDiscardedCount = 0;
        foreach (CiyuanSha.Gameplay.Cards.CardInstance card in discardedCards)
        {
            if (!sourceCharacter.TryConsumeHandCardByInstanceId(card.InstanceId, out CiyuanSha.Gameplay.Cards.CardInstance? discardedCard)
                || discardedCard is null)
            {
                continue;
            }

            actualDiscardedCount++;
            gameManager.DiscardCardFromEffect(discardedCard);
            gameManager.EmitCardsLost(sourceCharacter, sourceCharacter, discardedCard, value: 1, message: $"{sourceCharacter.CharacterName} loses {discardedCard.DisplayName} for Stone Axe.");
            gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, sourceCharacter, sourceCharacter, discardedCard, value: 1, message: $"{sourceCharacter.CharacterName} discards {discardedCard.DisplayName} for Stone Axe.");
        }

        if (actualDiscardedCount < 2)
        {
            return;
        }

        int forcedDamage = Math.Max(1, _initialDamageInfo.Value);
        DamageInfo = new DamageInfo(sourceCharacter, targetCharacter, forcedDamage, DamageInfo.Type, allowsDodgeResponse: false, DamageInfo.IsChainTransfer, DamageInfo.CauseCardType);
        WasCancelled = false;
        WasDodged = false;
        gameManager.AddBattleLog($"{sourceCharacter.CharacterName}'s Stone Axe forces damage through after {targetCharacter.CharacterName}'s Dodge.");
    }

    private void TryFollowUpSlashWithGreenDragonBlade()
    {
        if (!WasDodged
            || !WasCancelled
            || !CardRules.IsSlash(DamageInfo.CauseCardType)
            || DamageInfo.Source is not PlayerCharacter sourceCharacter
            || DamageInfo.Target is not PlayerCharacter targetCharacter
            || !sourceCharacter.CanUseGreenDragonBlade
            || !targetCharacter.IsAlive
            || GameManager.Instance is not { } gameManager
            || gameManager.ActionManager is not { } actionManager)
        {
            return;
        }

        CardInstance? followUpSlash = sourceCharacter.HandCards.FirstOrDefault(card => CardRules.IsSlash(card.CardType));
        if (followUpSlash is null)
        {
            return;
        }

        if (!sourceCharacter.TryConsumeHandCardByInstanceId(followUpSlash.InstanceId, out CardInstance? consumedSlash)
            || consumedSlash is null)
        {
            return;
        }

        DamageType damageType = consumedSlash.CardType switch
        {
            CardType.FireSlash => DamageType.Fire,
            CardType.ThunderSlash => DamageType.Thunder,
            _ => DamageType.Physical
        };

        gameManager.DiscardCardFromEffect(consumedSlash);
        gameManager.EmitCardsLost(sourceCharacter, sourceCharacter, consumedSlash, value: 1, message: $"{sourceCharacter.CharacterName} loses {consumedSlash.DisplayName} for Green Dragon Blade.");
        gameManager.EmitRuleEvent(RuleEventType.CardUsed, sourceCharacter, targetCharacter, consumedSlash, value: 1, message: $"{sourceCharacter.CharacterName} follows up with {consumedSlash.DisplayName} using Green Dragon Blade.");
        gameManager.AddBattleLog($"{sourceCharacter.CharacterName}'s Green Dragon Blade follows up with {consumedSlash.DisplayName} on {targetCharacter.CharacterName}.");
        DamageInfo followUpDamage = new(sourceCharacter, targetCharacter, Math.Max(1, consumedSlash.DamageValue), damageType, causeCardType: consumedSlash.CardType);
        actionManager.AddToTop(new DamageAction(actionManager, followUpDamage));
    }

    private static void TryTriggerKylinBow(DamageInfo damageInfo, PlayerCharacter targetCharacter, int finalDamage)
    {
        if (finalDamage <= 0
            || !CardRules.IsSlash(damageInfo.CauseCardType)
            || damageInfo.Source is not PlayerCharacter sourceCharacter
            || !sourceCharacter.CanUseKylinBow
            || GameManager.Instance is not { } gameManager
            || !targetCharacter.TryRemoveOneHorse(out CardInstance? removedHorse)
            || removedHorse is null)
        {
            return;
        }

        gameManager.DiscardCardFromEffect(removedHorse);
        gameManager.AddBattleLog($"{sourceCharacter.CharacterName}'s Kylin Bow discards {targetCharacter.CharacterName}'s {removedHorse.DisplayName}.");
        gameManager.EmitCardsLost(targetCharacter, sourceCharacter, removedHorse, value: 1, message: $"{targetCharacter.CharacterName} loses {removedHorse.DisplayName} to Kylin Bow.");
        gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, sourceCharacter, targetCharacter, removedHorse, value: 1, message: $"{sourceCharacter.CharacterName}'s Kylin Bow discards {removedHorse.DisplayName}.");
    }

    private static bool TryReplaceSlashDamageWithIceSword(ref DamageInfo damageInfo)
    {
        if (!CardRules.IsSlash(damageInfo.CauseCardType)
            || damageInfo.Source is not PlayerCharacter sourceCharacter
            || damageInfo.Target is not PlayerCharacter targetCharacter
            || !sourceCharacter.CanUseIceSword
            || GameManager.Instance is not { } gameManager)
        {
            return false;
        }

        int discardedCount = 0;
        for (int i = 0; i < 2; i++)
        {
            if (!targetCharacter.TryRemoveOneCardOrEquipment(out CardInstance? removedCard) || removedCard is null)
            {
                break;
            }

            discardedCount++;
            gameManager.DiscardCardFromEffect(removedCard);
            if (CardRules.IsEquipment(removedCard.CardType))
            {
                gameManager.NotifyEquipmentLost(targetCharacter, sourceCharacter, removedCard);
            }

            gameManager.EmitCardsLost(targetCharacter, sourceCharacter, removedCard, value: 1, message: $"{targetCharacter.CharacterName} loses {removedCard.DisplayName} to Ice Sword.");
            gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, sourceCharacter, targetCharacter, removedCard, value: 1, message: $"{sourceCharacter.CharacterName}'s Ice Sword discards {removedCard.DisplayName}.");
        }

        if (discardedCount <= 0)
        {
            return false;
        }

        damageInfo = new DamageInfo(sourceCharacter, targetCharacter, 0, damageInfo.Type, damageInfo.AllowsDodgeResponse, damageInfo.IsChainTransfer, damageInfo.CauseCardType);
        gameManager.AddBattleLog($"{sourceCharacter.CharacterName}'s Ice Sword prevents damage and discards {discardedCount} card(s) from {targetCharacter.CharacterName}.");
        gameManager.EmitRuleEvent(RuleEventType.DamageApplied, sourceCharacter, targetCharacter, damageInfo: damageInfo, value: 0, message: $"{sourceCharacter.CharacterName}'s Ice Sword prevents damage.");
        return true;
    }

    private static bool TryApplyGudingBladeBonus(ref DamageInfo damageInfo, PlayerCharacter targetCharacter)
    {
        if (!CardRules.IsSlash(damageInfo.CauseCardType)
            || damageInfo.Source is not PlayerCharacter sourceCharacter
            || !sourceCharacter.CanUseGudingBlade
            || targetCharacter.HandCardCount > 0
            || damageInfo.Value <= 0
            || GameManager.Instance is not { } gameManager)
        {
            return false;
        }

        damageInfo = new DamageInfo(
            damageInfo.Source,
            damageInfo.Target,
            damageInfo.Value + 1,
            damageInfo.Type,
            damageInfo.AllowsDodgeResponse,
            damageInfo.IsChainTransfer,
            damageInfo.CauseCardType);
        gameManager.AddBattleLog($"{sourceCharacter.CharacterName}'s Guding Blade adds +1 damage because {targetCharacter.CharacterName} has no hand cards.");
        return true;
    }
}
