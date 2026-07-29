using System;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using CiyuanSha.Settings;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Global presentation layer for quit confirmations and recoverable network failures.
/// </summary>
public partial class SystemDialogPanel : Control
{
    public static SystemDialogPanel? Instance { get; private set; }

    private Button? _openExitButton;
    private Control? _modal;
    private Panel? _dialog;
    private Label? _kickerLabel;
    private Label? _titleLabel;
    private Label? _bodyLabel;
    private Label? _detailLabel;
    private Label? _hintLabel;
    private Button? _secondaryButton;
    private Button? _primaryButton;
    private Tween? _dialogTween;
    private Action? _primaryAction;
    private Action? _secondaryAction;
    private DialogMode _mode;

    public bool IsDialogOpen => _modal?.Visible == true;

    public override void _Ready()
    {
        if (Instance is not null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
        GetTree().AutoAcceptQuit = false;
        CyberStyle.ApplyRootTheme(this);
        BuildInterface();
        BindNetworkEvents();
        CallDeferred(nameof(ApplyDeveloperArgs));
    }

    public override void _ExitTree()
    {
        UnbindNetworkEvents();
        _dialogTween?.Kill();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && IsInsideTree())
        {
            ShowQuitConfirmation();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!IsDialogOpen
            || inputEvent is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            return;
        }

        CloseDialog();
        GetViewport().SetInputAsHandled();
    }

    public void ShowQuitConfirmation()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        bool inMatch = GameManager.Instance?.IsMatchRunning == true;
        string body;
        string detail;

        if (network?.IsHost == true && network.IsConnected)
        {
            body = inMatch
                ? "当前牌局仍在进行。退出后房间将关闭，其他玩家会与房主断开。"
                : "当前局域网房间仍在开放。退出后，候战玩家会离开本房间。";
            detail = "房主退出不会保存本局进度。若只是暂时离开，请选择继续游戏。";
        }
        else if (network?.IsConnected == true)
        {
            body = inMatch
                ? "退出会离开当前牌局；房主会暂时保留你的席位，之后可使用相同名字重连。"
                : "退出会离开当前局域网房间。";
            detail = "确认退出前，请确保当前没有等待你完成的响应或选牌操作。";
        }
        else
        {
            body = "要结束本次《次元杀》并返回桌面吗？";
            detail = "本机音画设置已经自动保存。";
        }

