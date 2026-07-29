using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// General card presentation widget.
/// Shows portrait, name, stats, and skill summary.
/// </summary>
public partial class GeneralCardView : Control
{
	public int BoundPeerId => _character?.OwnerPeerId ?? 0;

	[Export]
	public NodePath CharacterPath { get; set; } = new NodePath();

	[Export]
	public NodePath PortraitTextureRectPath { get; set; } = new NodePath();

	[Export]
	public NodePath NameLabelPath { get; set; } = new NodePath();

	[Export]
	public NodePath HealthLabelPath { get; set; } = new NodePath();

	[Export]
	public NodePath HandCardLabelPath { get; set; } = new NodePath();

	[Export]
	public NodePath SkillLabelPath { get; set; } = new NodePath();

	[Export]
	public NodePath HealthBarPath { get; set; } = new NodePath();

	[Export]
	public Texture2D? FallbackPortrait { get; set; }

	private PlayerCharacter? _character;
	private TextureRect? _portraitTextureRect;
	private Label? _nameLabel;
	private Label? _healthLabel;
	private Label? _handCardLabel;
	private Label? _skillLabel;
	private ProgressBar? _healthBar;
	private HBoxContainer? _healthBeads;
	private HBoxContainer? _equipmentTags;
	private Label? _stateLabel;
	private ColorRect? _stateLine;
	private int _lastHealth = -1;

	public override void _Ready()
	{
		AddToGroup("ciyuansha_general_card_views");
		CacheViewNodes();
		ApplyChrome();

		if (_character is null)
		{
			BindCharacter(ResolveCharacter());
		}

		BindGlobalEvents();
		RefreshView();
	}

	public override void _ExitTree()
	{
		UnbindCharacterEvents();
		UnbindGlobalEvents();
	}

	public void BindCharacter(PlayerCharacter? character)
	{
		if (_character == character)
		{
			return;
		}

		UnbindCharacterEvents();
		_character = character;

		if (_character is not null)
		{
			_character.OnStatsChanged += HandleCharacterChanged;
			_character.OnDefeated += HandleCharacterChanged;
			_character.OnHandCardsChanged += HandleCharacterChanged;
			_character.OnEquipmentChanged += HandleCharacterChanged;
		}

		RefreshView();
	}

	private void BindGlobalEvents()
	{
		if (GameManager.Instance is not null)
		{
			GameManager.Instance.OnPhaseChanged += HandleGlobalPhaseChanged;
			GameManager.Instance.OnTurnOwnerChanged += HandleGlobalTurnOwnerChanged;
			GameManager.Instance.OnStateChanged += HandleGlobalStateChanged;
			GameManager.Instance.OnResponseWindowChanged += HandleGlobalStateChanged;
			GameManager.Instance.OnMatchRunningChanged += HandleGlobalMatchRunningChanged;
			GameManager.Instance.OnMatchEnded += HandleGlobalMatchEnded;
		}

		if (LanMultiplayerManager.Instance is not null)
		{
			LanMultiplayerManager.Instance.OnLobbyChanged += HandleLobbyChanged;
			LanMultiplayerManager.Instance.OnSessionStateChanged += HandleNetworkStateChanged;
		}
	}

	private void UnbindGlobalEvents()
	{
		if (GameManager.Instance is not null)
		{
			GameManager.Instance.OnPhaseChanged -= HandleGlobalPhaseChanged;
			GameManager.Instance.OnTurnOwnerChanged -= HandleGlobalTurnOwnerChanged;
			GameManager.Instance.OnStateChanged -= HandleGlobalStateChanged;
			GameManager.Instance.OnResponseWindowChanged -= HandleGlobalStateChanged;
			GameManager.Instance.OnMatchRunningChanged -= HandleGlobalMatchRunningChanged;
			GameManager.Instance.OnMatchEnded -= HandleGlobalMatchEnded;
		}

		if (LanMultiplayerManager.Instance is not null)
		{
			LanMultiplayerManager.Instance.OnLobbyChanged -= HandleLobbyChanged;
			LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleNetworkStateChanged;
		}
	}

	public void RefreshView()
	{
		UpdatePortrait();
		UpdateTextFields();
		UpdateHealthBar();
		UpdateEquipmentTags();
		UpdateStateBadge();
		UpdateAliveState();
	}

