using System;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Standardized use flow for Peach before it resolves into a healing event.
/// </summary>
public sealed class PeachUseAction : CardUseAction
{
    private readonly PlayerCharacter _sourceCharacter;
    private readonly PlayerCharacter _targetCharacter;

    public PeachUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, CardInstance card)
        : base(actionManager, new CardUseContext(sourceCharacter, card, new[] { targetCharacter }))
    {
        _sourceCharacter = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
        _targetCharacter = targetCharacter ?? throw new ArgumentNullException(nameof(targetCharacter));
    }

    protected override void ResolveEffect()
    {
        HealAction healAction = new(_sourceCharacter, _targetCharacter, Math.Max(1, UseContext.Card.DamageValue));
        ActionManager.AddToTop(healAction);
        GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName} uses {UseContext.Card.DisplayName}.");
    }
}
