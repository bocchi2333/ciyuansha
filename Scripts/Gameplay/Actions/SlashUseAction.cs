using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Standardized use flow for Slash before it resolves into a damage event.
/// </summary>
public sealed class SlashUseAction : CardUseAction
{
    private readonly PlayerCharacter _sourceCharacter;
    private readonly List<PlayerCharacter> _targetCharacters;
    private readonly DamageType _damageType;

    public SlashUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, CardInstance card, DamageType damageType)
        : this(actionManager, sourceCharacter, new[] { targetCharacter }, card, damageType)
    {
    }

    public SlashUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, IEnumerable<PlayerCharacter> targetCharacters, CardInstance card, DamageType damageType)
        : base(actionManager, new CardUseContext(sourceCharacter, card, targetCharacters))
    {
        _sourceCharacter = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
        _targetCharacters = targetCharacters?
            .Where(target => target is not null)
            .Distinct()
            .ToList() ?? new List<PlayerCharacter>();
        _damageType = damageType;
    }

    protected override void ResolveEffect()
    {
        foreach (PlayerCharacter targetCharacter in _targetCharacters.Where(target => target.IsAlive))
        {
            TryTriggerDoubleSwords(targetCharacter);

            DamageInfo damageInfo = new(_sourceCharacter, targetCharacter, Math.Max(0, UseContext.Card.DamageValue), _damageType, causeCardType: UseContext.Card.CardType);
            DamageAction damageAction = new(ActionManager, damageInfo);

            if (targetCharacter is PlayerResponder responder)
            {
                responder.AttachToDamageAction(damageAction);
            }

            ActionManager.AddToTop(damageAction);
            GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName} uses {UseContext.Card.DisplayName} on {targetCharacter.CharacterName}.");
        }
    }

    protected override void OnUseCancelled()
    {
        GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName}'s {UseContext.Card.DisplayName} is cancelled because it has no valid target.");
    }

    private void TryTriggerDoubleSwords(PlayerCharacter targetCharacter)
    {
        if (!_sourceCharacter.CanUseDoubleSwords
            || _sourceCharacter.Gender == PlayerGender.Unknown
            || targetCharacter.Gender == PlayerGender.Unknown
            || _sourceCharacter.Gender == targetCharacter.Gender
            || GameManager.Instance is not { } gameManager)
        {
            return;
        }

        if (targetCharacter.HandCards.FirstOrDefault() is { } discardedCandidate
            && targetCharacter.TryConsumeHandCardByInstanceId(discardedCandidate.InstanceId, out CardInstance? discardedCard)
            && discardedCard is not null)
        {
            gameManager.DiscardCardFromEffect(discardedCard);
            gameManager.AddBattleLog($"{targetCharacter.CharacterName} discards {discardedCard.DisplayName} for {_sourceCharacter.CharacterName}'s Double Swords.");
            gameManager.EmitCardsLost(targetCharacter, _sourceCharacter, discardedCard, value: 1, message: $"{targetCharacter.CharacterName} loses {discardedCard.DisplayName} for Double Swords.");
            gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, _sourceCharacter, targetCharacter, discardedCard, value: 1, message: $"{targetCharacter.CharacterName} discards {discardedCard.DisplayName} for Double Swords.");
            return;
        }

        int drawnCount = gameManager.DrawCardsForEffect(_sourceCharacter, 1, $"{_sourceCharacter.CharacterName}'s Double Swords draws {{count}} card(s).");
        if (drawnCount > 0)
        {
            gameManager.AddBattleLog($"{_sourceCharacter.CharacterName}'s Double Swords draws a card because {targetCharacter.CharacterName} has no hand cards.");
        }
    }
}
