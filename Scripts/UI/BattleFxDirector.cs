using System;
using System.Collections.Generic;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using CiyuanSha.Settings;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Coordinates presentation-only battle animation and sound without affecting rules.
/// </summary>
public partial class BattleFxDirector : Control
{
    private const string TextureRoot = "res://Assets/FX/Textures/";
    private const string AudioRoot = "res://Assets/FX/Audio/";

    private const string BaseGlowTexture = TextureRoot + "base_glow.png";
    private const string ImpactFlareTexture = TextureRoot + "impact_flare_flipbook.png";
    private const string RoundSmokeTexture = TextureRoot + "round_smoke_flipbook.png";
    private const string SlashSmokeTexture = TextureRoot + "slash_smoke_flipbook.png";
    private const string BloodyImpactTexture = TextureRoot + "bloody_impact_flipbook.png";
    private const string FireImpactTexture = TextureRoot + "fire_impact_flipbook.png";
    private const string LightningTexture = TextureRoot + "lightning_flipbook.png";
    private const string HealTexture = TextureRoot + "intent_heal.png";
    private const string GroundSparkTexture = TextureRoot + "ground_spark.png";

    private const string BattleStartAudio = AudioRoot + "battle_start.mp3";
    private const string CardDealAudio = AudioRoot + "card_deal.mp3";
    private const string CardSelectAudio = AudioRoot + "card_select.mp3";
    private const string PhysicalImpactAudio = AudioRoot + "physical_impact.mp3";
    private const string FireAudio = AudioRoot + "fire.mp3";
    private const string ThunderAudio = AudioRoot + "thunder.mp3";
    private const string HealAudio = AudioRoot + "heal.mp3";
    private const string DefeatAudio = AudioRoot + "defeat.mp3";

    private const string GeneralCardGroup = "ciyuansha_general_card_views";

    [Export]
    public bool AudioEnabled { get; set; } = true;

    [Export]
    public bool VisualEffectsEnabled { get; set; } = true;

    [Export]
    public bool ReducedMotion { get; set; }

    [Export]
    public bool ScreenFlashEnabled { get; set; } = true;

    [Export]
    public BattleFxQuality EffectQuality { get; set; } = BattleFxQuality.Balanced;

    [Export]
    public bool UseUserSettings { get; set; } = true;

    [Export(PropertyHint.Range, "-80,6,0.5")]
    public float SfxVolumeDb { get; set; } = -5f;

    [Export(PropertyHint.Range, "2,8,1")]
    public int AudioVoiceCount { get; set; } = 7;

    public int ActiveVisualCount => _activeVisuals.Count;

    public int CurrentVisualBudget => GetVisualBudget();

    private readonly Dictionary<string, Texture2D> _textureCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AudioStream> _audioCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastAudioAtMsec = new(StringComparer.Ordinal);
    private readonly Dictionary<int, long> _lastCueAtMsec = new();
    private readonly Dictionary<int, int> _cueBurstOrdinal = new();
    private readonly List<AudioStreamPlayer> _audioPlayers = new();
    private readonly LinkedList<Node> _activeVisuals = new();
    private readonly Random _random = new(5182026);
    private Control? _fxRoot;
    private ColorRect? _vignette;
    private int _nextAudioVoice;
    private long _lastBattleStartAtMsec = -10_000;
    private Shader? _additiveTintShader;
    private Shader? _alphaTintShader;
    private Tween? _vignetteTween;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildVisualLayer();
        BuildAudioPool();
        if (UseUserSettings)
        {
            ApplyUserSettings();
            BindUserSettings();
        }

