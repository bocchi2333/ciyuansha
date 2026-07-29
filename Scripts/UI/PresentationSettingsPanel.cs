using System;
using CiyuanSha.Settings;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Global presentation settings shared by the lobby and the in-match HUD.
/// </summary>
public partial class PresentationSettingsPanel : Control
{
    private const string PreviewAudioPath = "res://Assets/FX/Audio/card_select.mp3";

    private Button? _openButton;
    private Control? _modal;
    private Panel? _dialog;
    private Button? _audioToggle;
    private HSlider? _volumeSlider;
    private Label? _volumeValueLabel;
    private Button? _visualToggle;
    private Button? _screenFlashToggle;
    private Button? _reducedMotionToggle;
    private OptionButton? _qualityOption;
    private Label? _statusLabel;
    private AudioStreamPlayer? _previewPlayer;
    private Tween? _dialogTween;
    private bool _syncingControls;

    public override void _Ready()
    {
        CyberStyle.ApplyRootTheme(this);
        BuildInterface();
        if (GameUserSettings.Instance is not null)
        {
            GameUserSettings.Instance.OnChanged += HandleSettingsChanged;
        }

        SyncControls();
        CallDeferred(nameof(ApplyDeveloperArgs));
    }

    public override void _ExitTree()
    {
        if (GameUserSettings.Instance is not null)
        {
            GameUserSettings.Instance.OnChanged -= HandleSettingsChanged;
        }

        _dialogTween?.Kill();
        _previewPlayer?.Stop();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode == Key.Escape && _modal?.Visible == true)
        {
            CloseAndSave();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode == Key.F10 && _modal?.Visible != true)
        {
            OpenDialog();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildInterface()
    {
        _openButton = new Button
        {
            Name = "PresentationSettingsButton",
            Text = "音画",
            TooltipText = "音画设置（F10）",
            FocusMode = FocusModeEnum.All,
            ZIndex = 1
        };
        _openButton.AnchorLeft = 1f;
        _openButton.AnchorRight = 1f;
        _openButton.OffsetLeft = -108f;
        _openButton.OffsetTop = 20f;
        _openButton.OffsetRight = -22f;
        _openButton.OffsetBottom = 62f;
        CyberStyle.ApplyButton(_openButton, CyberButtonKind.Quiet);
        CyberStyle.AttachHoverLift(_openButton, 1.035f);
        _openButton.Pressed += OpenDialog;
        AddChild(_openButton);

        _modal = new Control
        {
            Name = "PresentationSettingsModal",
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
            ZIndex = 2
        };
        _modal.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_modal);

        ColorRect dimmer = new()
        {
            Name = "ModalDimmer",
            Color = new Color(0.004f, 0.006f, 0.006f, 0.82f),
            MouseFilter = MouseFilterEnum.Stop
        };
        dimmer.SetAnchorsPreset(LayoutPreset.FullRect);
        dimmer.GuiInput += HandleDimmerInput;
        _modal.AddChild(dimmer);

        _dialog = new Panel
        {
            Name = "SettingsDialog",
            MouseFilter = MouseFilterEnum.Stop
        };
        _dialog.AnchorLeft = 0.5f;
        _dialog.AnchorRight = 0.5f;
        _dialog.AnchorTop = 0.5f;
        _dialog.AnchorBottom = 0.5f;
        _dialog.OffsetLeft = -300f;
        _dialog.OffsetTop = -285f;
        _dialog.OffsetRight = 300f;
        _dialog.OffsetBottom = 285f;
        CyberStyle.ApplyPanel(_dialog, CyberPanelKind.Overlay);
        _modal.AddChild(_dialog);

        Label kicker = CreateLabel("本机偏好 · LOCAL SETTINGS", 30f, 18f, 450f, 42f, CyberStyle.Gold);
        kicker.AddThemeFontSizeOverride("font_size", 13);
        _dialog.AddChild(kicker);

        Label title = CreateLabel("音画设置", 28f, 42f, 360f, 82f, CyberStyle.GoldBright, title: true);
        title.AddThemeFontSizeOverride("font_size", 30);
        _dialog.AddChild(title);

        Label subtitle = CreateLabel("设置即时生效，仅保存在当前电脑，不参与联机同步。", 30f, 80f, 555f, 104f, CyberStyle.MutedText);
        subtitle.AddThemeFontSizeOverride("font_size", 13);
        _dialog.AddChild(subtitle);
        _dialog.AddChild(CreateRule(30f, 108f, 570f));

        AddSettingDescription("战斗音效", "出牌、伤害、治疗与响应提示音", 32f, 122f);
        _audioToggle = CreateToggleButton(432f, 124f, HandleAudioToggled);
        _dialog.AddChild(_audioToggle);

        Label volumeLabel = CreateLabel("音效音量", 32f, 174f, 138f, 214f, CyberStyle.Paper);
        _dialog.AddChild(volumeLabel);
        _volumeSlider = new HSlider
        {
            Name = "SfxVolumeSlider",
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Position = new Vector2(144f, 178f),
            Size = new Vector2(250f, 34f),
            FocusMode = FocusModeEnum.All
        };
        CyberStyle.ApplySlider(_volumeSlider);
        _volumeSlider.ValueChanged += HandleVolumeChanged;
        _volumeSlider.DragEnded += HandleVolumeDragEnded;
        _dialog.AddChild(_volumeSlider);

        _volumeValueLabel = CreateLabel("70%", 400f, 178f, 458f, 212f, CyberStyle.GoldBright);
        _volumeValueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _dialog.AddChild(_volumeValueLabel);

        Button previewButton = new()
        {
            Name = "PreviewAudioButton",
            Text = "试听",
            Position = new Vector2(476f, 174f),
            Size = new Vector2(92f, 40f)
        };
        CyberStyle.ApplyButton(previewButton, CyberButtonKind.Neutral);
        previewButton.Pressed += PlayAudioPreview;
        _dialog.AddChild(previewButton);

        _dialog.AddChild(CreateRule(30f, 226f, 570f));
        AddSettingDescription("战斗特效", "控制卡牌飞行、伤害和治疗演出", 32f, 242f);
        _visualToggle = CreateToggleButton(432f, 244f, HandleVisualToggled);
        _dialog.AddChild(_visualToggle);

        AddSettingDescription("屏幕闪光", "关闭全屏明暗闪烁，保留目标局部效果", 32f, 296f);
        _screenFlashToggle = CreateToggleButton(432f, 298f, HandleScreenFlashToggled);
        _dialog.AddChild(_screenFlashToggle);

        AddSettingDescription("低动态模式", "缩短位移和缩放动画，减少动态干扰", 32f, 350f);
        _reducedMotionToggle = CreateToggleButton(432f, 352f, HandleReducedMotionToggled);
        _dialog.AddChild(_reducedMotionToggle);

        AddSettingDescription("特效质量", "多人连续结算时控制装饰层与并发预算", 32f, 404f);
        _qualityOption = new OptionButton
        {
            Name = "EffectQualityOption",
            Position = new Vector2(386f, 406f),
            Size = new Vector2(182f, 42f)
        };
        _qualityOption.AddItem("低 · 性能优先", (int)BattleFxQuality.Low);
        _qualityOption.AddItem("标准 · 推荐", (int)BattleFxQuality.Balanced);
        _qualityOption.AddItem("高 · 画面优先", (int)BattleFxQuality.High);
        CyberStyle.ApplyOptionButton(_qualityOption);
        _qualityOption.ItemSelected += HandleQualitySelected;
        _dialog.AddChild(_qualityOption);

        _statusLabel = CreateLabel("更改会即时应用。", 32f, 462f, 566f, 486f, CyberStyle.NeonJade);
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _dialog.AddChild(_statusLabel);

        Button resetButton = new()
        {
            Name = "ResetSettingsButton",
            Text = "恢复默认",
            Position = new Vector2(32f, 500f),
            Size = new Vector2(142f, 44f)
        };
        CyberStyle.ApplyButton(resetButton, CyberButtonKind.Quiet);
        resetButton.Pressed += ResetDefaults;
        _dialog.AddChild(resetButton);

        Button closeButton = new()
        {
            Name = "SaveSettingsButton",
            Text = "保存并关闭",
            Position = new Vector2(406f, 500f),
            Size = new Vector2(162f, 44f)
        };
        CyberStyle.ApplyButton(closeButton, CyberButtonKind.Primary);
        closeButton.Pressed += CloseAndSave;
        _dialog.AddChild(closeButton);

        _previewPlayer = new AudioStreamPlayer
        {
            Name = "SettingsAudioPreview",
            Bus = "Master",
            Stream = GD.Load<AudioStream>(PreviewAudioPath)
        };
        AddChild(_previewPlayer);
    }

