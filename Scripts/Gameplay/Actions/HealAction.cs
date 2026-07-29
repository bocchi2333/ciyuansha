using System;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// Resolves a simple healing effect.
/// </summary>
public class HealAction : GameAction
{
    public Node? Source { get; }

    public Node? Target { get; }

    public int HealValue { get; }

    public HealAction(Node? source, Node? target, int healValue)
    {
        Source = source;
        Target = target;
        HealValue = Math.Max(0, healValue);
    }

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        if (HealValue <= 0)
        {
            MarkDone();
            return;
        }

        string sourceName = Source?.Name ?? "UnknownSource";
        string targetName = Target?.Name ?? "UnknownTarget";
        GD.Print($"{sourceName} heals {targetName} for {HealValue}.");

        if (Target is PlayerCharacter targetCharacter)
        {
            int beforeHealth = targetCharacter.CurrentHealth;
            targetCharacter.Heal(HealValue);
            int healedValue = Math.Max(0, targetCharacter.CurrentHealth - beforeHealth);
            if (healedValue > 0)
            {
                GameManager.Instance?.AddBattleLog($"{targetCharacter.CharacterName} recovers {healedValue} health.");
                GameManager.Instance?.EmitRuleEvent(RuleEventType.Healed, Source, targetCharacter, value: healedValue, message: $"{targetCharacter.CharacterName} recovers {healedValue} health.");
            }
        }

        MarkDone();
    }
}