        ConfigureDialog(
            DialogMode.Quit,
            "行囊 · EXIT",
            "退出次元杀",
            body,
            detail,
            "继续游戏",
            "确认退出",
            primaryKind: CyberButtonKind.Danger,
            primaryAction: ConfirmQuit,
            secondaryAction: CloseDialog);
    }

    public void ShowDisconnectNotice(LanDisconnectNotice notice)
    {
        bool hostGone = notice.Reason == LanDisconnectReason.HostDisconnected;
        string title = hostGone ? "与房主失去连接" : "未能加入房间";
        string body = hostGone
            ? notice.WasInMatch
                ? "牌局连接已经中断。你的席位仍由房主保留，可以尝试回到原来的对局。"
                : "候战大厅连接已经中断，可以使用原来的名字和武将重新加入。"
            : "未能建立局域网连接。请确认房主已经开房，并检查 IP、端口和防火墙设置。";
        string address = string.IsNullOrWhiteSpace(notice.Address) ? "未记录" : notice.Address;
        string detail = $"房间地址：{address}:{notice.Port}\n连接状态：{NetworkTextLocalizer.Localize(notice.Message)}";
        bool canReconnect = notice.CanReconnect;

        ConfigureDialog(
            DialogMode.Disconnected,
            hostGone ? "断线 · CONNECTION LOST" : "联机 · CONNECTION FAILED",
            title,
            body,
            detail,
            "返回大厅",
            canReconnect ? "重新连接" : "检查信息",
            primaryKind: canReconnect ? CyberButtonKind.Action : CyberButtonKind.Primary,
            primaryAction: canReconnect ? ReconnectLastSession : CloseDialog,
            secondaryAction: CloseDialog);
    }

    private void BuildInterface()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 110;

        _openExitButton = new Button
        {
            Name = "GlobalExitButton",
            Text = "退出",
            TooltipText = "安全退出游戏",
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop
        };
        _openExitButton.AnchorLeft = 1f;
        _openExitButton.AnchorRight = 1f;
        _openExitButton.OffsetLeft = -202f;
        _openExitButton.OffsetTop = 20f;
        _openExitButton.OffsetRight = -116f;
        _openExitButton.OffsetBottom = 62f;
        CyberStyle.ApplyButton(_openExitButton, CyberButtonKind.Quiet);
        CyberStyle.AttachHoverLift(_openExitButton, 1.035f);
        _openExitButton.Pressed += ShowQuitConfirmation;
        AddChild(_openExitButton);

        _modal = new Control
        {
            Name = "SystemDialogModal",
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        _modal.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_modal);

        ColorRect dimmer = new()
        {
            Name = "ModalDimmer",
            Color = new Color(0.003f, 0.004f, 0.004f, 0.86f),
            MouseFilter = MouseFilterEnum.Stop
        };
        dimmer.SetAnchorsPreset(LayoutPreset.FullRect);
        _modal.AddChild(dimmer);

        _dialog = new Panel
        {
            Name = "SystemDialog",
            MouseFilter = MouseFilterEnum.Stop
        };
        _dialog.AnchorLeft = 0.5f;
        _dialog.AnchorRight = 0.5f;
        _dialog.AnchorTop = 0.5f;
        _dialog.AnchorBottom = 0.5f;
        _dialog.OffsetLeft = -330f;
        _dialog.OffsetTop = -218f;
        _dialog.OffsetRight = 330f;
        _dialog.OffsetBottom = 218f;
        CyberStyle.ApplyPanel(_dialog, CyberPanelKind.Overlay);
        _modal.AddChild(_dialog);

        ColorRect sealLine = new()
        {
            Color = new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.72f),
            Position = new Vector2(30f, 24f),
            Size = new Vector2(4f, 58f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _dialog.AddChild(sealLine);

        _kickerLabel = CreateLabel(string.Empty, 48f, 20f, 600f, 44f, CyberStyle.Gold);
        _kickerLabel.AddThemeFontSizeOverride("font_size", 13);
        _dialog.AddChild(_kickerLabel);

        _titleLabel = CreateLabel(string.Empty, 46f, 44f, 610f, 88f, CyberStyle.GoldBright, title: true);
        _titleLabel.AddThemeFontSizeOverride("font_size", 30);
        _dialog.AddChild(_titleLabel);

        ColorRect rule = new()
        {
            Color = new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.34f),
            Position = new Vector2(30f, 101f),
            Size = new Vector2(600f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _dialog.AddChild(rule);

        _bodyLabel = CreateLabel(string.Empty, 34f, 116f, 626f, 184f, CyberStyle.PaperBright);
        _bodyLabel.AddThemeFontSizeOverride("font_size", 17);
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialog.AddChild(_bodyLabel);

        Panel detailFrame = new()
        {
            Name = "DetailFrame",
            Position = new Vector2(32f, 196f),
            Size = new Vector2(596f, 96f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        CyberStyle.ApplyPanel(detailFrame, CyberPanelKind.Recessed);
        _dialog.AddChild(detailFrame);

        _detailLabel = CreateLabel(string.Empty, 18f, 10f, 578f, 86f, CyberStyle.MutedText);
        _detailLabel.AddThemeFontSizeOverride("font_size", 14);
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailFrame.AddChild(_detailLabel);

        _hintLabel = CreateLabel("Esc 返回", 34f, 304f, 244f, 338f, CyberStyle.MutedText);
        _hintLabel.AddThemeFontSizeOverride("font_size", 12);
        _dialog.AddChild(_hintLabel);

        _secondaryButton = new Button
        {
            Name = "SecondaryButton",
            Position = new Vector2(296f, 348f),
            Size = new Vector2(150f, 52f)
        };
        _secondaryButton.Pressed += HandleSecondaryPressed;
        _dialog.AddChild(_secondaryButton);

        _primaryButton = new Button
        {
            Name = "PrimaryButton",
            Position = new Vector2(458f, 348f),
            Size = new Vector2(170f, 52f)
        };
        _primaryButton.Pressed += HandlePrimaryPressed;
        _dialog.AddChild(_primaryButton);
    }

    private void ConfigureDialog(
        DialogMode mode,
        string kicker,
        string title,
        string body,
        string detail,
        string secondaryText,
        string primaryText,
        CyberButtonKind primaryKind,
        Action primaryAction,
        Action secondaryAction)
    {
        if (_modal is null || _dialog is null || _kickerLabel is null || _titleLabel is null
            || _bodyLabel is null || _detailLabel is null || _primaryButton is null || _secondaryButton is null)
        {
            return;
        }

        _mode = mode;
        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;
        _kickerLabel.Text = kicker;
        _titleLabel.Text = title;
        _bodyLabel.Text = body;
        _detailLabel.Text = detail;
        _secondaryButton.Text = secondaryText;
        _secondaryButton.Disabled = false;
        CyberStyle.ApplyButton(_secondaryButton, CyberButtonKind.Quiet);
        _primaryButton.Text = primaryText;
        _primaryButton.Disabled = false;
        CyberStyle.ApplyButton(_primaryButton, primaryKind);

        _dialogTween?.Kill();
        _modal.Visible = true;
        _modal.Modulate = new Color(1f, 1f, 1f, 0f);
        _dialog.PivotOffset = _dialog.Size / 2f;
        _dialog.Scale = new Vector2(0.95f, 0.95f);
        _dialogTween = CreateTween();
        _dialogTween.SetParallel(true);
        _dialogTween.TweenProperty(_modal, "modulate", Colors.White, 0.16f);
        _dialogTween.TweenProperty(_dialog, "scale", Vector2.One, 0.22f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _secondaryButton.GrabFocus();
    }

    private void CloseDialog()
    {
        _primaryAction = null;
        _secondaryAction = null;
        if (_modal is null || _dialog is null || !_modal.Visible)
        {
            return;
        }

        _dialogTween?.Kill();
        _dialogTween = CreateTween();
        _dialogTween.SetParallel(true);
        _dialogTween.TweenProperty(_modal, "modulate", new Color(1f, 1f, 1f, 0f), 0.12f);
        _dialogTween.TweenProperty(_dialog, "scale", new Vector2(0.975f, 0.975f), 0.12f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        _dialogTween.Finished += () =>
        {
            if (_modal is not null)
            {
                _modal.Visible = false;
                _modal.Modulate = Colors.White;
            }
        };
    }

    private void HandlePrimaryPressed()
    {
        _primaryAction?.Invoke();
    }

    private void HandleSecondaryPressed()
    {
        _secondaryAction?.Invoke();
    }

    private void ConfirmQuit()
    {
        GameUserSettings.Instance?.SaveSettings();
        LanMultiplayerManager.Instance?.LeaveSession();
        GetTree().Quit();
    }

    private void ReconnectLastSession()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || _primaryButton is null || _detailLabel is null)
        {
            return;
        }

        _primaryButton.Disabled = true;
        _primaryButton.Text = "正在连接…";
        _detailLabel.Text = $"正在连接：{network.LastJoinAddress}:{network.Port}\n将使用原来的玩家名与武将恢复席位。";
        Error error = network.ReconnectLastSession();
        if (error != Error.Ok)
        {
            _primaryButton.Disabled = false;
            _primaryButton.Text = "重新连接";
        }
    }

    private void BindNetworkEvents()
    {
        if (LanMultiplayerManager.Instance is null)
        {
            return;
        }

        LanMultiplayerManager.Instance.OnUnexpectedDisconnect += ShowDisconnectNotice;
        LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
    }

    private void UnbindNetworkEvents()
    {
        if (LanMultiplayerManager.Instance is null)
        {
            return;
        }

        LanMultiplayerManager.Instance.OnUnexpectedDisconnect -= ShowDisconnectNotice;
        LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        if (_mode == DialogMode.Disconnected && state is LanSessionState.InLobby or LanSessionState.InMatch)
        {
            CloseDialog();
        }
    }

    private static Label CreateLabel(string text, float left, float top, float right, float bottom, Color color, bool title = false)
    {
        Label label = new()
        {
            Text = text,
            Position = new Vector2(left, top),
            Size = new Vector2(right - left, bottom - top),
            VerticalAlignment = VerticalAlignment.Center
        };
        CyberStyle.ApplyLabel(label, title, color);
        return label;
    }

    private async void ApplyDeveloperArgs()
    {
        string capturePath = string.Empty;
        string dialogName = string.Empty;
        bool autoQuit = false;
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--open-system-dialog=", StringComparison.OrdinalIgnoreCase))
            {
                dialogName = argument["--open-system-dialog=".Length..].Trim();
            }
            else if (argument.StartsWith("--capture-system-dialog=", StringComparison.OrdinalIgnoreCase))
            {
                capturePath = argument["--capture-system-dialog=".Length..].Trim();
            }
            else if (argument.Equals("--auto-quit=true", StringComparison.OrdinalIgnoreCase))
            {
                autoQuit = true;
            }
        }

        if (string.IsNullOrWhiteSpace(dialogName) && string.IsNullOrWhiteSpace(capturePath))
        {
            return;
        }

        if (dialogName.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            ShowDisconnectNotice(new LanDisconnectNotice(
                LanDisconnectReason.HostDisconnected,
                "Disconnected from the LAN host.",
                "192.168.1.108",
                24567,
                WasInMatch: true,
                CanReconnect: true));
        }
        else if (dialogName.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            ShowDisconnectNotice(new LanDisconnectNotice(
                LanDisconnectReason.ConnectionFailed,
                "Failed to connect to the LAN host.",
                "192.168.1.108",
                24567,
                WasInMatch: false,
                CanReconnect: true));
        }
        else
        {
            ShowQuitConfirmation();
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(0.30), SceneTreeTimer.SignalName.Timeout);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            Error error = GetViewport().GetTexture().GetImage().SavePng(capturePath);
            GD.Print($"[system-dialog-capture] {error}: {capturePath}");
        }

        if (autoQuit)
        {
            GetTree().Quit();
        }
    }

    private enum DialogMode
    {
        None,
        Quit,
        Disconnected
    }
}
