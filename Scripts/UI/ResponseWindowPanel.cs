using System.Linq;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Minimal manual response panel for out-of-turn Dodge decisions.
/// </summary>
public partial class ResponseWindowPanel : Control
{
    [Export]
    public NodePath PromptLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath PlayDodgeButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath PassButtonPath { get; set; } = new NodePath();

    private Label? _promptLabel;
    private Button? _playDodgeButton;
    private Button? _passButton;
    private Label? _sealLabel;
    private ColorRect? _topInkLine;
    private ColorRect? _bottomInkLine;
    private ColorRect? _responsePressureFill;
    private Label? _responseMetaLabel;
    private InkCardButton? _responseCardPreview;
    private bool _wasVisible;

    public override void _Ready()
    {
        _promptLabel = GetNodeOrNull<Label>(PromptLabelPath);
        _playDodgeButton = GetNodeOrNull<Button>(PlayDodgeButtonPath);
        _passButton = GetNodeOrNull<Button>(PassButtonPath);
        EnsureStageOrnaments();
        ApplyChrome();

        if (_playDodgeButton is not null)
        {
            _playDodgeButton.Pressed += HandlePlayDodgePressed;
        }

        if (_passButton is not null)
        {
            _passButton.Pressed += HandlePassPressed;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnResponseWindowChanged += HandleStateChanged;
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleNetworkStateChanged;
        }

        RefreshView();
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnResponseWindowChanged -= HandleStateChanged;
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleNetworkStateChanged;
        }
    }

    private void RefreshView()
    {
        GameManager? gameManager = GameManager.Instance;
        PlayerCharacter? localCharacter = ResolveLocalCharacter();
        bool isLocalResponse = gameManager is not null
            && gameManager.HasPendingResponseWindow
            && localCharacter is not null
            && localCharacter.OwnerPeerId == gameManager.PendingResponsePeerId;

        Visible = isLocalResponse;
        if (isLocalResponse && !_wasVisible)
        {
            PlayOpenAnimation();
        }

        _wasVisible = isLocalResponse;

        if (!isLocalResponse || gameManager is null || localCharacter is null)
        {
            return;
        }

        CardInstance? dodgeCard = localCharacter.FindFirstHandCardOfType(CardType.Dodge);
        CardInstance? peachCard = localCharacter.FindFirstHandCardOfType(CardType.Peach);
        CardInstance? wineCard = localCharacter.FindFirstHandCardOfType(CardType.Wine);
        CardInstance? slashCard = localCharacter.HandCards.FirstOrDefault(card => CardRules.IsSlash(card.CardType));
        CardInstance? nullificationCard = localCharacter.FindFirstHandCardOfType(CardType.Nullification);
        int dodgeCount = localCharacter.HandCards.Count(card => card.CardType == CardType.Dodge);
        int peachCount = localCharacter.HandCards.Count(card => card.CardType == CardType.Peach);
        int wineCount = localCharacter.HandCards.Count(card => card.CardType == CardType.Wine);
        int slashCount = localCharacter.HandCards.Count(card => CardRules.IsSlash(card.CardType));
        int nullificationCount = localCharacter.HandCards.Count(card => card.CardType == CardType.Nullification);
        bool canUseSerpentSpearAsSlash = localCharacter.CanUseSerpentSpear && localCharacter.HandCardCount >= 2;
        bool isDodgeResponse = gameManager.PendingResponseKind == ResponseWindowKind.Dodge;
        bool isPeachResponse = gameManager.PendingResponseKind == ResponseWindowKind.Peach;
        bool isSlashResponse = gameManager.PendingResponseKind == ResponseWindowKind.Slash;
        bool isNullificationResponse = gameManager.PendingResponseKind == ResponseWindowKind.Nullification;
        bool canUseWineForSelfSave = isPeachResponse
            && localCharacter.OwnerPeerId == gameManager.PendingResponsePeerId
            && gameManager.PendingResponsePrompt.Contains("Wine", System.StringComparison.OrdinalIgnoreCase);
        bool canRespond = isDodgeResponse
            ? dodgeCard is not null
            : isPeachResponse
                ? peachCard is not null || (canUseWineForSelfSave && wineCard is not null)
                : isSlashResponse
                    ? slashCard is not null || canUseSerpentSpearAsSlash
                    : isNullificationResponse && nullificationCard is not null;
        UpdateSeal(gameManager.PendingResponseKind, canRespond);
        bool showSerpentSpearVirtual = isSlashResponse && slashCard is null && canUseSerpentSpearAsSlash;
        UpdateResponsePressure(gameManager.PendingResponseKind, canRespond);
        UpdateResponseCardPreview(
            GetPreviewCard(gameManager.PendingResponseKind, dodgeCard, peachCard, wineCard, slashCard, nullificationCard, canUseWineForSelfSave),
            canRespond,
            showSerpentSpearVirtual);

        if (_promptLabel is not null)
        {
            _promptLabel.Text = $"需要响应：{FormatResponseKind(gameManager.PendingResponseKind)}\n{BattleLogTextLocalizer.Localize(gameManager.PendingResponsePrompt)}";
        }

        if (_responseMetaLabel is not null)
        {
            _responseMetaLabel.Text = $"{BuildResponseResourceText(gameManager.PendingResponseKind, dodgeCount, peachCount, wineCount, slashCount, nullificationCount, canUseSerpentSpearAsSlash)}\n{BuildResponseActionHint(gameManager.PendingResponseKind, canRespond, showSerpentSpearVirtual, canUseWineForSelfSave)}";
        }

        if (_playDodgeButton is not null)
        {
            _playDodgeButton.Disabled = !canRespond;
            _playDodgeButton.Text = isPeachResponse
                ? (canRespond ? $"打出救援牌 ({peachCount + (canUseWineForSelfSave ? wineCount : 0)})" : "无可用救援牌")
                : isSlashResponse
                    ? (canRespond ? slashCard is not null ? $"打出【杀】({slashCount})" : "发动丈八蛇矛" : "无可用【杀】")
                    : isNullificationResponse
                        ? (canRespond ? $"打出【无懈】({nullificationCount})" : "无可用【无懈】")
                        : (canRespond ? $"打出【闪】({dodgeCount})" : "无可用【闪】");
        }

        if (_passButton is not null)
        {
            _passButton.Disabled = false;
            _passButton.Text = isPeachResponse
                ? "放弃救援"
                : isNullificationResponse
                    ? "不无懈"
                    : isSlashResponse
                        ? "不出杀"
                        : "承受 / 放弃";
            _passButton.TooltipText = "放弃本次响应，继续结算当前牌。";
        }
    }

    private void HandlePlayDodgePressed()
    {
        PlayerCharacter? localCharacter = ResolveLocalCharacter();
        if (localCharacter is null)
        {
            return;
        }

        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null)
        {
            return;
        }

        CardType responseCardType = gameManager.PendingResponseKind == ResponseWindowKind.Peach
            ? CardType.Peach
            : gameManager.PendingResponseKind == ResponseWindowKind.Slash
                ? CardType.Slash
                : gameManager.PendingResponseKind == ResponseWindowKind.Nullification
                    ? CardType.Nullification
                    : CardType.Dodge;
        CardInstance? responseCard = responseCardType == CardType.Slash
            ? localCharacter.HandCards.FirstOrDefault(card => CardRules.IsSlash(card.CardType))
            : responseCardType == CardType.Peach
                ? localCharacter.FindFirstHandCardOfType(CardType.Peach)
                    ?? (gameManager.PendingResponsePrompt.Contains("Wine", System.StringComparison.OrdinalIgnoreCase)
                        ? localCharacter.FindFirstHandCardOfType(CardType.Wine)
                        : null)
                : localCharacter.FindFirstHandCardOfType(responseCardType);
        bool useSerpentSpearAsSlash = responseCardType == CardType.Slash
            && responseCard is null
            && localCharacter.CanUseSerpentSpear
            && localCharacter.HandCardCount >= 2;
        if (responseCard is null && !useSerpentSpearAsSlash)
        {
            RefreshView();
            return;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = responseCardType == CardType.Peach
                ? NetworkPlayCommandType.RespondPeach
                : responseCardType == CardType.Slash
                    ? NetworkPlayCommandType.RespondSlash
                    : responseCardType == CardType.Nullification
                        ? NetworkPlayCommandType.RespondNullification
                        : NetworkPlayCommandType.RespondDodge,
            CardInstanceId = responseCard?.InstanceId ?? string.Empty
        };

        if (LanMultiplayerManager.Instance?.IsConnected == true)
        {
            LanMultiplayerManager.Instance.SubmitPlayCommand(command);
        }
        else
        {
            GameManager.Instance?.TryHandleNetworkPlayCommand(localCharacter.OwnerPeerId, command);
        }
    }

    private void HandlePassPressed()
    {
        int localPeerId = ResolveLocalPeerId();
        if (localPeerId <= 0)
        {
            return;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.DeclineResponse
        };

        if (LanMultiplayerManager.Instance?.IsConnected == true)
        {
            LanMultiplayerManager.Instance.SubmitPlayCommand(command);
        }
        else
        {
            GameManager.Instance?.TryHandleNetworkPlayCommand(localPeerId, command);
        }
    }

    private void HandleStateChanged()
    {
        RefreshView();
    }

    private void HandleNetworkStateChanged(LanSessionState state)
    {
        RefreshView();
    }

    private PlayerCharacter? ResolveLocalCharacter()
    {
        int localPeerId = ResolveLocalPeerId();
        if (localPeerId <= 0)
        {
            return null;
        }

        return GetTree().GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .FirstOrDefault(character => character.OwnerPeerId == localPeerId);
    }

    private int ResolveLocalPeerId()
    {
        if (LanMultiplayerManager.Instance?.IsConnected == true)
        {
            return LanMultiplayerManager.Instance.LocalPeerId;
        }

        return GameManager.Instance?.PendingResponsePeerId ?? 0;
    }

    private void ApplyChrome()
    {
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel"), CyberPanelKind.Alert);
        CyberStyle.ApplyLabel(_promptLabel, title: true);
        CyberStyle.ApplyLabel(_sealLabel, title: true);
        CyberStyle.ApplyLabel(_responseMetaLabel, color: CyberStyle.Paper);
        CyberStyle.ApplyButton(_playDodgeButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_passButton, CyberButtonKind.Danger);
        if (_playDodgeButton is not null)
        {
            CyberStyle.AttachHoverLift(_playDodgeButton);
        }

        if (_passButton is not null)
        {
            CyberStyle.AttachHoverLift(_passButton);
        }
    }

    private void PlayOpenAnimation()
    {
		Scale = Vector2.One;
		Modulate = Colors.White;
		Control? panel = GetNodeOrNull<Control>("Panel");
		ColorRect? dimmer = GetNodeOrNull<ColorRect>("ModalDimmer");
		if (panel is null)
		{
			return;
		}

		panel.Scale = new Vector2(0.92f, 0.92f);
		panel.Modulate = new Color(1f, 1f, 1f, 0f);
		panel.PivotOffset = panel.Size / 2f;
		if (dimmer is not null)
		{
			dimmer.Modulate = new Color(1f, 1f, 1f, 0f);
		}

        Tween tween = CreateTween();
        tween.SetParallel(true);
		tween.TweenProperty(panel, "scale", Vector2.One, 0.18f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
		tween.TweenProperty(panel, "modulate", Colors.White, 0.13f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
		if (dimmer is not null)
		{
			tween.TweenProperty(dimmer, "modulate", Colors.White, 0.16f)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}
    }

    private void EnsureStageOrnaments()
    {
        Control? panel = GetNodeOrNull<Control>("Panel");
        if (panel is null)
        {
            return;
        }

        _sealLabel = panel.GetNodeOrNull<Label>("ResponseSealLabel");
        if (_sealLabel is null)
        {
            _sealLabel = new Label
            {
                Name = "ResponseSealLabel",
                Text = "应",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = new Color(1f, 0.78f, 0.58f, 0.10f)
            };
            _sealLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            _sealLabel.AddThemeFontSizeOverride("font_size", 118);
            panel.AddChild(_sealLabel);
            panel.MoveChild(_sealLabel, 0);
        }

        _topInkLine = EnsureInkLine(panel, "TopInkLine", 0.0f);
        _bottomInkLine = EnsureInkLine(panel, "BottomInkLine", 1.0f);
        EnsureResponsePressureBar(panel);
        EnsureResponseMetaLabel();
        EnsureResponseCardPreview();
    }

    private void EnsureResponseMetaLabel()
    {
        VBoxContainer? vbox = GetNodeOrNull<VBoxContainer>("Panel/VBox");
        if (vbox is null)
        {
            return;
        }

        _responseMetaLabel = vbox.GetNodeOrNull<Label>("ResponseMetaLabel");
        if (_responseMetaLabel is not null)
        {
            return;
        }

        _responseMetaLabel = new Label
        {
            Name = "ResponseMetaLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = string.Empty
        };
        vbox.AddChild(_responseMetaLabel);
        vbox.MoveChild(_responseMetaLabel, Mathf.Min(1, vbox.GetChildCount() - 1));
    }

    private void EnsureResponsePressureBar(Control panel)
    {
        _responsePressureFill = panel.GetNodeOrNull<ColorRect>("ResponsePressureFill");
        if (_responsePressureFill is not null)
        {
            return;
        }

        ColorRect frame = new()
        {
            Name = "ResponsePressureFrame",
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(0.10f, 0.065f, 0.038f, 0.72f)
        };
        frame.AnchorLeft = 0.18f;
        frame.AnchorRight = 0.82f;
        frame.AnchorTop = 0f;
        frame.AnchorBottom = 0f;
        frame.OffsetTop = 32f;
        frame.OffsetBottom = 36f;
        panel.AddChild(frame);
        panel.MoveChild(frame, 1);

        _responsePressureFill = new ColorRect
        {
            Name = "ResponsePressureFill",
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.62f)
        };
        _responsePressureFill.AnchorLeft = 0.18f;
        _responsePressureFill.AnchorRight = 0.82f;
        _responsePressureFill.AnchorTop = 0f;
        _responsePressureFill.AnchorBottom = 0f;
        _responsePressureFill.OffsetTop = 32f;
        _responsePressureFill.OffsetBottom = 36f;
        panel.AddChild(_responsePressureFill);
        panel.MoveChild(_responsePressureFill, 2);
    }

    private void EnsureResponseCardPreview()
    {
        VBoxContainer? vbox = GetNodeOrNull<VBoxContainer>("Panel/VBox");
        if (vbox is null)
        {
            return;
        }

        CenterContainer? host = vbox.GetNodeOrNull<CenterContainer>("ResponseCardPreviewHost");
        if (host is null)
        {
            host = new CenterContainer
            {
                Name = "ResponseCardPreviewHost",
                CustomMinimumSize = new Vector2(0f, 170f),
                Visible = false
            };
            vbox.AddChild(host);
            vbox.MoveChild(host, Mathf.Min(1, vbox.GetChildCount() - 1));
        }

        _responseCardPreview = host.GetNodeOrNull<InkCardButton>("ResponseCardPreview");
        if (_responseCardPreview is null)
        {
            _responseCardPreview = new InkCardButton
            {
                Name = "ResponseCardPreview",
                CustomMinimumSize = new Vector2(124f, 158f),
                FocusMode = FocusModeEnum.None,
                MouseFilter = MouseFilterEnum.Ignore
            };
            host.AddChild(_responseCardPreview);
        }
    }

    private static ColorRect EnsureInkLine(Control panel, string name, float anchorY)
    {
        ColorRect? line = panel.GetNodeOrNull<ColorRect>(name);
        if (line is not null)
        {
            return line;
        }

        line = new ColorRect
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.34f)
        };
        line.AnchorLeft = 0.08f;
        line.AnchorRight = 0.92f;
        line.AnchorTop = anchorY;
        line.AnchorBottom = anchorY;
        line.OffsetTop = anchorY <= 0.5f ? 10f : -12f;
        line.OffsetBottom = line.OffsetTop + 2f;
        panel.AddChild(line);
        panel.MoveChild(line, 1);
        return line;
    }

    private void UpdateSeal(ResponseWindowKind kind, bool canRespond)
    {
        if (_sealLabel is not null)
        {
            _sealLabel.Text = kind switch
            {
                ResponseWindowKind.Dodge => "闪",
                ResponseWindowKind.Peach => "桃",
                ResponseWindowKind.Slash => "杀",
                ResponseWindowKind.Nullification => "懈",
                _ => "应"
            };
            _sealLabel.Modulate = canRespond
                ? new Color(1f, 0.82f, 0.58f, 0.13f)
                : new Color(0.78f, 0.16f, 0.11f, 0.16f);
        }

        Color lineColor = canRespond
            ? new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.42f)
            : new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.44f);
        if (_topInkLine is not null)
        {
            _topInkLine.Color = lineColor;
        }

        if (_bottomInkLine is not null)
        {
            _bottomInkLine.Color = lineColor;
        }
    }

    private void UpdateResponsePressure(ResponseWindowKind kind, bool canRespond)
    {
        if (_responsePressureFill is null)
        {
            return;
        }

        Color color = canRespond
            ? new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.64f)
            : new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.70f);
        _responsePressureFill.Color = kind == ResponseWindowKind.Nullification && canRespond
            ? new Color(CyberStyle.Thunder.R, CyberStyle.Thunder.G, CyberStyle.Thunder.B, 0.62f)
            : color;
    }

    private void UpdateResponseCardPreview(CardInstance? card, bool canRespond, bool showSerpentSpearVirtual)
    {
        if (_responseCardPreview is null)
        {
            EnsureResponseCardPreview();
        }

        if (_responseCardPreview is null)
        {
            return;
        }

        Control? host = _responseCardPreview.GetParentOrNull<Control>();
        if (card is null && !showSerpentSpearVirtual)
        {
            _responseCardPreview.Visible = false;
            if (host is not null)
            {
                host.Visible = false;
            }

            return;
        }

        if (host is not null)
        {
            host.Visible = true;
        }

        _responseCardPreview.Visible = true;
        _responseCardPreview.Disabled = false;
        if (card is not null)
        {
            _responseCardPreview.Text = CardDisplayFormatter.FormatCardFace(card);
            _responseCardPreview.TooltipText = card.Description;
            CyberStyle.ApplyCardButton(_responseCardPreview, card, canRespond, disabled: false);
            _responseCardPreview.Configure(card, canRespond, disabled: false);
            return;
        }

        _responseCardPreview.Text = CardDisplayFormatter.FormatVirtualCardFace(CardType.Slash, "丈八蛇矛", "弃2张手牌");
        _responseCardPreview.TooltipText = "丈八蛇矛：可将两张手牌当【杀】打出。";
        CyberStyle.ApplyVirtualCardButton(_responseCardPreview, CardType.Slash, canRespond, disabled: false);
        _responseCardPreview.ConfigureVirtual(CardType.Slash, "丈八蛇矛", "弃2张手牌", canRespond, disabled: false);
    }

    private static CardInstance? GetPreviewCard(
        ResponseWindowKind kind,
        CardInstance? dodgeCard,
        CardInstance? peachCard,
        CardInstance? wineCard,
        CardInstance? slashCard,
        CardInstance? nullificationCard,
        bool canUseWineForSelfSave)
    {
        return kind switch
        {
            ResponseWindowKind.Dodge => dodgeCard,
            ResponseWindowKind.Peach => peachCard ?? (canUseWineForSelfSave ? wineCard : null),
            ResponseWindowKind.Slash => slashCard,
            ResponseWindowKind.Nullification => nullificationCard,
            _ => null
        };
    }

    private static string BuildResponseResourceText(
        ResponseWindowKind kind,
        int dodgeCount,
        int peachCount,
        int wineCount,
        int slashCount,
        int nullificationCount,
        bool canUseSerpentSpearAsSlash)
    {
        return kind switch
        {
            ResponseWindowKind.Peach => $"可用资源：桃 {peachCount} | 酒 {wineCount}",
            ResponseWindowKind.Slash => $"可用资源：杀 {slashCount} | 丈八蛇矛 {(canUseSerpentSpearAsSlash ? "可发动" : "不可发动")}",
            ResponseWindowKind.Nullification => $"可用资源：无懈可击 {nullificationCount}",
            ResponseWindowKind.Dodge => $"可用资源：闪 {dodgeCount}",
            _ => "可用资源：无"
        };
    }

    private static string BuildResponseActionHint(
        ResponseWindowKind kind,
        bool canRespond,
        bool showSerpentSpearVirtual,
        bool canUseWineForSelfSave)
    {
        if (!canRespond)
        {
            return kind switch
            {
                ResponseWindowKind.Peach => "当前没有救援牌；若无人救援，将继续濒死结算。",
                ResponseWindowKind.Nullification => "当前没有【无懈可击】；可直接选择“不无懈”。",
                ResponseWindowKind.Slash => "当前无法出【杀】；可选择“不出杀”。",
                _ => "当前没有可用响应牌；可选择“承受 / 放弃”。"
            };
        }

        if (showSerpentSpearVirtual)
        {
            return "推荐操作：可发动丈八蛇矛，弃 2 张手牌视为打出【杀】。";
        }

        return kind switch
        {
            ResponseWindowKind.Peach => canUseWineForSelfSave
                ? "推荐操作：打出【桃】；若是自救，也可打出【酒】。"
                : "推荐操作：打出【桃】救援濒死角色。",
            ResponseWindowKind.Nullification => "推荐操作：打出【无懈可击】抵消当前锦囊。",
            ResponseWindowKind.Slash => "推荐操作：打出【杀】响应当前要求。",
            ResponseWindowKind.Dodge => "推荐操作：打出【闪】避开本次伤害。",
            _ => "推荐操作：选择可用响应牌。"
        };
    }

    private static string FormatResponseKind(ResponseWindowKind kind)
    {
        return kind switch
        {
            ResponseWindowKind.Dodge => "闪",
            ResponseWindowKind.Peach => "桃 / 酒",
            ResponseWindowKind.Slash => "杀",
            ResponseWindowKind.Nullification => "无懈可击",
            _ => kind.ToString()
        };
    }
}
