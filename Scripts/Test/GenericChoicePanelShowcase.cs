using System;
using System.Collections.Generic;
using System.IO;
using CiyuanSha.UI;
using Godot;

namespace CiyuanSha.Test;

public partial class GenericChoicePanelShowcase : Control
{
    private const string BackdropPath = "res://Assets/UI/InkBattle/ink_courtyard_match_v1.png";

    public override void _Ready()
    {
        CyberStyle.ApplyRootTheme(this);
        BuildStage();
        CallDeferred(nameof(CaptureAndQuit));
    }

    private void BuildStage()
    {
        TextureRect backdrop = new()
        {
            Texture = GD.Load<Texture2D>(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = new Color(0.70f, 0.73f, 0.73f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        ColorRect wash = new()
        {
            Color = new Color(0.008f, 0.012f, 0.014f, 0.48f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        wash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(wash);

        Label title = new()
        {
            Text = "次元杀 · 统一选择接口",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        title.AnchorLeft = 0.25f;
        title.AnchorRight = 0.75f;
        title.AnchorTop = 0.10f;
        title.AnchorBottom = 0.22f;
        CyberStyle.ApplyLabel(title, title: true);
        title.AddThemeFontSizeOverride("font_size", 34);
        AddChild(title);

        AddChild(new GenericChoicePanel { PreviewMode = true });
    }

    private async void CaptureAndQuit()
    {
        Dictionary<string, string> args = ParseUserArgs();
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        string capturePath = args.GetValueOrDefault("capture", string.Empty);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            string? directory = Path.GetDirectoryName(capturePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            Image? image = GetViewport().GetTexture()?.GetImage();
            if (image is null)
            {
                GD.PushError("[choice-showcase] The active renderer cannot capture the viewport.");
                GetTree().Quit(1);
                return;
            }
            Error error = image.SavePng(capturePath);
            GD.Print($"[choice-showcase] Capture: {error} -> {capturePath}");
            if (error != Error.Ok)
            {
                GetTree().Quit(1);
                return;
            }
        }
        if (args.ContainsKey("auto-quit"))
        {
            GetTree().Quit();
        }
    }

    private static Dictionary<string, string> ParseUserArgs()
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            int equals = argument.IndexOf('=');
            result[equals > 2 ? argument[2..equals] : argument[2..]] = equals > 2 ? argument[(equals + 1)..] : "true";
        }
        return result;
    }
}
