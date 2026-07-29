using System.Text.Json.Serialization;
using CiyuanSha.GameCore.Domain;

namespace CiyuanSha.GameCore.Events;

public enum RuleEventStage
{
    Created = 0,
    Before = 1,
    Begin = 2,
    Resolve = 3,
    End = 4,
    After = 5,
    Completed = 6,
    Cancelled = 7,
    Faulted = 8
}

public enum RuleEventKind
{
    MatchStarted = 0,
    MatchEnded = 1,
    TurnStarted = 2,
    TurnEnded = 3,
    PhaseChanged = 4,
    PhaseSkipped = 5,
    CardMoved = 6,
    CardsDrawn = 7,
    CardsDiscarded = 8,
    CardUsed = 9,
    ResponseRequested = 10,
    ResponseResolved = 11,
    Damage = 12,
    Heal = 13,
    Dying = 14,
    Defeated = 15,
    Judgement = 16,
    SkillTriggered = 17,
    ChoiceRequested = 18,
    ChoiceAccepted = 19,
    RandomConsumed = 20,
    EquipmentChanged = 21,
    StateFaulted = 22,
    CardCancelled = 23,
    CardRevealed = 24,
    PhaseDirectiveScheduled = 25,
    Custom = 100
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$payload")]
[JsonDerivedType(typeof(EmptyRuleEventPayload), "empty")]
[JsonDerivedType(typeof(PhaseRuleEventPayload), "phase")]
[JsonDerivedType(typeof(CardMoveRuleEventPayload), "cardMove")]
[JsonDerivedType(typeof(DamageRuleEventPayload), "damage")]
[JsonDerivedType(typeof(ValueRuleEventPayload), "value")]
[JsonDerivedType(typeof(TextRuleEventPayload), "text")]
public abstract record RuleEventPayload;

public sealed record EmptyRuleEventPayload : RuleEventPayload
{
    public static EmptyRuleEventPayload Instance { get; } = new();
}

public sealed record PhaseRuleEventPayload(
    GamePhase Phase,
    bool WasSkipped = false,
    GamePhase ReplacementPhase = GamePhase.NotStarted,
    bool IsExtra = false,
    string SourceId = "") : RuleEventPayload;

public sealed record CardMoveRuleEventPayload(
    string CardInstanceId,
    CardZone FromZone,
    CardZone ToZone,
    int FromSeatId = 0,
    int ToSeatId = 0) : RuleEventPayload;

public sealed record DamageRuleEventPayload(
    int Amount,
    DamageNature Nature,
    string SourceCardId = "",
    bool IsChainTransfer = false) : RuleEventPayload;

public sealed record ValueRuleEventPayload(int Value, string Reason = "") : RuleEventPayload;

public sealed record TextRuleEventPayload(string MessageKey, IReadOnlyDictionary<string, string>? Arguments = null) : RuleEventPayload;

public sealed record RuleEvent(
    string EventId,
    long Sequence,
    string ParentEventId,
    RuleEventKind Kind,
    RuleEventStage Stage,
    int SourceSeatId,
    IReadOnlyList<int> TargetSeatIds,
    RuleEventPayload Payload,
    int Priority = 0,
    string SourceSkillId = "")
{
    public RuleEvent AtStage(RuleEventStage stage) => this with { Stage = stage };
}
