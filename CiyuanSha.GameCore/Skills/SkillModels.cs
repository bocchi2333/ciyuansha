using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Skills;

[Flags]
public enum SkillTags
{
    None = 0,
    Active = 1 << 0,
    Optional = 1 << 1,
    Forced = 1 << 2,
    Locked = 1 << 3,
    Limited = 1 << 4,
    ViewAs = 1 << 5,
    Temporary = 1 << 6,
    Lord = 1 << 7,
    Awakening = 1 << 8
}

public enum SkillUsageScope
{
    Unlimited = 0,
    OncePerPhase = 1,
    OncePerTurn = 2,
    OncePerRound = 3,
    OncePerMatch = 4
}

public sealed record SkillTriggerDefinition(
    RuleEventKind EventKind,
    RuleEventStage EventStage,
    int Priority = 0,
    bool FirstDo = false,
    bool LastDo = false,
    string Scope = "owner");

public sealed record SkillMarkDefinition(
    string MarkId,
    string DisplayName,
    bool IsPublic = true,
    int InitialValue = 0,
    int MaximumValue = int.MaxValue);

public sealed record SkillAiProfile(
    double Order = 0,
    double Benefit = 0,
    double Threat = 0,
    IReadOnlyDictionary<string, double>? Values = null);

public sealed record SkillDefinitionV2(
    string SkillId,
    string DisplayName,
    string Description,
    string EffectId,
    SkillTags Tags,
    SkillUsageScope UsageScope,
    IReadOnlyList<SkillTriggerDefinition> Triggers,
    IReadOnlyList<SkillMarkDefinition>? Marks = null,
    IReadOnlyList<string>? SubSkillIds = null,
    SkillAiProfile? Ai = null,
    string ViewAsCardId = "");

public sealed record SkillContext(
    GameState State,
    PlayerState Owner,
    RuleEvent? TriggerEvent,
    ChoiceResult? Choice,
    IReadOnlyList<int> TargetSeatIds,
    IReadOnlyList<string> CardInstanceIds);

public sealed record SkillEffectResult(
    bool WasApplied,
    IReadOnlyList<RuleEvent> Events,
    ChoiceRequest? PendingChoice = null,
    string FailureKey = "")
{
    public static SkillEffectResult NotApplied(string failureKey) => new(false, Array.Empty<RuleEvent>(), FailureKey: failureKey);
}

public interface ISkillEffect
{
    string EffectId { get; }

    bool CanTrigger(SkillContext context);

    ChoiceRequest? BuildCostChoice(SkillContext context, Func<string> requestIdFactory);

    SkillEffectResult Resolve(SkillContext context);

    double GetAiValue(SkillContext context);
}

public sealed record CardEffectContext(
    GameState State,
    PlayerState Source,
    CardState Card,
    IReadOnlyList<PlayerState> Targets,
    ChoiceResult? Choice);

public sealed record CardEffectResult(
    bool WasApplied,
    IReadOnlyList<RuleEvent> Events,
    ChoiceRequest? PendingChoice = null,
    string FailureKey = "");

public interface ICardEffect
{
    string EffectId { get; }

    CardEffectResult Resolve(CardEffectContext context);
}

public sealed class EffectRegistry
{
    private readonly Dictionary<string, ISkillEffect> _skillEffects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICardEffect> _cardEffects = new(StringComparer.Ordinal);

    public void Register(ISkillEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!_skillEffects.TryAdd(effect.EffectId, effect))
        {
            throw new InvalidOperationException($"Skill effect is already registered: {effect.EffectId}");
        }
    }

    public void Register(ICardEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!_cardEffects.TryAdd(effect.EffectId, effect))
        {
            throw new InvalidOperationException($"Card effect is already registered: {effect.EffectId}");
        }
    }

    public bool HasSkillEffect(string effectId) => string.IsNullOrWhiteSpace(effectId) || _skillEffects.ContainsKey(effectId);

    public bool HasCardEffect(string effectId) => string.IsNullOrWhiteSpace(effectId) || _cardEffects.ContainsKey(effectId);

    public ISkillEffect GetSkillEffect(string effectId) => _skillEffects.TryGetValue(effectId, out ISkillEffect? effect)
        ? effect
        : throw new KeyNotFoundException($"Unknown skill effect: {effectId}");

    public ICardEffect GetCardEffect(string effectId) => _cardEffects.TryGetValue(effectId, out ICardEffect? effect)
        ? effect
        : throw new KeyNotFoundException($"Unknown card effect: {effectId}");
}
