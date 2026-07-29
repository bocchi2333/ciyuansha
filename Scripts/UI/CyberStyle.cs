using CiyuanSha.Gameplay.Cards;
using Godot;

namespace CiyuanSha.UI;

public enum CyberButtonKind
{
	Neutral,
	Primary,
	Action,
	Danger,
	Disabled,
	Quiet
}

public enum CyberPanelKind
{
	Default,
	Strong,
	Alert,
	Card,
	Overlay,
	Recessed
}

/// <summary>
/// Commercial ink-wuxia UI system. The historical name remains as a stable API for existing panels.
/// </summary>
public static class CyberStyle
{
	public const string BodyFontPath = "res://Assets/Fonts/SourceHanSerifCN-Regular.otf";
	public const string DisplayFontPath = "res://Assets/Fonts/LXGWWenKai-Medium.ttf";

	public static readonly Color Ink = new(0.018f, 0.020f, 0.019f, 0.985f);
	public static readonly Color InkSoft = new(0.050f, 0.052f, 0.048f, 0.95f);
	public static readonly Color Metal = new(0.125f, 0.108f, 0.082f, 0.98f);
	public static readonly Color Gold = new(0.745f, 0.570f, 0.325f, 1f);
	public static readonly Color GoldBright = new(0.930f, 0.760f, 0.470f, 1f);
	public static readonly Color NeonJade = new(0.300f, 0.535f, 0.420f, 1f);
	public static readonly Color Vermilion = new(0.710f, 0.125f, 0.085f, 1f);
	public static readonly Color Fire = new(0.915f, 0.315f, 0.095f, 1f);
	public static readonly Color Thunder = new(0.510f, 0.355f, 0.760f, 1f);
	public static readonly Color Paper = new(0.825f, 0.750f, 0.620f, 1f);
	public static readonly Color PaperBright = new(0.955f, 0.895f, 0.760f, 1f);
	public static readonly Color PaperDark = new(0.450f, 0.360f, 0.245f, 1f);
	public static readonly Color MutedText = new(0.545f, 0.505f, 0.425f, 1f);
	public static readonly Color BloodInk = new(0.235f, 0.026f, 0.020f, 0.98f);

	private static Font? _bodyFont;
	private static Font? _displayFont;

	public static Font? BodyFont => _bodyFont ??= GD.Load<Font>(BodyFontPath);

	public static Font? DisplayFont => _displayFont ??= GD.Load<Font>(DisplayFontPath);

	public static void ApplyRootTheme(Control? root)
	{
		if (root is null)
		{
			return;
		}

		Theme theme = new()
		{
			DefaultFont = BodyFont,
			DefaultFontSize = 15
		};
		root.Theme = theme;
	}

	public static void ApplyPanel(Control? control, CyberPanelKind kind = CyberPanelKind.Default)
	{
		if (control is null)
		{
			return;
		}

		Color border = kind switch
		{
			CyberPanelKind.Strong => Gold.Darkened(0.08f),
			CyberPanelKind.Alert => Vermilion,
			CyberPanelKind.Card => Gold,
			CyberPanelKind.Overlay => GoldBright.Darkened(0.22f),
			CyberPanelKind.Recessed => new Color(0.20f, 0.175f, 0.135f, 0.92f),
			_ => new Color(0.30f, 0.245f, 0.165f, 0.95f)
		};
		Color fill = kind switch
		{
			CyberPanelKind.Strong => new Color(0.028f, 0.027f, 0.024f, 0.965f),
			CyberPanelKind.Alert => new Color(0.082f, 0.022f, 0.018f, 0.975f),
			CyberPanelKind.Card => new Color(0.030f, 0.028f, 0.024f, 0.985f),
			CyberPanelKind.Overlay => new Color(0.018f, 0.018f, 0.017f, 0.985f),
			CyberPanelKind.Recessed => new Color(0.014f, 0.014f, 0.013f, 0.87f),
			_ => new Color(0.022f, 0.022f, 0.020f, 0.935f)
		};
		int borderWidth = kind switch
		{
			CyberPanelKind.Card => 2,
			CyberPanelKind.Alert => 2,
			CyberPanelKind.Overlay => 2,
			_ => 1
		};

		control.AddThemeStyleboxOverride("panel", MakePanelBox(fill, border, borderWidth, kind));
	}

