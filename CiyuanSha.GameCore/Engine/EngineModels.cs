using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Engine;

public enum EngineProgress
{
    WaitingForChoice = 0,
    MatchCompleted = 1,
    Progressed = 2,
    Rejected = 3,
    Faulted = 4
}

public sealed record EngineStepResult(
    EngineProgress Progress,
    long StateRevision,
    IReadOnlyList<RuleEvent> Events,
    ChoiceRequest? PendingChoice,
    string StateHash,
    string ErrorKey = "");

internal enum PendingOperationKind
{
    None = 0,
    PlayAction = 1,
    SelectCardTarget = 2,
    RespondSlash = 3,
    RespondDodge = 4,
    DuelResponse = 5,
    DiscardToLimit = 6,
    SkillCost = 7,
    SelectSlashNature = 8,
    DyingRescue = 9,
    SkillTarget = 10,
    JudgementReplacement = 11,
    SelectTargetCard = 12,
    HarvestPick = 13,
    NullificationResponse = 14,
    FireAttackReveal = 15,
    FireAttackDiscard = 16,
    BorrowSwordVictim = 17,
    BorrowSwordSlash = 18,
    SequentialTargetEffect = 19,
    DoubleSwordsChoice = 20,
    GreenDragonFollowUp = 21,
    StoneAxeCost = 22,
    IceSwordCards = 23,
    KylinMountChoice = 24,
    SerpentSpearPlayCost = 25,
    SerpentSpearResponseCost = 26
}

internal sealed class PendingOperation
{
    public PendingOperationKind Kind { get; init; }

    public PendingOperationKind ResumeKind { get; init; }

    public int SourceSeatId { get; init; }

    public int TargetSeatId { get; set; }

    public int OtherSeatId { get; set; }

    public string CardInstanceId { get; init; } = string.Empty;

    public string OtherCardInstanceId { get; set; } = string.Empty;

    public string SkillId { get; init; } = string.Empty;

    public string EffectId { get; init; } = string.Empty;

    public DamageNature? DamageNatureOverride { get; set; }

    public int DamageAmount { get; set; }

    public bool RequiresNullification { get; set; } = true;

    public Queue<int> RemainingTargets { get; } = new();

    public Queue<int> RemainingDyingTargets { get; } = new();

    public Queue<int> EffectTargets { get; } = new();

    public PendingOperation? Continuation { get; set; }
}
