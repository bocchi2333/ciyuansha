using System;
using System.Collections.Generic;
using System.IO;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.Replay;
using Godot;

namespace CiyuanSha.Test;

public partial class ReplayViewerShowcase : Control
{
    public override void _Ready() => CallDeferred(nameof(Run));

    private async void Run()
    {
        Dictionary<string, string> args = ParseUserArgs();
        string replayPath = args.GetValueOrDefault("replay", string.Empty);
        ReplayViewerPanel panel = GetNode<ReplayViewerPanel>("ReplayViewerPanel");
        if (!panel.OpenReplay(replayPath) || panel.Session is not ReplayPlaybackSession session)
        {
            Fail("Replay viewer could not load the requested archive.");
            return;
        }

        ReplayVerificationResult verification = session.Verify();
        if (!verification.IsValid)
        {
            Fail($"Replay verification failed: {verification.ErrorKey} at {verification.DivergentSequence}.");
            return;
        }

        ReplayController controller = panel.GetNode<ReplayController>("ReplayController");
        controller.Seek(session.MaximumCursor / 2);
        controller.ShowPublicView();
        controller.SetSpeed(2);
        controller.StepForward();
        controller.Pause();
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);

        string capturePath = args.GetValueOrDefault("capture", string.Empty);
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            string? directory = Path.GetDirectoryName(capturePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            Image? image = GetViewport().GetTexture()?.GetImage();
            if (image is null || image.SavePng(capturePath) != Error.Ok)
            {
                Fail("Replay viewer screenshot failed.");
                return;
            }
        }

        GD.Print($"[replay-viewer-showcase] PASS: cursor={session.Cursor}/{session.MaximumCursor}; view={session.Viewer.Role}");
        GetTree().Quit(0);
    }

    private void Fail(string message)
    {
        GD.PushError($"[replay-viewer-showcase] FAIL: {message}");
        GetTree().Quit(1);
    }

    private static Dictionary<string, string> ParseUserArgs()
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;
            int equals = argument.IndexOf('=');
            result[equals > 2 ? argument[2..equals] : argument[2..]] = equals > 2 ? argument[(equals + 1)..] : "true";
        }
        return result;
    }
}
