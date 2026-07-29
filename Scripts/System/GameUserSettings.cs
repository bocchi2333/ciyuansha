using System;
using Godot;

namespace CiyuanSha.Settings;

public enum BattleFxQuality
{
    Low = 0,
    Balanced = 1,
    High = 2
}

public readonly struct GameUserSettingsSnapshot
{
    public GameUserSettingsSnapshot(
        bool audioEnabled,
        float sfxVolumePercent,
        bool visualEffectsEnabled,
        bool screenFlashEnabled,
        bool reducedMotion,
        BattleFxQuality effectQuality)
    {
        AudioEnabled = audioEnabled;
        SfxVolumePercent = sfxVolumePercent;
        VisualEffectsEnabled = visualEffectsEnabled;
        ScreenFlashEnabled = screenFlashEnabled;
        ReducedMotion = reducedMotion;
        EffectQuality = effectQuality;
    }

    public bool AudioEnabled { get; }

    public float SfxVolumePercent { get; }

    public bool VisualEffectsEnabled { get; }

    public bool ScreenFlashEnabled { get; }

    public bool ReducedMotion { get; }

    public BattleFxQuality EffectQuality { get; }
}

/// <summary>
/// Stores presentation-only preferences in the local Godot user directory.
/// </summary>
public partial class GameUserSettings : Node
{
    public const string SettingsPath = "user://ciyuansha_settings.cfg";

    private const string PresentationSection = "presentation";
    private const float DefaultSfxVolumePercent = 70f;

    public static GameUserSettings? Instance { get; private set; }

    public event Action? OnChanged;

    public bool AudioEnabled { get; private set; } = true;

    public float SfxVolumePercent { get; private set; } = DefaultSfxVolumePercent;

    public bool VisualEffectsEnabled { get; private set; } = true;

    public bool ScreenFlashEnabled { get; private set; } = true;

    public bool ReducedMotion { get; private set; }

    public BattleFxQuality EffectQuality { get; private set; } = BattleFxQuality.Balanced;

    public float SfxVolumeDb => SfxVolumePercent <= 0.01f
        ? -80f
        : 20f * MathF.Log10(SfxVolumePercent / 100f);

    public override void _EnterTree()
    {
        Instance = this;
        LoadSettings();
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public GameUserSettingsSnapshot CaptureSnapshot()
    {
        return new GameUserSettingsSnapshot(
            AudioEnabled,
            SfxVolumePercent,
            VisualEffectsEnabled,
            ScreenFlashEnabled,
            ReducedMotion,
            EffectQuality);
    }

    public void SetAudioEnabled(bool enabled)
    {
        if (AudioEnabled == enabled)
        {
            return;
        }

        AudioEnabled = enabled;
        NotifyChanged();
    }

    public void SetSfxVolumePercent(float percent)
    {
        float clamped = Mathf.Clamp(percent, 0f, 100f);
        if (Mathf.IsEqualApprox(SfxVolumePercent, clamped))
        {
            return;
        }

        SfxVolumePercent = clamped;
        NotifyChanged();
    }

    public void SetVisualEffectsEnabled(bool enabled)
    {
        if (VisualEffectsEnabled == enabled)
        {
            return;
        }

        VisualEffectsEnabled = enabled;
        NotifyChanged();
    }

    public void SetScreenFlashEnabled(bool enabled)
    {
        if (ScreenFlashEnabled == enabled)
        {
            return;
        }

        ScreenFlashEnabled = enabled;
        NotifyChanged();
    }

    public void SetReducedMotion(bool enabled)
    {
        if (ReducedMotion == enabled)
        {
            return;
        }

        ReducedMotion = enabled;
        NotifyChanged();
    }

    public void SetEffectQuality(BattleFxQuality quality)
    {
        BattleFxQuality normalized = NormalizeQuality((int)quality);
        if (EffectQuality == normalized)
        {
            return;
        }

        EffectQuality = normalized;
        NotifyChanged();
    }

    public void ResetToDefaults(bool save = true)
    {
        ApplySnapshot(CreateDefaultSnapshot());
        if (save)
        {
            SaveSettings();
        }
    }

    public void ApplySnapshot(GameUserSettingsSnapshot snapshot, bool notify = true)
    {
        AudioEnabled = snapshot.AudioEnabled;
        SfxVolumePercent = Mathf.Clamp(snapshot.SfxVolumePercent, 0f, 100f);
        VisualEffectsEnabled = snapshot.VisualEffectsEnabled;
        ScreenFlashEnabled = snapshot.ScreenFlashEnabled;
        ReducedMotion = snapshot.ReducedMotion;
        EffectQuality = NormalizeQuality((int)snapshot.EffectQuality);

        if (notify)
        {
            NotifyChanged();
        }
    }

    public Error SaveSettings()
    {
        return SaveToPath(SettingsPath);
    }

    public Error SaveToPath(string path)
    {
        ConfigFile config = new();
        config.SetValue(PresentationSection, "audio_enabled", AudioEnabled);
        config.SetValue(PresentationSection, "sfx_volume_percent", SfxVolumePercent);
        config.SetValue(PresentationSection, "visual_effects_enabled", VisualEffectsEnabled);
        config.SetValue(PresentationSection, "screen_flash_enabled", ScreenFlashEnabled);
        config.SetValue(PresentationSection, "reduced_motion", ReducedMotion);
        config.SetValue(PresentationSection, "effect_quality", (int)EffectQuality);

        Error error = config.Save(path);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Unable to save user settings to '{path}': {error}.");
        }

        return error;
    }

    public Error LoadFromPath(string path, bool notify = true)
    {
        ConfigFile config = new();
        Error error = config.Load(path);
        if (error != Error.Ok)
        {
            return error;
        }

        GameUserSettingsSnapshot snapshot = new(
            config.GetValue(PresentationSection, "audio_enabled", true).AsBool(),
            (float)config.GetValue(PresentationSection, "sfx_volume_percent", DefaultSfxVolumePercent).AsDouble(),
            config.GetValue(PresentationSection, "visual_effects_enabled", true).AsBool(),
            config.GetValue(PresentationSection, "screen_flash_enabled", true).AsBool(),
            config.GetValue(PresentationSection, "reduced_motion", false).AsBool(),
            NormalizeQuality(config.GetValue(PresentationSection, "effect_quality", (int)BattleFxQuality.Balanced).AsInt32()));
        ApplySnapshot(snapshot, notify);
        return Error.Ok;
    }

    private void LoadSettings()
    {
        if (!FileAccess.FileExists(SettingsPath))
        {
            return;
        }

        Error error = LoadFromPath(SettingsPath, notify: false);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Unable to load user settings from '{SettingsPath}': {error}. Defaults remain active.");
            ApplySnapshot(CreateDefaultSnapshot(), notify: false);
        }
    }

    private void NotifyChanged()
    {
        OnChanged?.Invoke();
    }

    private static GameUserSettingsSnapshot CreateDefaultSnapshot()
    {
        return new GameUserSettingsSnapshot(
            audioEnabled: true,
            sfxVolumePercent: DefaultSfxVolumePercent,
            visualEffectsEnabled: true,
            screenFlashEnabled: true,
            reducedMotion: false,
            effectQuality: BattleFxQuality.Balanced);
    }

    private static BattleFxQuality NormalizeQuality(int value)
    {
        return value switch
        {
            <= (int)BattleFxQuality.Low => BattleFxQuality.Low,
            >= (int)BattleFxQuality.High => BattleFxQuality.High,
            _ => BattleFxQuality.Balanced
        };
    }
}