	private void CacheViewNodes()
	{
		_portraitTextureRect = GetNodeOrNull<TextureRect>(PortraitTextureRectPath);
		_nameLabel = GetNodeOrNull<Label>(NameLabelPath);
		_healthLabel = GetNodeOrNull<Label>(HealthLabelPath);
		_handCardLabel = GetNodeOrNull<Label>(HandCardLabelPath);
		_skillLabel = GetNodeOrNull<Label>(SkillLabelPath);
		_healthBar = GetNodeOrNull<ProgressBar>(HealthBarPath);
		_healthBeads = GetNodeOrNull<HBoxContainer>("CardBackground/HealthBeads");
		if (_healthBeads is null)
		{
			_healthBeads = new HBoxContainer
			{
				Name = "HealthBeads",
				Alignment = BoxContainer.AlignmentMode.End
			};
			_healthBeads.AddThemeConstantOverride("separation", 3);
			_healthBeads.AnchorLeft = 0.54f;
			_healthBeads.AnchorRight = 0.95f;
			_healthBeads.AnchorTop = 0.73f;
			_healthBeads.AnchorBottom = 0.80f;
			GetNodeOrNull<Control>("CardBackground")?.AddChild(_healthBeads);
		}

		_equipmentTags = GetNodeOrNull<HBoxContainer>("CardBackground/EquipmentTags");
		if (_equipmentTags is null)
		{
			_equipmentTags = new HBoxContainer
			{
				Name = "EquipmentTags",
				Alignment = BoxContainer.AlignmentMode.Center
			};
			_equipmentTags.AddThemeConstantOverride("separation", 4);
			_equipmentTags.AnchorLeft = 0f;
			_equipmentTags.AnchorRight = 1f;
			_equipmentTags.AnchorTop = 0f;
			_equipmentTags.AnchorBottom = 0f;
			_equipmentTags.OffsetLeft = 12f;
			_equipmentTags.OffsetTop = 320f;
			_equipmentTags.OffsetRight = -12f;
			_equipmentTags.OffsetBottom = 344f;
			GetNodeOrNull<Control>("CardBackground")?.AddChild(_equipmentTags);
		}

		_stateLine = GetNodeOrNull<ColorRect>("CardBackground/StateLine");
		if (_stateLine is null)
		{
			_stateLine = new ColorRect
			{
				Name = "StateLine",
				MouseFilter = MouseFilterEnum.Ignore,
				Color = new Color(CyberStyle.MutedText.R, CyberStyle.MutedText.G, CyberStyle.MutedText.B, 0.28f)
			};
			_stateLine.AnchorLeft = 0.08f;
			_stateLine.AnchorRight = 0.92f;
			_stateLine.AnchorTop = 0f;
			_stateLine.AnchorBottom = 0f;
			_stateLine.OffsetTop = 5f;
			_stateLine.OffsetBottom = 7f;
			GetNodeOrNull<Control>("CardBackground")?.AddChild(_stateLine);
		}

		_stateLabel = GetNodeOrNull<Label>("CardBackground/StateLabel");
		if (_stateLabel is null)
		{
			_stateLabel = new Label
			{
				Name = "StateLabel",
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Text = string.Empty
			};
			_stateLabel.AnchorLeft = 0.63f;
			_stateLabel.AnchorRight = 0.95f;
			_stateLabel.AnchorTop = 0f;
			_stateLabel.AnchorBottom = 0f;
			_stateLabel.OffsetTop = 12f;
			_stateLabel.OffsetBottom = 34f;
			GetNodeOrNull<Control>("CardBackground")?.AddChild(_stateLabel);
		}
	}

	private PlayerCharacter? ResolveCharacter()
	{
		if (CharacterPath.IsEmpty)
		{
			return null;
		}

		return GetNodeOrNull<PlayerCharacter>(CharacterPath);
	}

	private void UpdatePortrait()
	{
		if (_portraitTextureRect is null)
		{
			return;
		}

		Texture2D? texture = _character?.LoadGeneralCardTexture() ?? FallbackPortrait;
		_portraitTextureRect.Texture = texture;
	}

	private void UpdateTextFields()
	{
		if (_nameLabel is not null)
		{
			_nameLabel.Text = _character?.CharacterName ?? "未绑定武将";
		}

		if (_healthLabel is not null)
		{
			_healthLabel.Text = _character is null
				? "体力：-/-"
				: $"体力：{_character.CurrentHealth}/{_character.MaxHealth}";
		}

		if (_handCardLabel is not null)
		{
			_handCardLabel.Text = _character is null
				? "手牌：-"
				: $"手牌：{_character.HandCardCount} | 攻距：{_character.EffectiveAttackRange}";
		}

		if (_skillLabel is not null)
		{
			_skillLabel.Text = BuildSkillSummaryText();
		}
	}

