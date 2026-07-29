using System;
using System.Collections.Generic;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using CiyuanSha.Settings;
using Godot;

namespace CiyuanSha.UI;

public partial class CyberMatchHudPanel : Control
{
	private const string MatchBackdropPath = "res://Assets/UI/InkBattle/ink_courtyard_match_v1.png";

	private Label? _phaseBannerLabel;
	private Label? _tableHintLabel;
	private Label? _drawPileLabel;
	private Label? _discardPileLabel;
	private Panel? _centralStage;
	private ColorRect? _ruleFlash;
	private Label? _ruleFxLabel;
	private readonly List<ColorRect> _damageStrokes = new();
	private TurnPhase _lastPresentedPhase = (TurnPhase)(-1);
	private bool _lastResponseVisible;
	private bool _hasPresentedStageState;

	public override void _Ready()
	{
		CyberStyle.ApplyRootTheme(this);
		BuildBackdrop();
		BindGameManager();
		if (GameUserSettings.Instance is not null)
		{
			GameUserSettings.Instance.OnChanged += HandlePresentationSettingsChanged;
		}

		RefreshTableState();
		Modulate = new Color(1f, 1f, 1f, 0f);
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate", Colors.White, 0.32f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}

	public override void _ExitTree()
	{
		if (GameManager.Instance is not null)
		{
			GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
			GameManager.Instance.OnStateChanged -= HandleStateChanged;
			GameManager.Instance.OnResponseWindowChanged -= HandleStateChanged;
			GameManager.Instance.OnPresentationEvent -= HandlePresentationEvent;
		}

		if (GameUserSettings.Instance is not null)
		{
			GameUserSettings.Instance.OnChanged -= HandlePresentationSettingsChanged;
		}
	}

	private void BuildBackdrop()
	{
		if (GetNodeOrNull<ColorRect>("InkBackdrop") is not null)
		{
			return;
		}

		ColorRect backdrop = new()
		{
			Name = "InkBackdrop",
			MouseFilter = MouseFilterEnum.Ignore,
			Color = CyberStyle.Ink,
			ZIndex = -122
		};
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);
		MoveChild(backdrop, 0);

		BuildMoonlitCourtyardLayer();
		BuildCourtyardSilhouetteLayer();
		BuildInkWashLayers();
		BuildCentralTableStage();
		BuildRuleFxLayer();
	}

