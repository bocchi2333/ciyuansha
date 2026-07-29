using System;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Standardized use flow for Wine. In PlayPhase it boosts the next Slash.
/// </summary>
public sealed class WineUseAction : CardUseAction
{
    private readonly PlayerCharacter _sourceCharacter;

    public WineUseAction(ActionManager actionManager, PlayerCharacter sourceCharacter, CardInstance card)
        : base(actionManager, new CardUseContext(sourceCharacter, card, new[] { sourceCharacter }))
    {
        _sourceCharacter = sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter));
    }

    protected override void ResolveEffect()
    {
        if (_sourceCharacter.IsDying)
        {
            HealAction healAction = new(_sourceCharacter, _sourceCharacter, 1);
            ActionManager.AddToTop(healAction);
            GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName} uses Wine to recover from dying.");
            return;
        }

        _sourceCharacter.AddPendingSlashDamageBonus(1);
        GameManager.Instance?.AddBattleLog($"{_sourceCharacter.CharacterName} uses Wine. Their next Slash deals +1 damage.");
    }
}