	private void UpdateHealthBar()
	{
		if (_healthBar is null)
		{
			return;
		}

		if (_character is null)
		{
			_healthBar.MaxValue = 1;
			_healthBar.Value = 0;
			_lastHealth = -1;
			return;
		}

		if (_lastHealth >= 0 && _lastHealth != _character.CurrentHealth)
		{
			FlashHealthChange(_character.CurrentHealth < _lastHealth);
		}

		_lastHealth = _character.CurrentHealth;
		_healthBar.MaxValue = _character.MaxHealth;
		_healthBar.Value = _character.CurrentHealth;
		UpdateHealthBeads();
	}

	private void UpdateAliveState()
	{
		Modulate = _character is not null && _character.IsDefeated
			? new Color(0.55f, 0.55f, 0.55f, 1f)
			: Colors.White;
	}

	private void UpdateStateBadge()
	{
		if (_stateLabel is null || _stateLine is null)
		{
			return;
		}

		(string text, Color color, string tooltip) = BuildStateBadge();
		_stateLabel.Text = text;
		_stateLabel.TooltipText = tooltip;
		_stateLabel.AddThemeColorOverride("font_color", color);
		_stateLine.Color = new Color(color.R, color.G, color.B, 0.58f);
	}

	private (string Text, Color Color, string Tooltip) BuildStateBadge()
	{
		if (_character is null)
		{
			return ("未绑定", CyberStyle.MutedText, "该武将卡尚未绑定角色。");
		}

		if (_character.IsDefeated)
		{
			return ("阵亡", CyberStyle.MutedText, "该角色已经阵亡。");
		}

		if (_character.IsDying)
		{
			return ("濒死", CyberStyle.Vermilion, "该角色处于濒死状态，等待救援。");
		}

		GameManager? game = GameManager.Instance;
		if (game?.IsMatchRunning == true)
		{
			if (game.HasPendingResponseWindow && game.PendingResponsePeerId == _character.OwnerPeerId)
			{
				return ("响应中", CyberStyle.Vermilion, "该角色需要处理中央响应窗口。");
			}

			if (game.CurrentTurnPeerId == _character.OwnerPeerId)
			{
				return (FormatPhaseBadge(game.CurrentPhase), CyberStyle.NeonJade, "当前回合角色。");
			}
		}

		if (IsOffline(_character.OwnerPeerId))
		{
			return ("离线", CyberStyle.MutedText, "该玩家当前离线，座位保留。");
		}

		if (IsLocalPeer(_character.OwnerPeerId))
		{
			return ("你", CyberStyle.Gold, "这是你的武将。");
		}

		return ("等待", CyberStyle.PaperDark, "等待其他操作。");
	}

	private string BuildSkillText()
	{
		if (_character is null)
		{
			return "技能：-";
		}

		string skillSummary = _character.Skills.Count == 0
			? "None"
			: string.Join(", ", _character.Skills.Select(FormatSkillSummary));

		string weaponName = _character.EquippedWeapon?.DisplayName ?? "-";
		string armorName = _character.EquippedArmor?.DisplayName ?? "-";
		string offensiveHorseName = _character.EquippedOffensiveHorse?.DisplayName ?? "-";
		string defensiveHorseName = _character.EquippedDefensiveHorse?.DisplayName ?? "-";
		string treasureName = _character.EquippedTreasure?.DisplayName ?? "-";
		string delayedTricks = _character.DelayedTricks.Count == 0
			? "-"
			: string.Join(", ", _character.DelayedTricks.Select(card => card.DisplayName));
		string chainState = _character.IsChained ? "Chained" : "Normal";
		return $"Faction: {_character.Faction}\nSkills: {skillSummary}\nEquip: W {weaponName} | A {armorName} | -1 {offensiveHorseName} | +1 {defensiveHorseName} | T {treasureName}\nJudge: {delayedTricks} | State: {chainState}";
	}

	private string BuildSkillSummaryText()
	{
		if (_character is null)
		{
			return "技能：-";
		}

		string skillSummary = _character.Skills.Count == 0
			? "无"
			: string.Join(", ", _character.Skills.Select(skill => skill.DisplayName));
		return $"阵营：{FormatFaction(_character.Faction)} | 技能：{skillSummary}";
	}

	private static string FormatFaction(PlayerFaction faction)
	{
		return faction switch
		{
			PlayerFaction.Lord => "主公",
			PlayerFaction.Loyalist => "忠臣",
			PlayerFaction.Rebel => "反贼",
			PlayerFaction.Renegade => "内奸",
			PlayerFaction.Neutral => "中立",
			PlayerFaction.None => "未定",
			_ => faction.ToString()
		};
	}

