using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using Godot;

namespace CiyuanSha.Gameplay.Core;

/// <summary>
/// Structured payload for high-level rule events.
/// </summary>
public sealed class RuleEventContext
{
	public RuleEventType EventType { get; set; }

	public Node? Source { get; set; }

	public Node? Target { get; set; }

	public PlayerCharacter? FromCharacter { get; set; }

	public PlayerCharacter? ToCharacter { get; set; }

	public CardInstance? Card { get; set; }

	public DamageInfo? DamageInfo { get; set; }

	public TurnPhase Phase { get; set; }

	public CardUseStage? CardUseStage { get; set; }

	public ResponseWindowKind ResponseKind { get; set; }

	public string SkillId { get; set; } = string.Empty;

	public string SkillName { get; set; } = string.Empty;

	public int ActingPeerId { get; set; }

	public int Value { get; set; }

	public string Message { get; set; } = string.Empty;
}
