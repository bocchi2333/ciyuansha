using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Settings;
using CiyuanSha.UI;
using Godot;

namespace CiyuanSha.Test;

/// <summary>
/// Developer-only scene for reviewing battle presentation cues in isolation.
/// </summary>
public partial class BattleFxShowcase : Control
{
    private const string BackdropPath = "res://Assets/UI/InkBattle/ink_courtyard_match_v1.png";
    private BattleFxDirector? _director;

    public override void _Ready()
    {
        CyberStyle.ApplyRootTheme(this);
        BuildStage();
        CallDeferred(nameof(RunShowcase));
    }

    private void BuildStage()
    {
        TextureRect backdrop = new()
        {
            Texture = GD.Load<Texture2D>(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.72f, 0.74f, 0.74f, 1f)
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        ColorRect wash = new()
        {
            Color = new Color(0.008f, 0.012f, 0.014f, 0.44f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        wash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(wash);

        Panel stage = new()
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        stage.AnchorLeft = 0.34f;
        stage.AnchorRight = 0.66f;
        stage.AnchorTop = 0.30f;
        stage.AnchorBottom = 0.64f;
        CyberStyle.ApplyPanel(stage, CyberPanelKind.Recessed);
        AddChild(stage);

        Label label = new()
        {
            Text = "次元杀  战斗演出校准",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        CyberStyle.ApplyLabel(label, title: true);
        label.AddThemeFontSizeOverride("font_size", 32);
        stage.AddChild(label);

        Label hint = new()
        {
            Text = "出牌  ·  物理  ·  火焰  ·  雷电  ·  治疗  ·  响应  ·  阵亡",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore
        };
        hint.SetAnchorsPreset(LayoutPreset.FullRect);
        hint.OffsetBottom = -28f;
        CyberStyle.ApplyLabel(hint, color: CyberStyle.MutedText);
        stage.AddChild(hint);

        _director = new BattleFxDirector
        {
            Name = "BattleFxDirector",
            MouseFilter = MouseFilterEnum.Ignore,
            UseUserSettings = false
        };
        BattleFxDirector director = _director;
        director.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(director);
    }

    private async void RunShowcase()
    {
        Dictionary<string, string> args = ParseUserArgs();
        string cue = args.GetValueOrDefault("cue", "physical").Trim().ToLowerInvariant();
        ApplyQualityOption(args.GetValueOrDefault("quality", "balanced"));
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        if (cue == "burst")
        {
            await EmitBurst();
            if (_director is not null && _director.ActiveVisualCount > _director.CurrentVisualBudget)
            {
                GD.PushError($"[fx-showcase] Visual budget exceeded: {_director.ActiveVisualCount}/{_director.CurrentVisualBudget}.");
                GetTree().Quit(1);
                return;
            }

            GD.Print($"[fx-showcase] Burst budget: {_director?.ActiveVisualCount ?? 0}/{_director?.CurrentVisualBudget ?? 0}.");
        }
        else
        {
            EmitCue(cue);
        }

        string capturePath = args.GetValueOrDefault("capture", string.Empty);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            await ToSignal(GetTree().CreateTimer(0.14), SceneTreeTimer.SignalName.Timeout);
            Image image = GetViewport().GetTexture().GetImage();
            Error error = image.SavePng(capturePath);
            GD.Print($"[fx-showcase] Capture {cue}: {error} -> {capturePath}");
        }

        if (IsTruthy(args.GetValueOrDefault("auto-quit")))
        {
            await ToSignal(GetTree().CreateTimer(1.05), SceneTreeTimer.SignalName.Timeout);
            _director?.StopAllAudio();
            await ToSignal(GetTree().CreateTimer(0.10), SceneTreeTimer.SignalName.Timeout);
            GetTree().Quit();
        }
    }

    private static void EmitCue(string cue)
    {
        GameManager? game = GameManager.Instance;
        if (game is null)
        {
            return;
        }

        switch (cue)
        {
            case "card":
                game.EmitRuleEvent(RuleEventType.CardUsed, card: new CardInstance
                {
                    InstanceId = "fx-showcase-slash",
                    CardType = CardType.Slash,
                    DisplayName = "杀"
                }, value: 1);
                break;
            case "fire":
                game.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo: new DamageInfo(null, null, 1, DamageType.Fire), value: 1);
                break;
            case "thunder":
                game.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo: new DamageInfo(null, null, 1, DamageType.Thunder), value: 1);
                break;
            case "heal":
                game.EmitRuleEvent(RuleEventType.Healed, value: 1);
                break;
            case "response":
                game.EmitRuleEvent(RuleEventType.ResponseUsed, value: 1, responseKind: ResponseWindowKind.Dodge);
                break;
            case "defeat":
                game.EmitRuleEvent(RuleEventType.CharacterDefeated, value: 1);
                break;
            default:
                game.EmitRuleEvent(RuleEventType.DamageApplied, damageInfo: new DamageInfo(null, null, 1, DamageType.Physical), value: 1);
                break;
        }
    }

    private async Task EmitBurst()
    {
        string[] cues =
        {
            "card", "physical", "fire", "response", "thunder", "heal",
            "physical", "card", "fire", "thunder", "response", "heal",
            "physical", "fire", "thunder", "response", "card", "heal"
        };
        foreach (string cue in cues)
        {
            EmitCue(cue);
            await ToSignal(GetTree().CreateTimer(0.032), SceneTreeTimer.SignalName.Timeout);
        }
    }

    private void ApplyQualityOption(string value)
    {
        if (_director is null)
        {
            return;
        }

        BattleFxQuality quality = value.Trim().ToLowerInvariant() switch
        {
            "low" => BattleFxQuality.Low,
            "high" => BattleFxQuality.High,
            _ => BattleFxQuality.Balanced
        };
        _director.SetPresentationOptions(
            audioEnabled: true,
            visualEffectsEnabled: true,
            reducedMotion: false,
            sfxVolumeDb: -5f,
            screenFlashEnabled: true,
            effectQuality: quality);
    }

    private static Dictionary<string, string> ParseUserArgs()
    {
        Dictionary<string, string> args = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawArg in OS.GetCmdlineUserArgs())
        {
            if (string.IsNullOrWhiteSpace(rawArg) || !rawArg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = rawArg[2..].Split('=', 2);
            args[parts[0]] = parts.Length == 2 ? parts[1] : "true";
        }

        return args;
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
