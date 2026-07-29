using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Simulation;

public sealed record SimulationResult(
    string MatchId,
    string ModeId,
    ulong Seed,
    MatchStatus Status,
    int Decisions,
    int RejectedChoices,
    string FinalStateHash,
    IReadOnlyList<int> WinnerSeatIds,
    string ErrorKey = "");

public sealed record SimulationBatchReport(
    int RequestedMatches,
    int CompletedMatches,
    int FaultedMatches,
    int RejectedChoices,
    IReadOnlyList<SimulationResult> Results)
{
    public bool Passed => RequestedMatches == CompletedMatches && FaultedMatches == 0 && RejectedChoices == 0;
}

/// <summary>Headless, deterministic, node-budgeted simulation without wall-clock decisions.</summary>
public sealed class MatchSimulationRunner
{
    private readonly ContentRegistry _content;
    private readonly GameModeRegistry _modes;
    private readonly IBotPolicy _policy;

    public MatchSimulationRunner(ContentRegistry content, GameModeRegistry? modes = null, IBotPolicy? policy = null)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _modes = modes ?? new GameModeRegistry();
        _policy = policy ?? new DeterministicBotPolicy();
    }

    public SimulationResult Run(MatchConfig config, int maximumDecisions = 20000)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (maximumDecisions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDecisions));
        }
        GameEngine engine = GameEngine.Start(config, _content, _modes);
        int decisions = 0;
        int rejected = 0;
        string error = string.Empty;
        while (engine.State.Status == MatchStatus.Running && decisions < maximumDecisions)
        {
            ChoiceRequest? request = engine.State.PendingChoice;
            if (request is null)
            {
                EngineStepResult automatic = engine.Advance();
                if (automatic.Progress == EngineProgress.Faulted)
                {
                    error = $"{automatic.ErrorKey}:{engine.State.ResultMessage}";
                    break;
                }
                continue;
            }
            PlayerConfig player = config.Players.Single(item => item.SeatId == request.ActingSeatId);
            if (request.StateRevision != engine.State.Revision)
            {
                error = $"choice.stale_revision:{request.StateRevision}/{engine.State.Revision}:{request.PromptKey}";
                rejected++;
                break;
            }
            GameView view = engine.BuildView(ViewerContext.ForPlayer(request.ActingSeatId));
            ulong decisionSeed = config.Seed ^ (ulong)engine.State.Revision ^ ((ulong)(uint)request.ActingSeatId << 32);
            ChoiceResult result = _policy.Choose(view, request, new BotContext(player.BotDifficulty, decisionSeed, NodeBudget: 64));
            EngineStepResult step = engine.Advance(request.ActingSeatId, result);
            decisions++;
            if (step.Progress == EngineProgress.Rejected)
            {
                rejected++;
                error = step.ErrorKey;
                break;
            }
            if (step.Progress == EngineProgress.Faulted)
            {
                error = $"{step.ErrorKey}:{engine.State.ResultMessage}";
                break;
            }
        }
        if (engine.State.Status == MatchStatus.Running && decisions >= maximumDecisions)
        {
            error = "simulation.decision_budget_exceeded";
        }
        return new SimulationResult(
            config.MatchId,
            config.ModeId,
            config.Seed,
            engine.State.Status,
            decisions,
            rejected,
            engine.State.ComputeCanonicalHash(),
            engine.State.WinnerSeatIds.ToArray(),
            error);
    }

    public SimulationBatchReport RunBatch(
        int count,
        Func<int, MatchConfig> configFactory,
        int maximumDecisionsPerMatch = 20000)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        ArgumentNullException.ThrowIfNull(configFactory);
        List<SimulationResult> results = new(count);
        for (int index = 0; index < count; index++)
        {
            results.Add(Run(configFactory(index), maximumDecisionsPerMatch));
        }
        return new SimulationBatchReport(
            count,
            results.Count(result => result.Status == MatchStatus.Completed),
            results.Count(result => result.Status == MatchStatus.Faulted || result.ErrorKey.Length > 0),
            results.Sum(result => result.RejectedChoices),
            results);
    }
}
