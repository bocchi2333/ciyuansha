using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Events;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Tests;

public sealed class PhaseDirectiveTests
{
    [Fact]
    public void SkipDirective_IsJournaledAndSkipsTheRequestedPhase()
    {
        GameEngine engine = StartDuel();
        int originalSeat = engine.State.CurrentSeatId;

        PhaseDirective directive = engine.SchedulePhaseDirective(
            originalSeat,
            PhaseDirectiveKind.Skip,
            GamePhase.Discard,
            sourceId: "test.skip");
        EngineStepResult result = EndPlay(engine);

        Assert.Equal(PhaseDirectiveKind.Skip, directive.Kind);
        Assert.NotEqual(EngineProgress.Faulted, result.Progress);
        Assert.NotEqual(originalSeat, engine.State.CurrentSeatId);
        Assert.DoesNotContain(engine.State.PhaseDirectives, value => value.Sequence == directive.Sequence);
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent is
        {
            Kind: RuleEventKind.PhaseDirectiveScheduled,
            Payload: PhaseRuleEventPayload { Phase: GamePhase.Discard, SourceId: "test.skip" }
        });
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent is
        {
            Kind: RuleEventKind.PhaseSkipped,
            Payload: PhaseRuleEventPayload { Phase: GamePhase.Discard, WasSkipped: true }
        });
    }

    [Fact]
    public void ExtraDirective_RunsOneInsertedPhaseThenResumesOriginalPhase()
    {
        GameEngine engine = StartDuel();
        int seatId = engine.State.CurrentSeatId;
        int handBefore = engine.State.Players[seatId].Hand.Count;
        engine.SchedulePhaseDirective(
            seatId,
            PhaseDirectiveKind.Extra,
            GamePhase.Discard,
            GamePhase.Draw,
            "test.extra_draw");

        EngineStepResult result = EndPlay(engine);

        Assert.NotEqual(EngineProgress.Faulted, result.Progress);
        Assert.Equal(seatId, engine.State.CurrentSeatId);
        Assert.Equal(GamePhase.Discard, engine.State.Phase);
        Assert.Equal(GamePhase.NotStarted, engine.State.ExtraPhaseResume);
        Assert.Equal(ChoiceKind.Discard, engine.State.PendingChoice?.Kind);
        Assert.Equal(handBefore + ExpectedDrawCount(engine, seatId), engine.State.Players[seatId].Hand.Count);
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent is
        {
            Kind: RuleEventKind.PhaseChanged,
            Payload: PhaseRuleEventPayload { Phase: GamePhase.Draw, IsExtra: true, SourceId: "test.extra_draw" }
        });
    }

    [Fact]
    public void ReplaceDirective_ConsumesOriginalAndContinuesFromReplacement()
    {
        GameEngine engine = StartDuel();
        int seatId = engine.State.CurrentSeatId;
        int handBefore = engine.State.Players[seatId].Hand.Count;
        engine.SchedulePhaseDirective(
            seatId,
            PhaseDirectiveKind.Replace,
            GamePhase.Discard,
            GamePhase.Draw,
            "test.replace_draw");

        EngineStepResult result = EndPlay(engine);

        Assert.NotEqual(EngineProgress.Faulted, result.Progress);
        Assert.Equal(seatId, engine.State.CurrentSeatId);
        Assert.Equal(GamePhase.Play, engine.State.Phase);
        Assert.Equal(ChoiceKind.SelectAction, engine.State.PendingChoice?.Kind);
        Assert.Equal(handBefore + ExpectedDrawCount(engine, seatId), engine.State.Players[seatId].Hand.Count);
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent is
        {
            Kind: RuleEventKind.PhaseSkipped,
            Payload: PhaseRuleEventPayload
            {
                Phase: GamePhase.Discard,
                WasSkipped: true,
                ReplacementPhase: GamePhase.Draw,
                SourceId: "test.replace_draw"
            }
        });
    }

    [Fact]
    public void PhaseDirectives_ArePartOfTheCanonicalStateHash()
    {
        GameEngine first = StartDuel(seed: 100);
        GameEngine second = StartDuel(seed: 100);
        Assert.Equal(first.State.ComputeCanonicalHash(), second.State.ComputeCanonicalHash());

        first.SchedulePhaseDirective(first.State.CurrentSeatId, PhaseDirectiveKind.Skip, GamePhase.Draw, sourceId: "hash");

        Assert.NotEqual(first.State.ComputeCanonicalHash(), second.State.ComputeCanonicalHash());
    }

    private static EngineStepResult EndPlay(GameEngine engine)
    {
        ChoiceRequest request = Assert.IsType<ChoiceRequest>(engine.State.PendingChoice);
        Assert.Contains(request.Options, option => option.OptionId == "action:end_play");
        return engine.Advance(request.ActingSeatId, ChoiceResult.Select(
            request.RequestId,
            request.StateRevision,
            "action:end_play"));
    }

    private static int ExpectedDrawCount(GameEngine engine, int seatId)
    {
        (ContentRegistry content, _) = TestContent.Load();
        PlayerState player = engine.State.Players[seatId];
        return 2
            + (player.SkillIds.Any(skillId => content.Skills[skillId].EffectId == "skill.bonus_draw") ? 1 : 0)
            + (player.SkillIds.Any(skillId => content.Skills[skillId].EffectId == "skill.boss_pressure") ? 1 : 0);
    }

    private static GameEngine StartDuel(ulong seed = 1234)
    {
        (ContentRegistry content, _) = TestContent.Load();
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(2).ToArray();
        MatchConfig config = new(
            $"phase-{seed}",
            BuiltInModeIds.Duel,
            seed,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
        return GameEngine.Start(config, content);
    }
}