	public static void ApplyButton(Button? button, CyberButtonKind kind = CyberButtonKind.Neutral)
	{
		if (button is null)
		{
			return;
		}

		Color accent = kind switch
		{
			CyberButtonKind.Primary => GoldBright,
			CyberButtonKind.Action => NeonJade.Lightened(0.15f),
			CyberButtonKind.Danger => Vermilion,
			CyberButtonKind.Disabled => MutedText,
			CyberButtonKind.Quiet => new Color(0.36f, 0.31f, 0.24f, 1f),
			_ => Gold.Darkened(0.15f)
		};
		Color fill = kind switch
		{
			CyberButtonKind.Primary => new Color(0.155f, 0.105f, 0.045f, 0.99f),
			CyberButtonKind.Action => new Color(0.040f, 0.120f, 0.088f, 0.99f),
			CyberButtonKind.Danger => BloodInk,
			CyberButtonKind.Quiet => new Color(0.025f, 0.024f, 0.022f, 0.88f),
			_ => new Color(0.052f, 0.047f, 0.038f, 0.98f)
		};

		button.AddThemeStyleboxOverride("normal", MakeButtonBox(fill, accent.Darkened(0.22f), 1));
		button.AddThemeStyleboxOverride("hover", MakeButtonBox(fill.Lightened(0.055f), accent, 2));
		button.AddThemeStyleboxOverride("pressed", MakeButtonBox(fill.Darkened(0.10f), accent.Lightened(0.12f), 2));
		button.AddThemeStyleboxOverride("focus", MakeButtonBox(fill.Lightened(0.025f), GoldBright, 2));
		button.AddThemeStyleboxOverride("disabled", MakeButtonBox(new Color(0.025f, 0.024f, 0.022f, 0.78f), new Color(0.13f, 0.12f, 0.105f, 0.84f), 1));
		button.AddThemeColorOverride("font_color", kind == CyberButtonKind.Danger ? new Color(0.98f, 0.75f, 0.61f, 1f) : Colors.White);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", accent.Lightened(0.24f));
		button.AddThemeColorOverride("font_focus_color", PaperBright);
		button.AddThemeColorOverride("font_disabled_color", MutedText.Darkened(0.20f));
		button.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.88f));
		button.AddThemeConstantOverride("outline_size", 1);
		ApplyFont(button, BodyFont);
		button.AddThemeFontSizeOverride("font_size", kind == CyberButtonKind.Primary ? 17 : 15);
		button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, Mathf.Max(button.CustomMinimumSize.Y, 40f));
	}

	public static void ApplyLineEdit(LineEdit? lineEdit)
	{
		if (lineEdit is null)
		{
			return;
		}

		lineEdit.AddThemeStyleboxOverride("normal", MakeInputBox(new Color(0.014f, 0.014f, 0.013f, 0.94f), new Color(0.25f, 0.21f, 0.15f, 1f), 1));
		lineEdit.AddThemeStyleboxOverride("focus", MakeInputBox(new Color(0.024f, 0.022f, 0.018f, 0.98f), Gold, 2));
		lineEdit.AddThemeStyleboxOverride("read_only", MakeInputBox(new Color(0.020f, 0.019f, 0.017f, 0.78f), new Color(0.15f, 0.14f, 0.12f, 0.8f), 1));
		lineEdit.AddThemeColorOverride("font_color", PaperBright);
		lineEdit.AddThemeColorOverride("font_placeholder_color", MutedText.Darkened(0.05f));
		lineEdit.AddThemeColorOverride("caret_color", GoldBright);
		lineEdit.AddThemeColorOverride("selection_color", new Color(Gold.R, Gold.G, Gold.B, 0.30f));
		ApplyFont(lineEdit, BodyFont);
		lineEdit.AddThemeFontSizeOverride("font_size", 15);
		lineEdit.CustomMinimumSize = new Vector2(lineEdit.CustomMinimumSize.X, Mathf.Max(lineEdit.CustomMinimumSize.Y, 42f));
	}

	public static void ApplyOptionButton(OptionButton? optionButton)
	{
		if (optionButton is null)
		{
			return;
		}

		ApplyButton(optionButton, CyberButtonKind.Neutral);
		optionButton.AddThemeColorOverride("font_color", PaperBright);
		optionButton.AddThemeColorOverride("font_hover_color", Colors.White);
	}

	public static void ApplySlider(Slider? slider)
	{
		if (slider is null)
		{
			return;
		}

		slider.AddThemeStyleboxOverride("slider", new StyleBoxFlat
		{
			BgColor = new Color(0.020f, 0.019f, 0.017f, 0.96f),
			BorderColor = new Color(0.25f, 0.21f, 0.15f, 0.92f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
			ContentMarginTop = 5,
			ContentMarginBottom = 5
		});
		slider.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat
		{
			BgColor = Gold.Darkened(0.22f),
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3
		});
		slider.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxFlat
		{
			BgColor = GoldBright,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3
		});
	}

	public static void ApplyLabel(Label? label, bool title = false, Color? color = null)
	{
		if (label is null)
		{
			return;
		}

		label.AddThemeColorOverride("font_color", color ?? (title ? GoldBright : Paper));
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.92f));
		label.AddThemeConstantOverride("shadow_offset_x", 1);
		label.AddThemeConstantOverride("shadow_offset_y", title ? 3 : 2);
		label.AddThemeConstantOverride("shadow_outline_size", title ? 2 : 1);
		ApplyFont(label, title ? DisplayFont : BodyFont);
		label.AddThemeFontSizeOverride("font_size", title ? 24 : 15);
	}

	public static void ApplyRichText(RichTextLabel? label)
	{
		if (label is null)
		{
			return;
		}

		label.AddThemeColorOverride("default_color", Paper);
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.72f));
		ApplyFont(label, BodyFont, "normal_font");
		ApplyFont(label, DisplayFont, "bold_font");
		label.AddThemeFontSizeOverride("normal_font_size", 14);
		label.AddThemeFontSizeOverride("bold_font_size", 15);
		label.ScrollFollowing = true;
	}

	public static void ApplyCardButton(Button button, CardInstance card, bool selected, bool disabled)
	{
		Color accent = GetCardAccent(card.CardType);
		Color fill = GetCardFill(card.CardType);
		Color textColor = selected
			? new Color(0.12f, 0.055f, 0.025f, 1f)
			: CardRules.IsRedSuit(card.Suit)
				? new Color(0.48f, 0.055f, 0.038f, 1f)
				: new Color(0.080f, 0.060f, 0.035f, 1f);
		button.AddThemeStyleboxOverride("normal", MakeCardBox(fill, selected ? GoldBright : accent, selected ? 3 : 2));
		button.AddThemeStyleboxOverride("hover", MakeCardBox(fill.Lightened(0.045f), selected ? GoldBright : accent.Lightened(0.12f), 3));
		button.AddThemeStyleboxOverride("pressed", MakeCardBox(fill.Darkened(0.055f), GoldBright, 3));
		button.AddThemeStyleboxOverride("focus", MakeCardBox(fill.Lightened(0.025f), GoldBright, 3));
		button.AddThemeStyleboxOverride("disabled", MakeCardBox(new Color(0.25f, 0.22f, 0.17f, 0.70f), new Color(0.16f, 0.14f, 0.11f, 0.84f), 1));
		button.AddThemeColorOverride("font_color", textColor);
		button.AddThemeColorOverride("font_hover_color", new Color(0.03f, 0.02f, 0.01f, 1f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.06f, 0.025f, 0.012f, 1f));
		button.AddThemeColorOverride("font_disabled_color", new Color(0.20f, 0.17f, 0.13f, 0.82f));
		button.AddThemeColorOverride("font_outline_color", new Color(0.94f, 0.84f, 0.64f, selected ? 0.28f : 0.14f));
		button.AddThemeConstantOverride("outline_size", selected ? 2 : 1);
		ApplyFont(button, DisplayFont);
		button.AddThemeFontSizeOverride("font_size", 20);
		button.Modulate = disabled ? new Color(0.58f, 0.57f, 0.53f, 0.72f) : Colors.White;
		button.Scale = selected ? new Vector2(1.055f, 1.055f) : Vector2.One;
		button.PivotOffset = button.CustomMinimumSize / 2f;
	}

	public static void ApplyVirtualCardButton(Button button, CardType cardType, bool selected, bool disabled)
	{
		Color accent = GetCardAccent(cardType);
		Color fill = GetCardFill(cardType);
		button.AddThemeStyleboxOverride("normal", MakeCardBox(fill.Darkened(0.04f), selected ? GoldBright : accent, selected ? 3 : 2));
		button.AddThemeStyleboxOverride("hover", MakeCardBox(fill.Lightened(0.035f), selected ? GoldBright : accent.Lightened(0.12f), 3));
		button.AddThemeStyleboxOverride("pressed", MakeCardBox(fill.Darkened(0.07f), GoldBright, 3));
		button.AddThemeStyleboxOverride("focus", MakeCardBox(fill.Lightened(0.02f), GoldBright, 3));
		button.AddThemeStyleboxOverride("disabled", MakeCardBox(new Color(0.25f, 0.22f, 0.17f, 0.70f), new Color(0.16f, 0.14f, 0.11f, 0.84f), 1));
		button.AddThemeColorOverride("font_color", selected ? new Color(0.10f, 0.045f, 0.02f, 1f) : new Color(0.085f, 0.055f, 0.028f, 1f));
		button.AddThemeColorOverride("font_hover_color", Colors.Black);
		button.AddThemeColorOverride("font_pressed_color", new Color(0.06f, 0.025f, 0.012f, 1f));
		button.AddThemeColorOverride("font_disabled_color", new Color(0.20f, 0.17f, 0.13f, 0.82f));
		button.AddThemeColorOverride("font_outline_color", new Color(0.94f, 0.84f, 0.64f, selected ? 0.32f : 0.18f));
		button.AddThemeConstantOverride("outline_size", selected ? 2 : 1);
		ApplyFont(button, DisplayFont);
		button.AddThemeFontSizeOverride("font_size", 19);
		button.Modulate = disabled ? new Color(0.58f, 0.57f, 0.53f, 0.72f) : Colors.White;
		button.Scale = selected ? new Vector2(1.055f, 1.055f) : Vector2.One;
		button.PivotOffset = button.CustomMinimumSize / 2f;
	}

	public static void AttachHoverLift(Button button, float liftScale = 1.045f)
	{
		button.MouseEntered += () =>
		{
			if (button.Disabled || !button.IsInsideTree())
			{
				return;
			}

			button.PivotOffset = button.Size / 2f;
			Tween tween = button.CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(button, "scale", new Vector2(liftScale, liftScale), 0.11f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			tween.TweenProperty(button, "modulate", Colors.White, 0.08f);
		};
		button.MouseExited += () =>
		{
			if (!button.IsInsideTree() || button.ButtonPressed)
			{
				return;
			}

			Tween tween = button.CreateTween();
			tween.TweenProperty(button, "scale", Vector2.One, 0.10f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		};
	}

	public static Color GetCardAccent(CardType cardType)
	{
		if (CardRules.IsEquipment(cardType))
		{
			return Gold.Darkened(0.06f);
		}

		return cardType switch
		{
			CardType.FireSlash or CardType.FireAttack => Fire,
			CardType.ThunderSlash or CardType.Lightning => Thunder,
			CardType.Slash or CardType.Dodge or CardType.Peach or CardType.Wine => new Color(0.32f, 0.22f, 0.12f, 1f),
			CardType.Barbarians or CardType.ArrowBarrage or CardType.Duel => Vermilion.Darkened(0.03f),
			CardType.Nullification or CardType.ExNihilo or CardType.Harvest => new Color(0.25f, 0.39f, 0.33f, 1f),
			_ => PaperDark
		};
	}

	public static StyleBoxFlat MakePanelBox(Color fill, Color border, int borderWidth, CyberPanelKind kind = CyberPanelKind.Default)
	{
		float shadowAlpha = kind is CyberPanelKind.Overlay or CyberPanelKind.Card ? 0.82f : 0.64f;
		int shadowSize = kind is CyberPanelKind.Overlay or CyberPanelKind.Card ? 22 : 14;
		return new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomRight = 3,
			CornerRadiusBottomLeft = 3,
			ShadowColor = new Color(0f, 0f, 0f, shadowAlpha),
			ShadowSize = shadowSize,
			ContentMarginLeft = kind == CyberPanelKind.Card ? 10 : 14,
			ContentMarginRight = kind == CyberPanelKind.Card ? 10 : 14,
			ContentMarginTop = kind == CyberPanelKind.Card ? 10 : 12,
			ContentMarginBottom = kind == CyberPanelKind.Card ? 10 : 12,
			AntiAliasing = true
		};
	}

	private static StyleBoxFlat MakeButtonBox(Color fill, Color border, int borderWidth)
	{
		return new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
			ShadowColor = new Color(0f, 0f, 0f, 0.42f),
			ShadowSize = borderWidth > 1 ? 8 : 4,
			ContentMarginLeft = 16,
			ContentMarginRight = 16,
			ContentMarginTop = 9,
			ContentMarginBottom = 9,
			AntiAliasing = true
		};
	}

	private static StyleBoxFlat MakeInputBox(Color fill, Color border, int borderWidth)
	{
		return new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
			AntiAliasing = true
		};
	}

	private static StyleBoxFlat MakeCardBox(Color fill, Color border, int borderWidth)
	{
		return new StyleBoxFlat
		{
			BgColor = fill,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ShadowColor = new Color(0f, 0f, 0f, 0.62f),
			ShadowSize = borderWidth > 1 ? 14 : 5,
			ContentMarginLeft = 9,
			ContentMarginRight = 9,
			ContentMarginTop = 11,
			ContentMarginBottom = 11,
			AntiAliasing = true
		};
	}

	private static Color GetCardFill(CardType cardType)
	{
		if (CardRules.IsEquipment(cardType))
		{
			return new Color(0.675f, 0.575f, 0.415f, 0.995f);
		}

		return cardType switch
		{
			CardType.FireSlash or CardType.FireAttack => new Color(0.735f, 0.535f, 0.355f, 0.995f),
			CardType.ThunderSlash or CardType.Lightning => new Color(0.600f, 0.545f, 0.685f, 0.995f),
			CardType.Nullification or CardType.ExNihilo or CardType.Harvest => new Color(0.735f, 0.685f, 0.570f, 0.995f),
			_ => new Color(0.810f, 0.745f, 0.615f, 0.995f)
		};
	}

	private static void ApplyFont(Control control, Font? font, string themeProperty = "font")
	{
		if (font is not null)
		{
			control.AddThemeFontOverride(themeProperty, font);
		}
	}
}