	private static string FormatPhaseBadge(TurnPhase phase)
	{
		return phase switch
		{
			TurnPhase.TurnStart => "准备",
			TurnPhase.JudgementPhase => "判定",
			TurnPhase.DrawPhase => "摸牌",
			TurnPhase.PlayPhase => "出牌",
			TurnPhase.DiscardPhase => "弃牌",
			TurnPhase.EndPhase => "结束",
			_ => "当前"
		};
	}

	private static bool IsLocalPeer(int peerId)
	{
		LanMultiplayerManager? network = LanMultiplayerManager.Instance;
		return network?.IsConnected == true && network.LocalPeerId == peerId;
	}

	private static bool IsOffline(int peerId)
	{
		LanMultiplayerManager? network = LanMultiplayerManager.Instance;
		return network?.IsConnected == true
			&& network.Players.TryGetValue(peerId, out LanPlayerInfo? player)
			&& !player.IsConnected;
	}

	private static string FormatSkillSummary(CharacterSkill skill)
	{
		string usage = skill.UsageScope == SkillUsageScope.OncePerTurn
			? $" ({skill.UsesThisTurn}/1)"
			: string.Empty;
		string active = skill.IsActiveSkill ? " Active" : string.Empty;
		string cost = skill.RequiresHandCardCost ? " Cost:hand" : string.Empty;
		string target = skill.TargetingMode != SkillTargetingMode.None ? $" Target:{skill.TargetingMode}" : string.Empty;
		string timing = skill.TriggerTimings != SkillTriggerTiming.None ? $" Timing:{skill.TriggerTimings}" : string.Empty;
		return $"{skill.DisplayName}{usage}{active}{cost}{target}{timing}";
	}

	private void ApplyChrome()
	{
		CyberStyle.ApplyPanel(GetNodeOrNull<Control>("CardBackground"), CyberPanelKind.Card);
		CyberStyle.ApplyLabel(_nameLabel, title: true);
		CyberStyle.ApplyLabel(_healthLabel, color: CyberStyle.Fire);
		CyberStyle.ApplyLabel(_handCardLabel, color: CyberStyle.NeonJade);
		CyberStyle.ApplyLabel(_skillLabel, color: CyberStyle.Paper);
		CyberStyle.ApplyLabel(_stateLabel, color: CyberStyle.Gold);
		if (_healthBar is not null)
		{
			_healthBar.AddThemeStyleboxOverride("background", MakeHealthBackground());
			_healthBar.AddThemeStyleboxOverride("fill", MakeHealthFill());
		}

		if (_portraitTextureRect is not null)
		{
			_portraitTextureRect.Modulate = new Color(0.97f, 0.96f, 0.93f, 1f);
		}
	}