        BindGameManager();
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnPresentationEvent -= HandlePresentationEvent;
            GameManager.Instance.OnMatchRunningChanged -= HandleMatchRunningChanged;
        }

        if (UseUserSettings && GameUserSettings.Instance is not null)
        {
            GameUserSettings.Instance.OnChanged -= HandleUserSettingsChanged;
        }

        _vignetteTween?.Kill();
        ClearActiveVisuals();
        StopAllAudio();
        _textureCache.Clear();
        _additiveTintShader = null;
        _alphaTintShader = null;
    }

    public void SetPresentationOptions(bool audioEnabled, bool visualEffectsEnabled, bool reducedMotion, float sfxVolumeDb)
    {
        SetPresentationOptions(
            audioEnabled,
            visualEffectsEnabled,
            reducedMotion,
            sfxVolumeDb,
            screenFlashEnabled: true,
            BattleFxQuality.Balanced);
    }

    public void SetPresentationOptions(
        bool audioEnabled,
        bool visualEffectsEnabled,
        bool reducedMotion,
        float sfxVolumeDb,
        bool screenFlashEnabled,
        BattleFxQuality effectQuality)
    {
        AudioEnabled = audioEnabled;
        VisualEffectsEnabled = visualEffectsEnabled;
        ReducedMotion = reducedMotion;
        ScreenFlashEnabled = screenFlashEnabled;
        EffectQuality = effectQuality;
        SfxVolumeDb = Mathf.Clamp(sfxVolumeDb, -80f, 6f);
        if (!AudioEnabled)
        {
            StopAllAudio();
        }

        if (!VisualEffectsEnabled)
        {
            ClearActiveVisuals();
            ResetVignette();
        }

        TrimVisualBudget();
    }

    public void StopAllAudio()
    {
        foreach (AudioStreamPlayer player in _audioPlayers)
        {
            player.Stop();
            player.Stream = null;
        }

        _audioCache.Clear();
        _lastAudioAtMsec.Clear();
    }

    private void BindUserSettings()
    {
        if (GameUserSettings.Instance is not null)
        {
            GameUserSettings.Instance.OnChanged += HandleUserSettingsChanged;
        }
    }

    private void HandleUserSettingsChanged()
    {
        ApplyUserSettings();
    }

    private void ApplyUserSettings()
    {
        GameUserSettings? settings = GameUserSettings.Instance;
        if (settings is null)
        {
            return;
        }

        SetPresentationOptions(
            settings.AudioEnabled,
            settings.VisualEffectsEnabled,
            settings.ReducedMotion,
            settings.SfxVolumeDb,
            settings.ScreenFlashEnabled,
            settings.EffectQuality);
    }

    private void BuildVisualLayer()
    {
        _fxRoot = new Control
        {
            Name = "BattleFxCanvas",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 72
        };
        _fxRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fxRoot);

        _vignette = new ColorRect
        {
            Name = "BattleFxVignette",
            MouseFilter = MouseFilterEnum.Ignore,
            Color = Colors.Transparent,
            Modulate = new Color(1f, 1f, 1f, 0f)
        };
        _vignette.SetAnchorsPreset(LayoutPreset.FullRect);
        _fxRoot.AddChild(_vignette);
    }

    private void BuildAudioPool()
    {
        int voiceCount = Mathf.Clamp(AudioVoiceCount, 2, 8);
        for (int index = 0; index < voiceCount; index++)
        {
            AudioStreamPlayer player = new()
            {
                Name = $"BattleSfxVoice{index + 1}",
                Bus = "Master"
            };
            AddChild(player);
            _audioPlayers.Add(player);
        }
    }

    private void BindGameManager()
    {
        if (GameManager.Instance is null)
        {
            return;
        }

        GameManager.Instance.OnPresentationEvent += HandlePresentationEvent;
        GameManager.Instance.OnMatchRunningChanged += HandleMatchRunningChanged;
    }

    private void HandleMatchRunningChanged(bool isRunning)
    {
        if (isRunning)
        {
            PlayMatchStart();
        }
    }

    private void HandlePresentationEvent(NetworkPresentationEvent presentationEvent)
    {
        if (!Enum.TryParse(presentationEvent.EventType, true, out RuleEventType eventType))
        {
            return;
        }

        switch (eventType)
        {
            case RuleEventType.MatchStarted:
                PlayMatchStart();
                break;
            case RuleEventType.CardUsed:
                PlayCardUsed(presentationEvent);
                break;
            case RuleEventType.DamageApplied:
                PlayDamage(presentationEvent);
                break;
            case RuleEventType.Healed:
                PlayHeal(presentationEvent);
                break;
            case RuleEventType.ResponseUsed:
                PlayResponse(presentationEvent);
                break;
            case RuleEventType.CharacterDefeated:
                PlayDefeat(presentationEvent);
                break;
            case RuleEventType.CardsDrawn:
                PlaySfx(CardDealAudio, -7f, RandomPitch(0.96f, 1.05f));
                break;
            case RuleEventType.CardsDiscarded:
                PlaySfx(CardSelectAudio, -9f, RandomPitch(0.82f, 0.92f));
                break;
            case RuleEventType.EquipmentEquipped:
                PlaySfx(CardDealAudio, -4f, RandomPitch(0.84f, 0.92f));
                PlayGoldPulse(ResolveCuePosition(presentationEvent.TargetPeerId), 170f);
                break;
            case RuleEventType.JudgementPerformed:
                PlaySfx(CardDealAudio, -6f, RandomPitch(0.72f, 0.80f));
                PlayFrameSprite(ImpactFlareTexture, 2, 2, 4, ResolveCuePosition(presentationEvent.TargetPeerId), 170f, CyberStyle.GoldBright, 0.24f, true);
                break;
        }
    }

    private void PlayMatchStart()
    {
        long now = (long)Time.GetTicksMsec();
        if (now - _lastBattleStartAtMsec < 800)
        {
            return;
        }

        _lastBattleStartAtMsec = now;
        PlaySfx(BattleStartAudio, -2f, 1f);
        if (!VisualEffectsEnabled)
        {
            return;
        }

        Vector2 center = GetStageCenter();
        PlayVignette(new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 1f), 0.13f, 0.65f);
        if (UseSecondaryEffects)
        {
            PlayFrameSprite(RoundSmokeTexture, 2, 2, 4, center, 560f, new Color(0.52f, 0.44f, 0.32f, 0.68f), 0.75f, false);
        }

        PlayStaticSprite(BaseGlowTexture, center, 430f, new Color(CyberStyle.GoldBright.R, CyberStyle.GoldBright.G, CyberStyle.GoldBright.B, 0.58f), 0.62f, true);
    }

    private void PlayCardUsed(NetworkPresentationEvent presentationEvent)
    {
        PlaySfx(CardSelectAudio, -3f, RandomPitch(0.96f, 1.06f));
        if (!VisualEffectsEnabled || _fxRoot is null)
        {
            return;
        }

        CardType cardType = ParseCardType(presentationEvent.CardType);
        string cardName = string.IsNullOrWhiteSpace(presentationEvent.DisplayName)
            ? CardDisplayFormatter.FormatCardTypeName(cardType)
            : presentationEvent.DisplayName;
        Color accent = GetCardAccent(cardType);
        Vector2 start = new(Size.X * 0.50f - 48f, Size.Y * 0.82f - 64f);
        Vector2 target = GetStageCenter() - new Vector2(48f, 64f);

        Panel cardShadow = new()
        {
            Name = "CardCastShadow",
            MouseFilter = MouseFilterEnum.Ignore,
            Position = start + new Vector2(8f, 10f),
            Size = new Vector2(96f, 128f),
            Modulate = new Color(0f, 0f, 0f, 0.48f),
            RotationDegrees = -7f
        };
        CyberStyle.ApplyPanel(cardShadow, CyberPanelKind.Card);
        _fxRoot.AddChild(cardShadow);
        TrackVisual(cardShadow);

        Panel card = new()
        {
            Name = "CardCast",
            MouseFilter = MouseFilterEnum.Ignore,
            Position = start,
            Size = new Vector2(96f, 128f),
            RotationDegrees = -7f
        };
        CyberStyle.ApplyPanel(card, CyberPanelKind.Card);
        _fxRoot.AddChild(card);
        TrackVisual(card);

        ColorRect accentLine = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = accent
        };
        accentLine.AnchorRight = 1f;
        accentLine.OffsetBottom = 5f;
        card.AddChild(accentLine);

        Label cardLabel = new()
        {
            Text = cardName,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        cardLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        cardLabel.OffsetLeft = 8f;
        cardLabel.OffsetTop = 12f;
        cardLabel.OffsetRight = -8f;
        cardLabel.OffsetBottom = -12f;
        CyberStyle.ApplyLabel(cardLabel, title: true, color: accent.Lightened(0.18f));
        cardLabel.AddThemeFontSizeOverride("font_size", 21);
        card.AddChild(cardLabel);

        AnimateFlyingCard(cardShadow, target + new Vector2(8f, 10f), 0.32f, 0.04f);
        AnimateFlyingCard(card, target, 0.32f, 0f);
        PlayStaticSprite(BaseGlowTexture, GetStageCenter(), 250f, new Color(accent.R, accent.G, accent.B, 0.50f), 0.42f, true);
    }

    private void AnimateFlyingCard(Control card, Vector2 target, float duration, float delay)
    {
        card.PivotOffset = card.Size / 2f;
        card.Scale = new Vector2(0.78f, 0.78f);
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(card, "position", target, ReducedMotion ? 0.10f : duration)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quart)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "rotation_degrees", 0f, ReducedMotion ? 0.10f : duration)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "scale", new Vector2(1.06f, 1.06f), ReducedMotion ? 0.10f : duration)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "modulate", new Color(1f, 1f, 1f, 0f), 0.18f)
            .SetDelay(delay + (ReducedMotion ? 0.12f : duration + 0.12f));
        tween.Finished += () => SafeQueueFree(card);
    }

    private void PlayDamage(NetworkPresentationEvent presentationEvent)
    {
        DamageType damageType = ParseDamageType(presentationEvent.DamageType);
        Vector2 position = ResolveCuePosition(presentationEvent.TargetPeerId);
        switch (damageType)
        {
            case DamageType.Fire:
                PlaySfx(FireAudio, -1f, RandomPitch(0.94f, 1.05f));
                PlayVignette(CyberStyle.Fire, 0.18f, 0.40f);
                PlayStaticSprite(BaseGlowTexture, position, 390f, new Color(CyberStyle.Fire.R, CyberStyle.Fire.G, CyberStyle.Fire.B, 0.72f), 0.42f, true);
                PlayFrameSprite(FireImpactTexture, 3, 2, 6, position + new Vector2(0f, 32f), 360f, CyberStyle.Fire.Lightened(0.12f), 0.38f, true);
                if (UseSecondaryEffects)
                {
                    PlayFrameSprite(RoundSmokeTexture, 2, 2, 4, position, 300f, new Color(0.40f, 0.16f, 0.06f, 0.64f), 0.48f, false);
                }

                break;
            case DamageType.Thunder:
                PlaySfx(ThunderAudio, -1f, RandomPitch(0.96f, 1.04f));
                PlayVignette(CyberStyle.Thunder, 0.20f, 0.34f);
                PlayStaticSprite(BaseGlowTexture, position, 440f, new Color(CyberStyle.Thunder.R, CyberStyle.Thunder.G, CyberStyle.Thunder.B, 0.74f), 0.36f, true);
                PlayFrameSprite(LightningTexture, 3, 3, 8, position, 470f, CyberStyle.Thunder.Lightened(0.22f), 0.34f, true);
                if (UseSecondaryEffects)
                {
                    PlayFrameSprite(ImpactFlareTexture, 2, 2, 4, position, 200f, new Color(0.78f, 0.68f, 1f, 0.90f), 0.20f, true);
                }

                break;
            default:
                PlaySfx(PhysicalImpactAudio, -2f, RandomPitch(0.92f, 1.06f));
                PlayVignette(CyberStyle.Vermilion, 0.15f, 0.32f);
                PlayFrameSprite(SlashSmokeTexture, 4, 2, 8, position, 390f, new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.86f), 0.34f, false, -12f);
                if (UseSecondaryEffects)
                {
                    PlayFrameSprite(BloodyImpactTexture, 5, 2, 10, position, 330f, CyberStyle.Vermilion.Lightened(0.08f), 0.32f, true);
                    PlayFrameSprite(ImpactFlareTexture, 2, 2, 4, position, 190f, CyberStyle.PaperBright, 0.18f, true);
                    PlayStaticSprite(GroundSparkTexture, position + new Vector2(34f, 30f), 170f, new Color(CyberStyle.GoldBright.R, CyberStyle.GoldBright.G, CyberStyle.GoldBright.B, 0.74f), 0.27f, true);
                }

                break;
        }
    }

    private void PlayHeal(NetworkPresentationEvent presentationEvent)
    {
        PlaySfx(HealAudio, -2f, RandomPitch(0.97f, 1.05f));
        Vector2 position = ResolveCuePosition(presentationEvent.TargetPeerId);
        PlayVignette(CyberStyle.NeonJade, 0.09f, 0.46f);
        PlayStaticSprite(BaseGlowTexture, position, 310f, new Color(CyberStyle.NeonJade.R, CyberStyle.NeonJade.G, CyberStyle.NeonJade.B, 0.66f), 0.55f, true);
        PlayStaticSprite(HealTexture, position, 102f, CyberStyle.NeonJade.Lightened(0.24f), 0.62f, true, new Vector2(0f, -42f));
        if (UseSecondaryEffects)
        {
            PlayRisingMotes(position, CyberStyle.NeonJade.Lightened(0.22f));
        }
    }

    private void PlayResponse(NetworkPresentationEvent presentationEvent)
    {
        PlaySfx(CardSelectAudio, -1f, RandomPitch(1.08f, 1.18f));
        Vector2 position = presentationEvent.TargetPeerId > 0
            ? ResolveCuePosition(presentationEvent.TargetPeerId)
            : GetStageCenter();
        PlayGoldPulse(position, 240f);
        if (UseSecondaryEffects)
        {
            PlayFrameSprite(ImpactFlareTexture, 2, 2, 4, position, 150f, CyberStyle.GoldBright, 0.22f, true);
        }
    }

    private void PlayDefeat(NetworkPresentationEvent presentationEvent)
    {
        PlaySfx(DefeatAudio, -1f, RandomPitch(0.96f, 1.01f));
        Vector2 position = ResolveCuePosition(presentationEvent.TargetPeerId);
        PlayVignette(CyberStyle.BloodInk, 0.30f, 0.86f);
        if (UseSecondaryEffects)
        {
            PlayFrameSprite(RoundSmokeTexture, 2, 2, 4, position, 480f, new Color(0.12f, 0.10f, 0.09f, 0.88f), 0.80f, false);
        }

        PlayStaticSprite(BaseGlowTexture, position, 350f, new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.48f), 0.70f, true);
    }

    private void PlayGoldPulse(Vector2 position, float size)
    {
        PlayStaticSprite(BaseGlowTexture, position, size, new Color(CyberStyle.GoldBright.R, CyberStyle.GoldBright.G, CyberStyle.GoldBright.B, 0.62f), 0.44f, true);
    }

    private void PlayFrameSprite(
        string texturePath,
        int columns,
        int rows,
        int frameCount,
        Vector2 position,
        float targetSize,
        Color tint,
        float duration,
        bool additive,
        float rotationDegrees = 0f)
    {
        if (!VisualEffectsEnabled || _fxRoot is null)
        {
            return;
        }

        Texture2D? texture = LoadTexture(texturePath);
        if (texture is null)
        {
            return;
        }

        Sprite2D sprite = new()
        {
            Texture = texture,
            Hframes = Math.Max(1, columns),
            Vframes = Math.Max(1, rows),
            Frame = 0,
            Position = position,
            RotationDegrees = rotationDegrees,
            Centered = true,
            Material = CreateTintMaterial(tint, additive)
        };
        float frameWidth = texture.GetWidth() / (float)Math.Max(1, columns);
        float frameHeight = texture.GetHeight() / (float)Math.Max(1, rows);
        float scale = targetSize / Math.Max(frameWidth, frameHeight);
        sprite.Scale = new Vector2(scale * 0.72f, scale * 0.72f);
        sprite.Modulate = new Color(1f, 1f, 1f, 0f);
        _fxRoot.AddChild(sprite);
        TrackVisual(sprite);

        float resolvedDuration = ReducedMotion ? Math.Min(0.18f, duration) : duration;
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(sprite, "frame", Math.Max(0, frameCount - 1), resolvedDuration)
            .SetTrans(Tween.TransitionType.Linear);
        tween.TweenProperty(sprite, "scale", new Vector2(scale, scale), resolvedDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "modulate", Colors.White, Math.Min(0.07f, resolvedDuration * 0.4f));
        tween.TweenProperty(sprite, "modulate", new Color(1f, 1f, 1f, 0f), Math.Min(0.16f, resolvedDuration * 0.48f))
            .SetDelay(Math.Max(0.04f, resolvedDuration * 0.55f));
        tween.Finished += () => SafeQueueFree(sprite);
    }

    private void PlayStaticSprite(
        string texturePath,
        Vector2 position,
        float targetSize,
        Color tint,
        float duration,
        bool additive,
        Vector2? travel = null)
    {
        if (!VisualEffectsEnabled || _fxRoot is null)
        {
            return;
        }

        Texture2D? texture = LoadTexture(texturePath);
        if (texture is null)
        {
            return;
        }

        Sprite2D sprite = new()
        {
            Texture = texture,
            Position = position,
            Centered = true,
            Material = CreateTintMaterial(tint, additive),
            Modulate = new Color(1f, 1f, 1f, 0f)
        };
        float scale = targetSize / Math.Max(texture.GetWidth(), texture.GetHeight());
        sprite.Scale = new Vector2(scale * 0.62f, scale * 0.62f);
        _fxRoot.AddChild(sprite);
        TrackVisual(sprite);

        float resolvedDuration = ReducedMotion ? Math.Min(0.20f, duration) : duration;
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(sprite, "modulate", Colors.White, Math.Min(0.08f, resolvedDuration * 0.35f));
        tween.TweenProperty(sprite, "scale", new Vector2(scale, scale), resolvedDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        if (travel.HasValue && !ReducedMotion)
        {
            tween.TweenProperty(sprite, "position", position + travel.Value, resolvedDuration)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
        }
        tween.TweenProperty(sprite, "modulate", new Color(1f, 1f, 1f, 0f), Math.Min(0.20f, resolvedDuration * 0.48f))
            .SetDelay(Math.Max(0.05f, resolvedDuration * 0.55f));
        tween.Finished += () => SafeQueueFree(sprite);
    }

    private void PlayRisingMotes(Vector2 origin, Color color)
    {
        if (!VisualEffectsEnabled || _fxRoot is null || ReducedMotion)
        {
            return;
        }

        int moteCount = EffectQuality == BattleFxQuality.High ? 10 : 6;
        for (int index = 0; index < moteCount; index++)
        {
            float size = 4f + (float)_random.NextDouble() * 5f;
            ColorRect mote = new()
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = new Color(color.R, color.G, color.B, 0.82f),
                Position = origin + new Vector2(-42f + (float)_random.NextDouble() * 84f, 12f + (float)_random.NextDouble() * 30f),
                Size = new Vector2(size, size),
                Modulate = new Color(1f, 1f, 1f, 0f)
            };
            _fxRoot.AddChild(mote);
            TrackVisual(mote);
            Vector2 destination = mote.Position + new Vector2(-12f + (float)_random.NextDouble() * 24f, -66f - (float)_random.NextDouble() * 42f);
            Tween tween = CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(mote, "position", destination, 0.62f + index * 0.025f)
                .SetDelay(index * 0.025f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(mote, "modulate", Colors.White, 0.10f).SetDelay(index * 0.025f);
            tween.TweenProperty(mote, "modulate", new Color(1f, 1f, 1f, 0f), 0.22f).SetDelay(0.40f + index * 0.025f);
            tween.Finished += () => SafeQueueFree(mote);
        }
    }

    private void PlayVignette(Color color, float peakAlpha, float duration)
    {
        if (!VisualEffectsEnabled || !ScreenFlashEnabled || _vignette is null)
        {
            return;
        }

        _vignette.Color = new Color(color.R, color.G, color.B, 1f);
        _vignette.Modulate = new Color(1f, 1f, 1f, 0f);
        _vignette.MoveToFront();
        float resolvedDuration = ReducedMotion ? Math.Min(0.20f, duration) : duration;
        _vignetteTween?.Kill();
        _vignetteTween = CreateTween();
        _vignetteTween.TweenProperty(_vignette, "modulate", new Color(1f, 1f, 1f, peakAlpha), Math.Min(0.06f, resolvedDuration * 0.25f));
        _vignetteTween.TweenProperty(_vignette, "modulate", new Color(1f, 1f, 1f, 0f), Math.Max(0.10f, resolvedDuration - 0.06f))
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    private void PlaySfx(string path, float volumeOffsetDb, float pitchScale)
    {
        if (!AudioEnabled || SfxVolumeDb <= -79f || _audioPlayers.Count == 0)
        {
            return;
        }

        long now = (long)Time.GetTicksMsec();
        if (_lastAudioAtMsec.TryGetValue(path, out long lastPlayedAt)
            && now - lastPlayedAt < GetAudioMinimumIntervalMsec(path))
        {
            return;
        }

        _lastAudioAtMsec[path] = now;

        AudioStream? stream = LoadAudio(path);
        if (stream is null)
        {
            return;
        }

        int voiceLimit = GetAudioVoiceLimit();
        AudioStreamPlayer? player = null;
        for (int index = 0; index < voiceLimit; index++)
        {
            int candidateIndex = (_nextAudioVoice + index) % voiceLimit;
            if (!_audioPlayers[candidateIndex].Playing)
            {
                player = _audioPlayers[candidateIndex];
                _nextAudioVoice = (candidateIndex + 1) % voiceLimit;
                break;
            }
        }

        if (player is null)
        {
            int voiceIndex = _nextAudioVoice % voiceLimit;
            player = _audioPlayers[voiceIndex];
            _nextAudioVoice = (voiceIndex + 1) % voiceLimit;
            player.Stop();
        }

        player.Stream = stream;
        player.VolumeDb = Mathf.Clamp(SfxVolumeDb + volumeOffsetDb, -80f, 6f);
        player.PitchScale = Mathf.Clamp(pitchScale, 0.5f, 1.5f);
        player.Play();
    }

    private bool UseSecondaryEffects => EffectQuality != BattleFxQuality.Low;

    private int GetVisualBudget()
    {
        return EffectQuality switch
        {
            BattleFxQuality.Low => 12,
            BattleFxQuality.High => 48,
            _ => 28
        };
    }

    private int GetAudioVoiceLimit()
    {
        if (_audioPlayers.Count == 0)
        {
            return 0;
        }

        int requested = EffectQuality switch
        {
            BattleFxQuality.Low => 3,
            BattleFxQuality.High => 7,
            _ => 5
        };
        return Math.Max(1, Math.Min(_audioPlayers.Count, requested));
    }

    private static long GetAudioMinimumIntervalMsec(string path)
    {
        return path switch
        {
            CardDealAudio => 75,
            CardSelectAudio => 55,
            _ => 40
        };
    }

    private void TrackVisual(Node node)
    {
        _activeVisuals.AddLast(node);
        node.TreeExited += () => _activeVisuals.Remove(node);
        TrimVisualBudget();
    }

    private void TrimVisualBudget()
    {
        int budget = GetVisualBudget();
        while (_activeVisuals.Count > budget)
        {
            LinkedListNode<Node>? oldest = _activeVisuals.First;
            if (oldest is null)
            {
                break;
            }

            _activeVisuals.RemoveFirst();
            SafeQueueFree(oldest.Value);
        }

        int voiceLimit = GetAudioVoiceLimit();
        for (int index = voiceLimit; index < _audioPlayers.Count; index++)
        {
            _audioPlayers[index].Stop();
            _audioPlayers[index].Stream = null;
        }
    }

    private void ClearActiveVisuals()
    {
        foreach (Node node in _activeVisuals)
        {
            SafeQueueFree(node);
        }

        _activeVisuals.Clear();
    }

    private void ResetVignette()
    {
        _vignetteTween?.Kill();
        if (_vignette is not null)
        {
            _vignette.Modulate = new Color(1f, 1f, 1f, 0f);
        }
    }

    private static void SafeQueueFree(Node node)
    {
        if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
        {
            node.QueueFree();
        }
    }

    private Texture2D? LoadTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out Texture2D? texture))
        {
            return texture;
        }

        texture = GD.Load<Texture2D>(path);
        if (texture is not null)
        {
            _textureCache[path] = texture;
        }

        return texture;
    }

    private AudioStream? LoadAudio(string path)
    {
        if (_audioCache.TryGetValue(path, out AudioStream? stream))
        {
            return stream;
        }

        stream = GD.Load<AudioStream>(path);
        if (stream is not null)
        {
            _audioCache[path] = stream;
        }

        return stream;
    }

    private Vector2 ResolveCuePosition(int peerId)
    {
        if (peerId > 0 && IsInsideTree())
        {
            foreach (Node node in GetTree().GetNodesInGroup(GeneralCardGroup))
            {
                if (node is not GeneralCardView cardView || cardView.BoundPeerId != peerId || !cardView.IsVisibleInTree())
                {
                    continue;
                }

                Vector2 localPosition = cardView.GetGlobalRect().GetCenter() - GetGlobalRect().Position;
                return ApplyCueBurstOffset(peerId, ClampToViewport(localPosition));
            }
        }

        Vector2 fallback = GetStageCenter();
        return peerId > 0 ? ApplyCueBurstOffset(peerId, fallback) : fallback;
    }

    private Vector2 ApplyCueBurstOffset(int peerId, Vector2 position)
    {
        long now = (long)Time.GetTicksMsec();
        int ordinal = 0;
        if (_lastCueAtMsec.TryGetValue(peerId, out long lastCueAt) && now - lastCueAt <= 220)
        {
            ordinal = (_cueBurstOrdinal.GetValueOrDefault(peerId) + 1) % 5;
        }

        _lastCueAtMsec[peerId] = now;
        _cueBurstOrdinal[peerId] = ordinal;
        Vector2 offset = ordinal switch
        {
            1 => new Vector2(-30f, -14f),
            2 => new Vector2(30f, 14f),
            3 => new Vector2(-18f, 28f),
            4 => new Vector2(20f, -30f),
            _ => Vector2.Zero
        };
        return ClampToViewport(position + offset);
    }

    private Vector2 GetStageCenter()
    {
        Vector2 viewportSize = Size;
        if (viewportSize.X <= 1f || viewportSize.Y <= 1f)
        {
            viewportSize = GetViewportRect().Size;
        }

        return new Vector2(viewportSize.X * 0.50f, viewportSize.Y * 0.46f);
    }

    private Vector2 ClampToViewport(Vector2 position)
    {
        Vector2 viewportSize = Size.X > 1f && Size.Y > 1f ? Size : GetViewportRect().Size;
        return new Vector2(
            Mathf.Clamp(position.X, 100f, Math.Max(100f, viewportSize.X - 100f)),
            Mathf.Clamp(position.Y, 90f, Math.Max(90f, viewportSize.Y - 110f)));
    }

    private float RandomPitch(float minimum, float maximum)
    {
        return minimum + (float)_random.NextDouble() * (maximum - minimum);
    }

    private static DamageType ParseDamageType(string value)
    {
        return Enum.TryParse(value, true, out DamageType damageType) ? damageType : DamageType.Physical;
    }

    private static CardType ParseCardType(string value)
    {
        return Enum.TryParse(value, true, out CardType cardType) ? cardType : CardType.None;
    }

    private static Color GetCardAccent(CardType cardType)
    {
        return cardType switch
        {
            CardType.FireSlash or CardType.FireAttack => CyberStyle.Fire,
            CardType.ThunderSlash or CardType.Lightning => CyberStyle.Thunder,
            CardType.Peach or CardType.PeachGarden => CyberStyle.NeonJade,
            CardType.Dodge or CardType.Nullification => CyberStyle.GoldBright,
            CardType.Weapon or CardType.Armor or CardType.OffensiveHorse or CardType.DefensiveHorse or CardType.Treasure => CyberStyle.Paper,
            _ => CyberStyle.Vermilion.Lightened(0.12f)
        };
    }

    private ShaderMaterial CreateTintMaterial(Color tint, bool additive)
    {
        ShaderMaterial material = new()
        {
            Shader = additive ? GetAdditiveTintShader() : GetAlphaTintShader()
        };
        material.SetShaderParameter("tint_color", tint);
        return material;
    }

    private Shader GetAdditiveTintShader()
    {
        _additiveTintShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_add;
                uniform vec4 tint_color : source_color = vec4(1.0);
                void fragment() {
                    vec4 source = texture(TEXTURE, UV);
                    float energy = source.r;
                    COLOR = vec4(tint_color.rgb * energy, source.a * energy * tint_color.a);
                }
                """
        };
        return _additiveTintShader;
    }

    private Shader GetAlphaTintShader()
    {
        _alphaTintShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_mix;
                uniform vec4 tint_color : source_color = vec4(1.0);
                void fragment() {
                    vec4 source = texture(TEXTURE, UV);
                    float energy = source.r;
                    COLOR = vec4(tint_color.rgb * energy, source.a * energy * tint_color.a);
                }
                """
        };
        return _alphaTintShader;
    }
}
