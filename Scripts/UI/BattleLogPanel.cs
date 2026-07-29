using System;
using System.Linq;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Displays the replicated battle log with compact category coloring.
/// </summary>
public partial class BattleLogPanel : Control
{
	private const int MaxVisibleEntries = 32;

	[Export]
	public NodePath LogLabelPath { get; set; } = new NodePath();

	private RichTextLabel? _logLabel;
	private Control? _panel;
	private int _lastLogCount;

	public override void _Ready()
	{
		_logLabel = GetNodeOrNull<RichTextLabel>(LogLabelPath);
		_panel = GetNodeOrNull<Control>("Panel");
		CyberStyle.ApplyPanel(_panel, CyberPanelKind.Recessed);
		CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/VBox/TitleLabel"), title: true);
		CyberStyle.ApplyRichText(_logLabel);
		if (_logLabel is not null)
		{
			_logLabel.BbcodeEnabled = true;
			_logLabel.Text = "[color=#8C7D63]暂无战斗记录。[/color]";
		}

		if (GameManager.Instance is not null)
		{
			GameManager.Instance.OnBattleLogChanged += HandleBattleLogChanged;
		}

		RefreshLog();
	}

	public override void _ExitTree()
	{
		if (GameManager.Instance is not null)
		{
			GameManager.Instance.OnBattleLogChanged -= HandleBattleLogChanged;
		}
	}

	private void HandleBattleLogChanged()
	{
		RefreshLog();
	}

	private void RefreshLog()
	{
		if (_logLabel is null)
		{
			return;
		}

		if (GameManager.Instance is null || GameManager.Instance.BattleLog.Count == 0)
		{
			_logLabel.Text = "[color=#8C7D63]暂无战斗记录。[/color]";
			return;
		}

		int logCount = GameManager.Instance.BattleLog.Count;
		int latestSequence = GameManager.Instance.BattleLog[^1].Sequence;
		_logLabel.Text = string.Join("\n", GameManager.Instance.BattleLog
			.TakeLast(MaxVisibleEntries)
			.Select(entry => FormatEntry(entry, latestSequence)));
		_logLabel.ScrollToLine(Math.Max(0, _logLabel.GetLineCount() - 1));
		if (logCount > _lastLogCount)
		{
			PlayLogPulse();
		}

		_lastLogCount = logCount;
	}

	private static string FormatEntry(NetworkBattleLogEntry entry, int latestSequence)
	{
		string displayMessage = BattleLogTextLocalizer.Localize(entry.Message);
		LogVisual visual = PickEntryVisual(displayMessage);
		string marker = entry.Sequence == latestSequence ? "[color=#D8B36C]▶[/color] " : "  ";
		return $"{marker}[color=#8C7D63]{entry.Sequence:D2}.[/color] [color={visual.Color}]{visual.Tag}[/color] [color=#CDBF9C]{EscapeBbcode(displayMessage)}[/color]";
	}

	private void PlayLogPulse()
	{
		if (_panel is null || !IsInsideTree())
		{
			return;
		}

		_panel.Modulate = new Color(1f, 0.86f, 0.58f, 1f);
		Tween tween = CreateTween();
		tween.TweenProperty(_panel, "modulate", Colors.White, 0.22f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}

	private static LogVisual PickEntryVisual(string message)
	{
		if (ContainsAny(message, "damage", "defeated", "dying", "伤害", "濒死", "阵亡", "死亡"))
		{
			return new LogVisual("[伤害]", "#D76A4D");
		}

		if (ContainsAny(message, "heal", "Peach", "救援", "回复", "治疗", "桃", "酒"))
		{
			return new LogVisual("[回复]", "#65A97C");
		}

		if (ContainsAny(message, "Dodge", "Nullification", "respond", "response", "响应", "闪", "无懈"))
		{
			return new LogVisual("[响应]", "#D8B36C");
		}

		if (ContainsAny(message, "judge", "Lightning", "判定", "闪电"))
		{
			return new LogVisual("[判定]", "#9B7AD5");
		}

		if (ContainsAny(message, "draw", "discard", "摸牌", "弃牌", "牌堆"))
		{
			return new LogVisual("[牌堆]", "#A99B7A");
		}

		return new LogVisual("[记录]", "#BFAE8A");
	}

	private static bool ContainsAny(string text, params string[] needles)
	{
		return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
	}

	private static string EscapeBbcode(string text)
	{
		return text
			.Replace("[", "[lb]", StringComparison.Ordinal)
			.Replace("]", "[rb]", StringComparison.Ordinal);
	}

	private readonly record struct LogVisual(string Tag, string Color);
}
