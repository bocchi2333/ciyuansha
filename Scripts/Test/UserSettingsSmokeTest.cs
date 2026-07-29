using CiyuanSha.Settings;
using Godot;

namespace CiyuanSha.Test;

/// <summary>
/// Verifies settings persistence without modifying the player's real settings file.
/// </summary>
public partial class UserSettingsSmokeTest : Node
{
    private const string TestPath = "user://ciyuansha_settings_smoke.cfg";

    public override void _Ready()
    {
        CallDeferred(nameof(RunSmokeTest));
    }

    private void RunSmokeTest()
    {
        GameUserSettings? settings = GameUserSettings.Instance;
        if (settings is null)
        {
            Fail("GameUserSettings autoload is unavailable.");
            return;
        }

        GameUserSettingsSnapshot original = settings.CaptureSnapshot();
        try
        {
            GameUserSettingsSnapshot expected = new(
                audioEnabled: false,
                sfxVolumePercent: 37f,
                visualEffectsEnabled: true,
                screenFlashEnabled: false,
                reducedMotion: true,
                effectQuality: BattleFxQuality.High);
            settings.ApplySnapshot(expected);
            if (settings.SaveToPath(TestPath) != Error.Ok)
            {
                Fail("Unable to save temporary settings.");
                return;
            }

            settings.ResetToDefaults(save: false);
            if (settings.LoadFromPath(TestPath) != Error.Ok || !Matches(settings, expected))
            {
                Fail("Settings round-trip mismatch.");
                return;
            }

            ConfigFile invalid = new();
            invalid.SetValue("presentation", "sfx_volume_percent", 250f);
            invalid.SetValue("presentation", "effect_quality", 99);
            if (invalid.Save(TestPath) != Error.Ok || settings.LoadFromPath(TestPath) != Error.Ok)
            {
                Fail("Unable to load clamping fixture.");
                return;
            }

            if (!Mathf.IsEqualApprox(settings.SfxVolumePercent, 100f)
                || settings.EffectQuality != BattleFxQuality.High)
            {
                Fail("Invalid settings were not clamped.");
                return;
            }

            GD.Print("[settings-smoke] PASS: round-trip, defaults, and clamping verified.");
            GetTree().Quit(0);
        }
        finally
        {
            settings.ApplySnapshot(original);
            string absolutePath = ProjectSettings.GlobalizePath(TestPath);
            if (FileAccess.FileExists(TestPath))
            {
                DirAccess.RemoveAbsolute(absolutePath);
            }
        }
    }

    private static bool Matches(GameUserSettings settings, GameUserSettingsSnapshot expected)
    {
        return settings.AudioEnabled == expected.AudioEnabled
            && Mathf.IsEqualApprox(settings.SfxVolumePercent, expected.SfxVolumePercent)
            && settings.VisualEffectsEnabled == expected.VisualEffectsEnabled
            && settings.ScreenFlashEnabled == expected.ScreenFlashEnabled
            && settings.ReducedMotion == expected.ReducedMotion
            && settings.EffectQuality == expected.EffectQuality;
    }

    private void Fail(string message)
    {
        GD.PushError($"[settings-smoke] FAIL: {message}");
        GetTree().Quit(1);
    }
}
