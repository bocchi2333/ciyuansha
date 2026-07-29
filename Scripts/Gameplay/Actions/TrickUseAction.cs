using System;
using System.Linq;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Generic implementation for common trick cards.
/// </summary>
public sealed class TrickUseAction : CardUseAction
{
    private readonly PlayerCharacter _sourceCharacter;
    private bool _checkedNullification;
    private bool _isNullified;

    public TrickUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, CardInstance card, params PlayerCharacter[] targets)
        : base(actionManager, new CardUseContext(sourceCharacter, card, targets))
    {
        _sourceCharacter = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
    }

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        if (!_checkedNullification)
        {
            _checkedNullification = true;
            GameManager? gameManager = GameManager.Instance;
            if (!UsesPerTargetNullification(UseContext.Card.CardType)
                && gameManager?.BeginNullificationWindow(
                _sourceCharacter,
                UseContext.Card,
                responder =>
                {
                    _isNullified = true;
                    gameManager.AddBattleLog($"{UseContext.Card.DisplayName} is cancelled by {responder.CharacterName}'s Nullification.");
                },
                () => { }) == true)
            {
                return;
            }
        }

        if (GameManager.Instance?.HasPendingResponseWindow == true)
        {
            return;
        }

        if (_isNullified)
        {
            if (UseContext.Card.IsDelayedTrick)
            {
                GameManager.Instance?.DiscardCardFromEffect(UseContext.Card);
            }

            MarkDone();
            return;
        }

        base.Update();
    }

    protected override bool RequiresTarget => CardRules.NeedsTarget(UseContext.Card.CardType)
        || CardRules.TargetsAllOthers(UseContext.Card.CardType)
        || CardRules.TargetsAllAlive(UseContext.Card.CardType);

    protected override void ResolveEffect()
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null)
        {
            return;
        }

        switch (UseContext.Card.CardType)
        {
            case CardType.ExNihilo:
                gameManager.DrawCardsForEffect(_sourceCharacter, 2, $"{_sourceCharacter.CharacterName} uses Ex Nihilo and draws {{count}} card(s).");
                break;

            case CardType.Dismantle:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    TryResolveTargetWithNullification(gameManager, target, () => ResolveDismantleTarget(gameManager, target));
                    return;
                }
                break;

            case CardType.Snatch:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    TryResolveTargetWithNullification(gameManager, target, () => ResolveSnatchTarget(gameManager, target));
                    return;
                }
                break;

            case CardType.Duel:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    TryResolveTargetWithNullification(gameManager, target, () => BeginDuelSequence(gameManager, target));
                    return;
                }
                break;

            case CardType.Barbarians:
                BeginMassResponseSequence(gameManager, CardType.Slash, "Barbarians", "Slash");
                break;

            case CardType.ArrowBarrage:
                BeginMassResponseSequence(gameManager, CardType.Dodge, "Arrow Barrage", "Dodge");
                break;

            case CardType.PeachGarden:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>().Where(target => target.CurrentHealth < target.MaxHealth))
                {
                    ActionManager.AddToBottom(new HealAction(_sourceCharacter, target, 1));
                }

                gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} uses Peach Garden.");
                break;

            case CardType.Harvest:
                gameManager.ResolveHarvest(_sourceCharacter, UseContext.Targets.OfType<PlayerCharacter>());
                break;

            case CardType.IronChain:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    target.ToggleChained();
                    gameManager.AddBattleLog($"{target.CharacterName} is {(target.IsChained ? "chained" : "unchained")} by Iron Chain.");
                }
                break;

            case CardType.FireAttack:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    TryResolveTargetWithNullification(gameManager, target, () => ResolveFireAttackTarget(gameManager, target));
                    return;
                }
                break;

            case CardType.BorrowSword:
                PlayerCharacter[] borrowSwordTargets = UseContext.Targets.OfType<PlayerCharacter>().ToArray();
                if (borrowSwordTargets.Length < 2)
                {
                    gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Borrow Sword has no valid target pair.");
                    break;
                }

                PlayerCharacter weaponHolder = borrowSwordTargets[0];
                PlayerCharacter slashTarget = borrowSwordTargets[1];
                TryResolveTargetWithNullification(gameManager, weaponHolder, () => ResolveBorrowSwordTarget(gameManager, weaponHolder, slashTarget));
                break;

            case CardType.Nullification:
                gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Nullification is waiting for a response window.");
                break;

            case CardType.Indulgence:
            case CardType.SupplyShortage:
            case CardType.Lightning:
                foreach (PlayerCharacter target in UseContext.Targets.OfType<PlayerCharacter>())
                {
                    TryResolveTargetWithNullification(gameManager, target, () => gameManager.PlaceDelayedTrick(_sourceCharacter, target, UseContext.Card));
                    return;
                }
                break;

            default:
                gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} uses {UseContext.Card.DisplayName}.");
                break;
        }
    }

    private static bool UsesPerTargetNullification(CardType cardType)
    {
        return cardType is CardType.Dismantle
            or CardType.Snatch
            or CardType.Duel
            or CardType.FireAttack
            or CardType.BorrowSword
            or CardType.Barbarians
            or CardType.ArrowBarrage
            or CardType.Indulgence
            or CardType.SupplyShortage
            or CardType.Lightning;
    }

    private bool TryResolveTargetWithNullification(GameManager gameManager, PlayerCharacter target, Action resolveEffect)
    {
        if (target is null || resolveEffect is null || !target.IsAlive)
        {
            return false;
        }

        CardInstance subjectCard = UseContext.Card.Clone();
        subjectCard.DisplayName = $"{UseContext.Card.DisplayName} on {target.CharacterName}";
        return gameManager.BeginNullificationWindow(
            _sourceCharacter,
            subjectCard,
            responder => gameManager.AddBattleLog($"{UseContext.Card.DisplayName} on {target.CharacterName} is cancelled by {responder.CharacterName}'s Nullification."),
            resolveEffect);
    }

    private void ResolveDismantleTarget(GameManager gameManager, PlayerCharacter target)
    {
        gameManager.BeginTargetCardSelection(
            _sourceCharacter,
            target,
            $"Choose one card to dismantle from {target.CharacterName}.",
            (source, selectedTarget, selectedCard, selectedFromHiddenHand) =>
            {
                gameManager.DiscardCardFromEffect(selectedCard);
                gameManager.AddBattleLog($"{source.CharacterName} dismantles {selectedCard.DisplayName} from {selectedTarget.CharacterName}.");
                gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, source, selectedTarget, selectedCard, value: 1, message: $"{source.CharacterName} dismantles {selectedCard.DisplayName} from {selectedTarget.CharacterName}.");
            });
    }

    private void ResolveSnatchTarget(GameManager gameManager, PlayerCharacter target)
    {
        gameManager.BeginTargetCardSelection(
            _sourceCharacter,
            target,
            $"Choose one card to snatch from {target.CharacterName}.",
            (source, selectedTarget, selectedCard, selectedFromHiddenHand) =>
            {
                source.AddCard(selectedCard);
                string displayName = selectedFromHiddenHand ? "a hand card" : selectedCard.DisplayName;
                gameManager.AddBattleLog($"{source.CharacterName} snatches {displayName} from {selectedTarget.CharacterName}.");
                gameManager.EmitCardsGained(source, source, selectedCard, value: 1, message: $"{source.CharacterName} gains {displayName}.");
                gameManager.EmitCardMoved(source, selectedTarget, source, selectedCard, $"{displayName} moves from {selectedTarget.CharacterName} to {source.CharacterName}.");
            });
    }

    private void ResolveFireAttackTarget(GameManager gameManager, PlayerCharacter target)
    {
        if (target.HandCardCount <= 0)
        {
            gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Fire Attack has no effect because {target.CharacterName} has no hand cards.");
            return;
        }

        bool opened = gameManager.BeginHandCardSelection(
            target,
            $"Reveal one hand card for {_sourceCharacter.CharacterName}'s Fire Attack.",
            (_, revealedCard) => ContinueFireAttackAfterReveal(gameManager, target, revealedCard),
            consumeSelectedCard: false);
        if (!opened)
        {
            gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Fire Attack has no effect because {target.CharacterName} has no revealable hand card.");
        }
    }

    private void ContinueFireAttackAfterReveal(GameManager gameManager, PlayerCharacter target, CardInstance revealedCard)
    {
        gameManager.AddBattleLog($"{target.CharacterName} reveals {revealedCard.DisplayName} [{revealedCard.Suit}] for Fire Attack.");
        if (!_sourceCharacter.HandCards.Any(card => card.InstanceId != UseContext.Card.InstanceId && card.Suit == revealedCard.Suit))
        {
            gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} has no {revealedCard.Suit} card to discard for Fire Attack.");
            return;
        }

        bool opened = gameManager.BeginHandCardSelection(
            _sourceCharacter,
            $"Discard a {revealedCard.Suit} card to resolve Fire Attack on {target.CharacterName}, or decline to deal no damage.",
            (source, discardedCard) =>
            {
                gameManager.AddBattleLog($"{source.CharacterName} discards {discardedCard.DisplayName} for Fire Attack.");
                gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, source, target, discardedCard, value: 1, message: $"{source.CharacterName} discards {discardedCard.DisplayName} for Fire Attack.");

                DamageInfo damageInfo = new(source, target, 1, DamageType.Fire, allowsDodgeResponse: false, causeCardType: UseContext.Card.CardType);
                ActionManager.AddToTop(new DamageAction(ActionManager, damageInfo));
                gameManager.AddBattleLog($"{source.CharacterName} uses Fire Attack on {target.CharacterName}.");
            },
            card => card.InstanceId != UseContext.Card.InstanceId && card.Suit == revealedCard.Suit,
            canDecline: true,
            onDecline: source => gameManager.AddBattleLog($"{source.CharacterName} declines to discard for Fire Attack. No damage is dealt."));
        if (!opened)
        {
            gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Fire Attack fizzles because no discard choice is available.");
        }
    }

    private void ResolveBorrowSwordTarget(GameManager gameManager, PlayerCharacter weaponHolder, PlayerCharacter slashTarget)
    {
        gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} uses Borrow Sword on {weaponHolder.CharacterName}, naming {slashTarget.CharacterName}.");
        gameManager.BeginRequiredCardResponseWindow(
            weaponHolder,
            CardType.Slash,
            $"Borrow Sword: play Slash on {slashTarget.CharacterName} or give your weapon to {_sourceCharacter.CharacterName}.",
            responder =>
            {
                CardInstance? responseSlash = gameManager.CurrentResolvedResponseCard;
                CardType causeCardType = responseSlash?.CardType is { } cardType && CardRules.IsSlash(cardType)
                    ? cardType
                    : CardType.Slash;
                DamageType damageType = causeCardType switch
                {
                    CardType.FireSlash => DamageType.Fire,
                    CardType.ThunderSlash => DamageType.Thunder,
                    _ => DamageType.Physical
                };
                int damageValue = Math.Max(1, responseSlash?.DamageValue ?? 1);
                DamageInfo damageInfo = new(responder, slashTarget, damageValue, damageType, causeCardType: causeCardType);
                ActionManager.AddToTop(new DamageAction(ActionManager, damageInfo));
                gameManager.AddBattleLog($"{responder.CharacterName} plays Slash for Borrow Sword.");
            },
            responder =>
            {
                gameManager.TryStealEquipmentBySlot(
                    _sourceCharacter,
                    responder,
                    EquipmentSlotType.Weapon,
                    $"{_sourceCharacter.CharacterName} takes {responder.CharacterName}'s weapon with Borrow Sword.");
            });
    }

    private void BeginDuelSequence(GameManager gameManager, PlayerCharacter target)
    {
        if (!_sourceCharacter.IsAlive || target is null || !target.IsAlive)
        {
            return;
        }

        gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} challenges {target.CharacterName} to a Duel.");
        ContinueDuelSequence(gameManager, target, _sourceCharacter);
    }

    private void ContinueDuelSequence(GameManager gameManager, PlayerCharacter responder, PlayerCharacter opponent)
    {
        if (!responder.IsAlive || !opponent.IsAlive)
        {
            gameManager.AddBattleLog("Duel ends because one side is no longer alive.");
            return;
        }

        gameManager.BeginRequiredCardResponseWindow(
            responder,
            CardType.Slash,
            $"Duel: play Slash against {opponent.CharacterName} or take 1 damage.",
            currentResponder =>
            {
                gameManager.AddBattleLog($"{currentResponder.CharacterName} plays Slash in the Duel.");
                ContinueDuelSequence(gameManager, opponent, currentResponder);
            },
            currentResponder =>
            {
                DamageInfo damageInfo = new(opponent, currentResponder, 1, DamageType.Physical, allowsDodgeResponse: false);
                gameManager.AddBattleLog($"{currentResponder.CharacterName} fails to play Slash in the Duel.");
                ActionManager.AddToTop(new DamageAction(ActionManager, damageInfo));
            });
    }

    private void BeginMassResponseSequence(GameManager gameManager, CardType requiredCardType, string displayName, string responseName)
    {
        gameManager.AddBattleLog($"{_sourceCharacter.CharacterName} uses {displayName}.");
        PlayerCharacter[] effectiveTargets = UseContext.Targets
            .OfType<PlayerCharacter>()
            .Where(target => !TrySkipMassTrickForVineArmor(gameManager, target, UseContext.Card.CardType, displayName))
            .ToArray();

        gameManager.BeginRequiredCardResponseSequence(
            _sourceCharacter,
            effectiveTargets,
            requiredCardType,
            displayName,
            target => $"Play {responseName} for {displayName} or take 1 damage.",
            responder => gameManager.AddBattleLog($"{responder.CharacterName} responds to {displayName} with {responseName}."),
            responder =>
            {
                DamageInfo damageInfo = new(_sourceCharacter, responder, 1, DamageType.Physical, allowsDodgeResponse: false, causeCardType: UseContext.Card.CardType);
                ActionManager.AddToBottom(new DamageAction(ActionManager, damageInfo));
            },
            UseContext.Card);
    }

    private bool TrySkipMassTrickForVineArmor(GameManager gameManager, PlayerCharacter target, CardType cardType, string displayName)
    {
        if (target.HasVineArmor && cardType is CardType.Barbarians or CardType.ArrowBarrage)
        {
            gameManager.AddBattleLog($"{target.CharacterName}'s Vine Armor makes {displayName} ineffective.");
            return true;
        }

        return false;
    }
}