    private void AddSettingDescription(string title, string description, float left, float top)
    {
        if (_dialog is null)
        {
            return;
        }

        Label titleLabel = CreateLabel(title, left, top, 360f, top + 24f, CyberStyle.PaperBright);
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        _dialog.AddChild(titleLabel);

        Label descriptionLabel = CreateLabel(description, left, top + 23f, 410f, top + 45f, CyberStyle.MutedText);
        descriptionLabel.AddThemeFontSizeOverride("font_size", 12);
        _dialog.AddChild(descriptionLabel);
    }

    private static Button CreateToggleButton(float left, float top, Action<bool> handler)
    {
        Button button = new()
        {
            ToggleMode = true,
            Position = new Vector2(left, top),
            Size = new Vector2(136f, 40f),
            FocusMode = FocusModeEnum.All
        };
        CyberStyle.ApplyButton(button, CyberButtonKind.Action);
        button.Toggled += toggledOn => handler(toggledOn);
        return button;
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

    private static ColorRect CreateRule(float left, float top, float right)
    {
        return new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.32f),
            Position = new Vector2(left, top),
            Size = new Vector2(right - left, 1f)
        };
    }

    private void OpenDialog()
    {
        if (_modal is null || _dialog is null)
        {
            return;
        }

        SyncControls();
        _dialogTween?.Kill();
        _modal.Visible = true;
        _modal.Modulate = new Color(1f, 1f, 1f, 0f);
        _dialog.PivotOffset = _dialog.Size / 2f;
        _dialog.Scale = new Vector2(0.965f, 0.965f);
        _dialogTween = CreateTween();
        _dialogTween.SetParallel(true);
        _dialogTween.TweenProperty(_modal, "modulate", Colors.White, 0.16f);
        _dialogTween.TweenProperty(_dialog, "scale", Vector2.One, 0.20f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _audioToggle?.GrabFocus();
    }

    private void CloseAndSave()
    {
        GameUserSettings.Instance?.SaveSettings();
        _previewPlayer?.Stop();
        if (_modal is null || _dialog is null)
        {
            return;
        }

        _dialogTween?.Kill();
        _dialogTween = CreateTween();
        _dialogTween.SetParallel(true);
        _dialogTween.TweenProperty(_modal, "modulate", new Color(1f, 1f, 1f, 0f), 0.13f);
        _dialogTween.TweenProperty(_dialog, "scale", new Vector2(0.975f, 0.975f), 0.13f)
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

    private void HandleDimmerInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            CloseAndSave();
        }
    }

    private void HandleSettingsChanged()
    {
        if (!_syncingControls)
        {
            SyncControls();
        }
    }

    private void SyncControls()
    {
        GameUserSettings? settings = GameUserSettings.Instance;
        if (settings is null)
        {
            return;
        }

        _syncingControls = true;
        SetToggleState(_audioToggle, settings.AudioEnabled);
        SetToggleState(_visualToggle, settings.VisualEffectsEnabled);
        SetToggleState(_screenFlashToggle, settings.ScreenFlashEnabled);
        SetToggleState(_reducedMotionToggle, settings.ReducedMotion);
        if (_volumeSlider is not null)
        {
            _volumeSlider.SetValueNoSignal(settings.SfxVolumePercent);
        }

        if (_volumeValueLabel is not null)
        {
            _volumeValueLabel.Text = $"{Mathf.RoundToInt(settings.SfxVolumePercent)}%";
        }

        _qualityOption?.Select((int)settings.EffectQuality);
        _syncingControls = false;
    }

    private static void SetToggleState(Button? button, bool enabled)
    {
        if (button is null)
        {
            return;
        }

        button.SetPressedNoSignal(enabled);
        button.Text = enabled ? "已开启" : "已关闭";
        CyberStyle.ApplyButton(button, enabled ? CyberButtonKind.Action : CyberButtonKind.Quiet);
    }

    private void HandleAudioToggled(bool enabled)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetAudioEnabled(enabled);
        SetStatus(enabled ? "战斗音效已开启。" : "战斗音效已关闭。", success: enabled);
        if (enabled)
        {
            PlayAudioPreview();
        }
    }

    private void HandleVolumeChanged(double value)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetSfxVolumePercent((float)value);
        if (_volumeValueLabel is not null)
        {
            _volumeValueLabel.Text = $"{Mathf.RoundToInt((float)value)}%";
        }
    }

    private void HandleVolumeDragEnded(bool valueChanged)
    {
        if (!valueChanged)
        {
            return;
        }

        GameUserSettings.Instance?.SaveSettings();
        PlayAudioPreview();
    }

    private void HandleVisualToggled(bool enabled)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetVisualEffectsEnabled(enabled);
        SetStatus(enabled ? "战斗特效已开启。" : "战斗特效已关闭，仅保留规则文字。", success: enabled);
    }

    private void HandleScreenFlashToggled(bool enabled)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetScreenFlashEnabled(enabled);
        SetStatus(enabled ? "屏幕闪光已开启。" : "屏幕闪光已关闭。", success: true);
    }

    private void HandleReducedMotionToggled(bool enabled)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetReducedMotion(enabled);
        SetStatus(enabled ? "低动态模式已开启。" : "使用标准动态演出。", success: true);
    }

    private void HandleQualitySelected(long index)
    {
        if (_syncingControls)
        {
            return;
        }

        GameUserSettings.Instance?.SetEffectQuality((BattleFxQuality)Mathf.Clamp((int)index, 0, 2));
        GameUserSettings.Instance?.SaveSettings();
        SetStatus(index == (int)BattleFxQuality.Low ? "已切换为性能优先。" : "特效质量已更新。", success: true);
    }

    private void ResetDefaults()
    {
        GameUserSettings.Instance?.ResetToDefaults();
        SyncControls();
        SetStatus("已恢复推荐设置。", success: true);
        PlayAudioPreview();
    }

    private void PlayAudioPreview()
    {
        GameUserSettings? settings = GameUserSettings.Instance;
        if (settings is null || _previewPlayer is null)
        {
            return;
        }

        if (!settings.AudioEnabled || settings.SfxVolumePercent <= 0.01f)
        {
            SetStatus("请先开启音效并提高音量。", success: false);
            return;
        }

        _previewPlayer.Stop();
        _previewPlayer.VolumeDb = Mathf.Clamp(settings.SfxVolumeDb - 2f, -48f, 4f);
        _previewPlayer.PitchScale = 1.04f;
        _previewPlayer.Play();
        SetStatus("正在试听当前音量。", success: true);
    }

    private void SetStatus(string text, bool success)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", success ? CyberStyle.NeonJade : CyberStyle.Vermilion.Lightened(0.22f));
    }

    private async void ApplyDeveloperArgs()
    {
        string capturePath = string.Empty;
        bool shouldOpen = false;
        bool autoQuit = false;
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.Equals("--open-settings", StringComparison.OrdinalIgnoreCase))
            {
                shouldOpen = true;
            }
            else if (argument.StartsWith("--capture-settings=", StringComparison.OrdinalIgnoreCase))
            {
                capturePath = argument["--capture-settings=".Length..].Trim();
                shouldOpen = true;
            }
            else if (argument.Equals("--auto-quit=true", StringComparison.OrdinalIgnoreCase))
            {
                autoQuit = true;
            }
        }

        if (!shouldOpen)
        {
            return;
        }

        OpenDialog();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(0.30), SceneTreeTimer.SignalName.Timeout);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            Image image = GetViewport().GetTexture().GetImage();
            Error error = image.SavePng(capturePath);
            GD.Print($"[settings-capture] {error}: {capturePath}");
        }

        if (autoQuit)
        {
            _previewPlayer?.Stop();
            await ToSignal(GetTree().CreateTimer(0.08), SceneTreeTimer.SignalName.Timeout);
            GetTree().Quit();
        }
    }
}