	private void FlashHealthChange(bool damaged)
	{
		Color flash = damaged ? CyberStyle.Vermilion : CyberStyle.NeonJade;
		Modulate = flash.Lightened(0.12f);
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate", Colors.White, 0.22f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}

	private static StyleBoxFlat MakeHealthBackground()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.052f, 0.058f, 1f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		};
	}

	private static StyleBoxFlat MakeHealthFill()
	{
		return new StyleBoxFlat
		{
			BgColor = CyberStyle.Vermilion,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		};
	}

	private void UpdateHealthBeads()
	{
		if (_healthBeads is null)
		{
			return;
		}

		foreach (Node child in _healthBeads.GetChildren())
		{
			child.QueueFree();
		}

		if (_character is null)
		{
			return;
		}

		for (int index = 0; index < _character.MaxHealth; index++)
		{
			Panel bead = new()
			{
				CustomMinimumSize = new Vector2(10f, 18f)
			};
			bead.AddThemeStyleboxOverride("panel", MakeBeadBox(index < _character.CurrentHealth));
			_healthBeads.AddChild(bead);
		}
	}

	private void UpdateEquipmentTags()
	{
		if (_equipmentTags is null)
		{
			return;
		}

		foreach (Node child in _equipmentTags.GetChildren())
		{
			child.QueueFree();
		}

		if (_character is null)
		{
			AddEquipmentTag("武", "-", false, CyberStyle.MutedText);
			AddEquipmentTag("甲", "-", false, CyberStyle.MutedText);
			AddEquipmentTag("判", "-", false, CyberStyle.MutedText);
			return;
		}

		AddEquipmentTag("武", ShortCardName(_character.EquippedWeapon?.DisplayName), _character.EquippedWeapon is not null, CyberStyle.Gold);
		AddEquipmentTag("甲", ShortCardName(_character.EquippedArmor?.DisplayName), _character.EquippedArmor is not null, CyberStyle.Fire);
		AddEquipmentTag("-1", ShortCardName(_character.EquippedOffensiveHorse?.DisplayName), _character.EquippedOffensiveHorse is not null, CyberStyle.Paper);
		AddEquipmentTag("+1", ShortCardName(_character.EquippedDefensiveHorse?.DisplayName), _character.EquippedDefensiveHorse is not null, CyberStyle.NeonJade);
		AddEquipmentTag("宝", ShortCardName(_character.EquippedTreasure?.DisplayName), _character.EquippedTreasure is not null, CyberStyle.Thunder);
		AddEquipmentTag("判", _character.DelayedTricks.Count.ToString(), _character.DelayedTricks.Count > 0, CyberStyle.Thunder);
		AddEquipmentTag("链", _character.IsChained ? "横" : "-", _character.IsChained, CyberStyle.Fire);
	}

	private void AddEquipmentTag(string prefix, string value, bool active, Color accent)
	{
		if (_equipmentTags is null)
		{
			return;
		}

		Label label = new()
		{
			Text = $"{prefix}:{value}",
			CustomMinimumSize = new Vector2(26f, 20f),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TooltipText = BuildEquipmentTagTooltip(prefix)
		};
		label.AddThemeFontSizeOverride("font_size", 10);
		label.AddThemeColorOverride("font_color", active ? CyberStyle.Paper : CyberStyle.MutedText.Darkened(0.2f));
		label.AddThemeStyleboxOverride("normal", MakeEquipmentTagBox(active, accent));
		_equipmentTags.AddChild(label);
	}

	private static string ShortCardName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "-";
		}

		return name.Length <= 2 ? name : name[..2];
	}

	private static string BuildEquipmentTagTooltip(string prefix)
	{
		return prefix switch
		{
			"武" => "武器",
			"甲" => "防具",
			"-1" => "进攻马",
			"+1" => "防御马",
			"宝" => "宝物",
			"判" => "判定区",
			"链" => "铁索横置状态",
			_ => prefix
		};
	}

	private static StyleBoxFlat MakeEquipmentTagBox(bool active, Color accent)
	{
		return new StyleBoxFlat
		{
			BgColor = active
				? new Color(accent.R * 0.22f, accent.G * 0.22f, accent.B * 0.22f, 0.94f)
				: new Color(0.035f, 0.032f, 0.027f, 0.78f),
			BorderColor = active ? accent.Darkened(0.1f) : new Color(0.18f, 0.16f, 0.13f, 0.9f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
			ContentMarginLeft = 3,
			ContentMarginRight = 3
		};
	}

	private static StyleBoxFlat MakeBeadBox(bool alive)
	{
		Color color = alive
			? CyberStyle.NeonJade.Lightened(0.08f)
			: new Color(0.30f, 0.285f, 0.25f, 1f);
		return new StyleBoxFlat
		{
			BgColor = color,
			BorderColor = alive ? CyberStyle.Gold.Darkened(0.25f) : CyberStyle.Ink,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5,
			CornerRadiusBottomRight = 5
		};
	}

	private void HandleCharacterChanged(PlayerCharacter character)
	{
		RefreshView();
	}

	private void HandleGlobalStateChanged()
	{
		UpdateStateBadge();
	}

	private void HandleGlobalPhaseChanged(TurnPhase phase)
	{
		UpdateStateBadge();
	}

	private void HandleGlobalTurnOwnerChanged(int peerId)
	{
		UpdateStateBadge();
	}

	private void HandleGlobalMatchRunningChanged(bool isRunning)
	{
		UpdateStateBadge();
	}

	private void HandleGlobalMatchEnded(int winnerPeerId, string resultMessage)
	{
		UpdateStateBadge();
	}

	private void HandleLobbyChanged(IReadOnlyDictionary<int, LanPlayerInfo> players)
	{
		UpdateStateBadge();
	}

	private void HandleNetworkStateChanged(LanSessionState state)
	{
		UpdateStateBadge();
	}

	private void UnbindCharacterEvents()
	{
		if (_character is null)
		{
			return;
		}

		_character.OnStatsChanged -= HandleCharacterChanged;
		_character.OnDefeated -= HandleCharacterChanged;
		_character.OnHandCardsChanged -= HandleCharacterChanged;
		_character.OnEquipmentChanged -= HandleCharacterChanged;
	}
}
