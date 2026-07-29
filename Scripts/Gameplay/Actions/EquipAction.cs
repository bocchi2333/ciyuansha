using System;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Equips a weapon or armor card onto a character.
/// </summary>
public class EquipAction : GameAction
{
    private readonly PlayerCharacter _character;
    private readonly CardInstance _equipmentCard;
    private readonly Action<CardInstance>? _discardReplacedEquipment;

    public EquipAction(PlayerCharacter character, CardInstance equipmentCard, Action<CardInstance>? discardReplacedEquipment = null)
    {
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _equipmentCard = equipmentCard ?? throw new ArgumentNullException(nameof(equipmentCard));
        _discardReplacedEquipment = discardReplacedEquipment;
    }

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        bool equipped = _character.TryEquipCard(_equipmentCard, out CardInstance? replacedCard);
        if (equipped && replacedCard is not null)
        {
            _discardReplacedEquipment?.Invoke(replacedCard);
            GameManager.Instance?.EmitCardsLost(_character, _character, replacedCard, value: 1, message: $"{_character.CharacterName} loses {replacedCard.DisplayName}.");
            GameManager.Instance?.EmitRuleEvent(RuleEventType.CardsDiscarded, _character, _character, replacedCard, value: 1, message: $"{_character.CharacterName} discards replaced equipment {replacedCard.DisplayName}.");
            GameManager.Instance?.NotifyEquipmentLost(_character, _character, replacedCard);
        }

        if (equipped)
        {
            GameManager.Instance?.EmitRuleEvent(RuleEventType.EquipmentEquipped, _character, _character, _equipmentCard, value: _character.EffectiveAttackRange, message: $"{_character.CharacterName} equips {_equipmentCard.DisplayName}.");
            GameManager.Instance?.EmitCardMoved(_character, _character, _character, _equipmentCard, $"{_equipmentCard.DisplayName} moves to {_character.CharacterName}'s equipment area.");
        }

        MarkDone();
    }
}