	private void BuildMoonlitCourtyardLayer()
	{
		TextureRect courtyard = new()
		{
			Name = "InkBattleBackground",
			MouseFilter = MouseFilterEnum.Ignore,
			Texture = GD.Load<Texture2D>(MatchBackdropPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			Modulate = new Color(0.74f, 0.77f, 0.78f, 1f),
			ZIndex = -121
		};
		courtyard.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(courtyard);
		MoveChild(courtyard, 1);

		ColorRect atmosphere = new()
		{
			Name = "BattleAtmosphereTint",
			MouseFilter = MouseFilterEnum.Ignore,
			Color = new Color(0.008f, 0.014f, 0.018f, 0.26f),
			ZIndex = -120
		};
		atmosphere.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(atmosphere);
		MoveChild(atmosphere, 2);

		Label titleMark = new()
		{
			Name = "InkTitleMark",
			Text = "次元杀",
			MouseFilter = MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Modulate = new Color(CyberStyle.Paper.R, CyberStyle.Paper.G, CyberStyle.Paper.B, 0.42f),
			ZIndex = 2
		};
		titleMark.AnchorLeft = 0.02f;
		titleMark.AnchorRight = 0.18f;
		titleMark.AnchorTop = 0.02f;
		titleMark.AnchorBottom = 0.10f;
		CyberStyle.ApplyLabel(titleMark, title: true);
		titleMark.AddThemeFontSizeOverride("font_size", 38);
		AddChild(titleMark);
		MoveChild(titleMark, 3);
	}

	private void BuildCourtyardSilhouetteLayer()
	{
		AddBackdropRect("TopVignette", 0f, 0f, 1f, 0.10f, new Color(0.004f, 0.005f, 0.005f, 0.60f), 3);
		AddBackdropRect("LeftVignette", 0f, 0f, 0.10f, 1f, new Color(0.004f, 0.005f, 0.005f, 0.52f), 3);
		AddBackdropRect("RightVignette", 0.90f, 0f, 1f, 1f, new Color(0.004f, 0.005f, 0.005f, 0.58f), 3);
		AddBackdropRect("BottomVignette", 0f, 0.76f, 1f, 1f, new Color(0.004f, 0.005f, 0.005f, 0.46f), 3);
		AddBackdropRect("TableFocus", 0.27f, 0.43f, 0.73f, 0.64f, new Color(0.37f, 0.30f, 0.20f, 0.055f), 3);
	}

	private void BuildInkWashLayers()
	{
		System.Random random = new(5182026);
		for (int index = 0; index < 8; index++)
		{
			float width = 0.10f + (float)random.NextDouble() * 0.28f;
			float left = (float)random.NextDouble() * (1f - width);
			float top = 0.42f + (float)random.NextDouble() * 0.35f;
			float height = 0.018f + (float)random.NextDouble() * 0.055f;
			float alpha = 0.035f + (float)random.NextDouble() * 0.11f;
			Color color = index % 3 == 0
				? new Color(CyberStyle.PaperDark.R, CyberStyle.PaperDark.G, CyberStyle.PaperDark.B, alpha)
				: new Color(0f, 0f, 0f, alpha);
			ColorRect stroke = AddBackdropRect($"InkWash{index:00}", left, top, left + width, Math.Min(0.92f, top + height), color, 3);
			stroke.RotationDegrees = -3f + (float)random.NextDouble() * 6f;
			stroke.PivotOffset = stroke.Size / 2f;
		}
	}

	private ColorRect AddBackdropRect(string name, float left, float top, float right, float bottom, Color color, int childIndex)
	{
		ColorRect rect = new()
		{
			Name = name,
			MouseFilter = MouseFilterEnum.Ignore,
			Color = color,
			ZIndex = -118
		};
		rect.AnchorLeft = left;
		rect.AnchorRight = right;
		rect.AnchorTop = top;
		rect.AnchorBottom = bottom;
		AddChild(rect);
		MoveChild(rect, Math.Min(childIndex, GetChildCount() - 1));
		return rect;
	}

	private void BuildCentralTableStage()
	{
		_centralStage = new Panel
		{
			Name = "CentralDecisionStage",
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 4
		};
		Panel stage = _centralStage;
		stage.AnchorLeft = 0.36f;
		stage.AnchorRight = 0.64f;
		stage.AnchorTop = 0.34f;
		stage.AnchorBottom = 0.58f;
		CyberStyle.ApplyPanel(stage, CyberPanelKind.Recessed);
		AddChild(stage);
		MoveChild(stage, 4);

		Label stageSeal = new()
		{
			Name = "StageSealLabel",
			Text = "次元杀",
			MouseFilter = MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Modulate = new Color(CyberStyle.Paper.R, CyberStyle.Paper.G, CyberStyle.Paper.B, 0.08f)
		};
		stageSeal.SetAnchorsPreset(LayoutPreset.FullRect);
		CyberStyle.ApplyLabel(stageSeal, title: true);
		stageSeal.AddThemeFontSizeOverride("font_size", 64);
		stage.AddChild(stageSeal);

		VBoxContainer box = new()
		{
			Name = "StageVBox",
			MouseFilter = MouseFilterEnum.Ignore
		};
		box.SetAnchorsPreset(LayoutPreset.FullRect);
		box.OffsetLeft = 18f;
		box.OffsetTop = 12f;
		box.OffsetRight = -18f;
		box.OffsetBottom = -12f;
		box.AddThemeConstantOverride("separation", 8);
		stage.AddChild(box);

		_phaseBannerLabel = new Label
		{
			Name = "PhaseBannerLabel",
			Text = "出牌阶段",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		CyberStyle.ApplyLabel(_phaseBannerLabel, title: true);
		_phaseBannerLabel.AddThemeFontSizeOverride("font_size", 30);
		box.AddChild(_phaseBannerLabel);

		_tableHintLabel = new Label
		{
			Name = "TableHintLabel",
			Text = "选牌、定目标，然后让牌局结算。",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		CyberStyle.ApplyLabel(_tableHintLabel, color: CyberStyle.MutedText);
		box.AddChild(_tableHintLabel);

		HBoxContainer pileRow = new()
		{
			Name = "PileRow",
			Alignment = BoxContainer.AlignmentMode.Center
		};
		pileRow.AddThemeConstantOverride("separation", 14);
		box.AddChild(pileRow);

		_drawPileLabel = CreatePilePlaque("牌堆\n--");
		_discardPileLabel = CreatePilePlaque("弃牌\n--");
		pileRow.AddChild(_drawPileLabel);
		pileRow.AddChild(_discardPileLabel);
	}

	private void BuildRuleFxLayer()
	{
		BattleFxDirector battleFxDirector = new()
		{
			Name = "BattleFxDirector",
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 70
		};
		battleFxDirector.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(battleFxDirector);

		Control layer = new()
		{
			Name = "InkRuleFxLayer",
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 80
		};
		layer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(layer);

		_ruleFlash = new ColorRect
		{
			Name = "RuleFlash",
			MouseFilter = MouseFilterEnum.Ignore,
			Color = Colors.Transparent,
			Visible = false
		};
		_ruleFlash.SetAnchorsPreset(LayoutPreset.FullRect);
		layer.AddChild(_ruleFlash);

		_ruleFxLabel = new Label
		{
			Name = "RuleFxLabel",
			MouseFilter = MouseFilterEnum.Ignore,
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Visible = false
		};
		_ruleFxLabel.SetAnchorsPreset(LayoutPreset.FullRect);
		CyberStyle.ApplyLabel(_ruleFxLabel, title: true);
		_ruleFxLabel.AddThemeFontSizeOverride("font_size", 46);
		layer.AddChild(_ruleFxLabel);

		for (int index = 0; index < 5; index++)
		{
			ColorRect stroke = new()
			{
				Name = $"DamageStroke{index + 1}",
				MouseFilter = MouseFilterEnum.Ignore,
				Color = Colors.Transparent,
				Visible = false
			};
			stroke.AnchorLeft = 0.22f;
			stroke.AnchorRight = 0.78f;
			stroke.AnchorTop = 0.50f;
			stroke.AnchorBottom = 0.50f;
			stroke.OffsetLeft = 0f;
			stroke.OffsetRight = 0f;
			stroke.OffsetTop = -4f;
			stroke.OffsetBottom = 4f;
			stroke.PivotOffset = new Vector2(0.5f, 0.5f);
			layer.AddChild(stroke);
			_damageStrokes.Add(stroke);
		}
	}

	private static Label CreatePilePlaque(string text)
	{
		Label label = new()
		{
			Text = text,
			CustomMinimumSize = new Vector2(96f, 54f),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		CyberStyle.ApplyLabel(label, color: CyberStyle.Paper);
		label.AddThemeStyleboxOverride("normal", MakePlaqueBox());
		return label;
	}

	private static StyleBoxFlat MakePlaqueBox()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.055f, 0.047f, 0.037f, 0.92f),
			BorderColor = CyberStyle.Gold.Darkened(0.28f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3
		};
	}

	private void BindGameManager()
	{
		if (GameManager.Instance is null)
		{
			return;
		}

		GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
		GameManager.Instance.OnStateChanged += HandleStateChanged;
		GameManager.Instance.OnResponseWindowChanged += HandleStateChanged;
		GameManager.Instance.OnPresentationEvent += HandlePresentationEvent;
	}

	private void HandlePhaseChanged(TurnPhase phase)
	{
		RefreshTableState();
	}

	private void HandleStateChanged()
	{
		RefreshTableState();
	}

	private void HandlePresentationEvent(NetworkPresentationEvent context)
	{
		if (!Enum.TryParse(context.EventType, true, out RuleEventType eventType))
		{
			return;
		}

		if (eventType == RuleEventType.DamageApplied && context.Value > 0)
		{
			DamageType damageType = Enum.TryParse(context.DamageType, true, out DamageType parsedDamageType)
				? parsedDamageType
				: DamageType.Physical;
			PlayRuleFlash(GetDamageFxColor(damageType), BuildDamageFxText(context, damageType), playDamageStrokes: true);
			return;
		}

		if (eventType == RuleEventType.Healed && context.Value > 0)
		{
			string targetName = context.TargetName;
			string text = string.IsNullOrWhiteSpace(targetName)
				? $"+{context.Value} 治疗"
				: $"{targetName} +{context.Value}";
			PlayRuleFlash(new Color(CyberStyle.NeonJade.R, CyberStyle.NeonJade.G, CyberStyle.NeonJade.B, 0.24f), text);
			return;
		}

		if (eventType == RuleEventType.ResponseUsed)
		{
			ResponseWindowKind responseKind = Enum.TryParse(context.ResponseKind, true, out ResponseWindowKind parsedResponseKind)
				? parsedResponseKind
				: ResponseWindowKind.None;
			PlayRuleFlash(new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.18f), FormatResponseFxText(responseKind));
		}
	}

	private void RefreshTableState()
	{
		if (GameManager.Instance is null)
		{
			return;
		}

		GameManager game = GameManager.Instance;
		bool responseVisible = game.HasPendingResponseWindow;
		if (!_hasPresentedStageState
			|| _lastPresentedPhase != game.CurrentPhase
			|| _lastResponseVisible != responseVisible)
		{
			PlayStageTransition(responseVisible);
			_lastPresentedPhase = game.CurrentPhase;
			_lastResponseVisible = responseVisible;
			_hasPresentedStageState = true;
		}

		if (_phaseBannerLabel is not null)
		{
			_phaseBannerLabel.Text = game.HasPendingResponseWindow
				? "需要响应"
				: FormatPhaseBanner(game.CurrentPhase);
			_phaseBannerLabel.Modulate = game.HasPendingResponseWindow
				? new Color(1f, 0.72f, 0.58f, 1f)
				: Colors.White;
		}

		if (_tableHintLabel is not null)
		{
			_tableHintLabel.Text = game.HasPendingResponseWindow
				? BattleLogTextLocalizer.Localize(game.PendingResponsePrompt)
				: game.IsAwaitingPlayerInput
					? "请选择手牌、发动技能，或结束出牌。"
					: game.IsAwaitingDiscardInput
						? "弃牌至当前手牌上限。"
						: "牌桌正在结算动作。";
		}

		if (_drawPileLabel is not null)
		{
			_drawPileLabel.Text = $"牌堆\n{game.DrawPileCount:D2}";
		}

		if (_discardPileLabel is not null)
		{
			_discardPileLabel.Text = $"弃牌\n{game.DiscardPileCount:D2}";
		}
	}

	private void PlayStageTransition(bool responseVisible)
	{
		if (_centralStage is null || !IsInsideTree())
		{
			return;
		}

		_centralStage.PivotOffset = _centralStage.Size / 2f;
		_centralStage.Scale = new Vector2(0.985f, 0.985f);
		_centralStage.Modulate = responseVisible
			? new Color(1f, 0.70f, 0.56f, 1f)
			: new Color(1f, 0.88f, 0.68f, 1f);
		GameUserSettings? settings = GameUserSettings.Instance;
		if (settings?.VisualEffectsEnabled == false || settings?.ReducedMotion == true)
		{
			_centralStage.Scale = Vector2.One;
			_centralStage.Modulate = Colors.White;
			return;
		}

		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_centralStage, "scale", Vector2.One, 0.24f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_centralStage, "modulate", Colors.White, 0.34f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}

	private static string FormatPhaseBanner(TurnPhase phase)
	{
		return phase switch
		{
			TurnPhase.TurnStart => "回合开始",
			TurnPhase.DrawPhase => "摸牌阶段",
			TurnPhase.PlayPhase => "出牌阶段",
			TurnPhase.DiscardPhase => "弃牌阶段",
			_ => phase.ToString().ToUpperInvariant()
		};
	}

	private void PlayRuleFlash(Color color, string text, bool playDamageStrokes = false)
	{
		if (_ruleFlash is null || _ruleFxLabel is null || !IsInsideTree())
		{
			return;
		}

		GameUserSettings? settings = GameUserSettings.Instance;
		bool visualEffectsEnabled = settings?.VisualEffectsEnabled != false;
		bool screenFlashEnabled = visualEffectsEnabled && settings?.ScreenFlashEnabled != false;
		bool reducedMotion = settings?.ReducedMotion == true;
		BattleFxQuality quality = settings?.EffectQuality ?? BattleFxQuality.Balanced;

		_ruleFlash.Color = color;
		_ruleFlash.Modulate = new Color(1f, 1f, 1f, 0f);
		_ruleFlash.Visible = screenFlashEnabled;
		_ruleFxLabel.Text = text;
		_ruleFxLabel.Modulate = new Color(1f, 1f, 1f, 0f);
		_ruleFxLabel.Scale = visualEffectsEnabled && !reducedMotion ? new Vector2(0.86f, 0.86f) : Vector2.One;
		_ruleFxLabel.PivotOffset = Size / 2f;
		_ruleFxLabel.Visible = true;
		if (playDamageStrokes && visualEffectsEnabled && !reducedMotion && quality != BattleFxQuality.Low)
		{
			PlayDamageStrokes(color);
		}

		Tween tween = CreateTween();
		tween.SetParallel(true);
		if (screenFlashEnabled)
		{
			tween.TweenProperty(_ruleFlash, "modulate", new Color(1f, 1f, 1f, 0.92f), reducedMotion ? 0.03f : 0.06f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			tween.TweenProperty(_ruleFlash, "modulate", new Color(1f, 1f, 1f, 0f), reducedMotion ? 0.14f : 0.34f)
				.SetDelay(reducedMotion ? 0.04f : 0.08f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}

		tween.TweenProperty(_ruleFxLabel, "modulate", Colors.White, reducedMotion ? 0.04f : 0.08f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		if (visualEffectsEnabled && !reducedMotion)
		{
			tween.TweenProperty(_ruleFxLabel, "scale", Vector2.One, 0.16f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out);
		}

		tween.TweenProperty(_ruleFxLabel, "modulate", new Color(1f, 1f, 1f, 0f), reducedMotion ? 0.16f : 0.24f)
			.SetDelay(reducedMotion ? 0.16f : 0.24f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.In);
		tween.Finished += () =>
		{
			if (_ruleFlash is not null)
			{
				_ruleFlash.Visible = false;
			}

			if (_ruleFxLabel is not null)
			{
				_ruleFxLabel.Visible = false;
			}
		};
	}

	private void HandlePresentationSettingsChanged()
	{
		GameUserSettings? settings = GameUserSettings.Instance;
		if (settings?.VisualEffectsEnabled != false)
		{
			return;
		}

		if (_ruleFlash is not null)
		{
			_ruleFlash.Visible = false;
		}

		foreach (ColorRect stroke in _damageStrokes)
		{
			stroke.Visible = false;
		}
	}

	private void PlayDamageStrokes(Color color)
	{
		if (_damageStrokes.Count == 0 || !IsInsideTree())
		{
			return;
		}

		float[] rotations = { -18f, -8f, 6f, 15f, 0f };
		float[] yOffsets = { -54f, -28f, 12f, 40f, -4f };
		for (int index = 0; index < _damageStrokes.Count; index++)
		{
			ColorRect stroke = _damageStrokes[index];
			float yOffset = yOffsets[index % yOffsets.Length];
			stroke.Visible = true;
			stroke.Color = new Color(color.R, color.G, color.B, 0.72f);
			stroke.RotationDegrees = rotations[index % rotations.Length];
			stroke.OffsetTop = yOffset - 4f;
			stroke.OffsetBottom = yOffset + 4f;
			stroke.Modulate = new Color(1f, 1f, 1f, 0f);
			stroke.Scale = new Vector2(0.10f, 1f);

			Tween tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(stroke, "scale", Vector2.One, 0.10f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			tween.TweenProperty(stroke, "modulate", Colors.White, 0.05f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			tween.TweenProperty(stroke, "modulate", new Color(1f, 1f, 1f, 0f), 0.22f)
				.SetDelay(0.10f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.In);
			tween.Finished += () => stroke.Visible = false;
		}
	}

	private static Color GetDamageFxColor(DamageType damageType)
	{
		return damageType switch
		{
			DamageType.Fire => new Color(CyberStyle.Fire.R, CyberStyle.Fire.G, CyberStyle.Fire.B, 0.42f),
			DamageType.Thunder => new Color(CyberStyle.Thunder.R, CyberStyle.Thunder.G, CyberStyle.Thunder.B, 0.40f),
			_ => new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.34f)
		};
	}

	private static string BuildDamageFxText(NetworkPresentationEvent context, DamageType damageType)
	{
		string targetName = string.IsNullOrWhiteSpace(context.TargetName) ? "目标" : context.TargetName;
		string damageName = damageType switch
		{
			DamageType.Fire => "火焰伤害",
			DamageType.Thunder => "雷电伤害",
			_ => "伤害"
		};
		return $"{targetName} -{context.Value} {damageName}";
	}

	private static string FormatResponseFxText(ResponseWindowKind kind)
	{
		return kind switch
		{
			ResponseWindowKind.Dodge => "闪避",
			ResponseWindowKind.Peach => "救援",
			ResponseWindowKind.Slash => "杀响应",
			ResponseWindowKind.Nullification => "无懈",
			_ => "响应"
		};
	}

}
