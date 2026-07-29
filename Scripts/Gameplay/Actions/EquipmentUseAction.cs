using System;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Standardized use flow for equipment cards.
/// </summary>
public sealed class EquipmentUseAction : CardUseAction
{
    private readonly PlayerCharacter _sourceCharacter;
    private readonly Action<CardInstance?> _discardReplacedEquipment;

    public EquipmentUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, CardInstance card, Action<CardInstance?> discardReplacedEquipment)
        : base(actionManager, new CardUseContext(sourceCharacter, card, Array.Empty<PlayerCharacter>()))
    {
        _sourceCharacter = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
        _discardReplacedEquipment = discardReplacedEquipment ?? throw new ArgumentNullException(nameof(discardReplacedEquipment));
    }

    protected override bool RequiresTarget => false;

    protected override void ResolveEffect()
    {
        EquipAction equipAction = new(_sourceCharacter, UseContext.Card.Clone(), _discardReplacedEquipment);
        ActionManager.AddToTop(equipAction);
        GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName} equips {UseContext.Card.DisplayName}.");
    }
}
