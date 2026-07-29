using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Events;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Skills;

namespace CiyuanSha.GameCore.Tests;

public sealed class SkillV2Tests
{
    [Fact]
    public void BundledSkills_CoverActiveOptionalLockedLimitedViewAsMarksSubskillsAndTemporary()
    {
        ContentRegistry content = TestContent.Load().Registry;
        SkillDefinitionV2[] skills = content.Skills.Values.ToArray();

        Assert.Contains(skills, skill => (skill.Tags & SkillTags.Active) != 0);
        Assert.Contains(skills, skill => (skill.Tags & SkillTags.Optional) != 0);
        Assert.Contains(skills, skill => (skill.Tags & SkillTags.Locked) != 0);
        Assert.Contains(skills, skill => (skill.Tags & SkillTags.Limited) != 0);
        Assert.Contains(skills, skill => (skill.Tags & SkillTags.ViewAs) != 0 && !string.IsNullOrWhiteSpace(skill.ViewAsCardId));
        Assert.Contains(skills, skill => (skill.Tags & SkillTags.Temporary) != 0);
        Assert.Contains(skills, skill => skill.Marks is { Count: > 0 });
        Assert.Contains(skills, skill => skill.SubSkillIds is { Count: > 0 });
    }

    [Fact]
    public void TriggerScheduler_UsesSpecialPrioritySeatAndStableSkillOrder()
    {
        SkillTriggerDefinition first = new(RuleEventKind.Damage, RuleEventStage.Before, Priority: -10, FirstDo: true);
        SkillTriggerDefinition normalHigh = new(RuleEventKind.Damage, RuleEventStage.Before, Priority: 20);
        SkillTriggerDefinition normalTie = new(RuleEventKind.Damage, RuleEventStage.Before, Priority: 10);
        SkillTriggerDefinition last = new(RuleEventKind.Damage, RuleEventStage.Before, Priority: 999, LastDo: true);
        SkillTriggerCandidate[] input =
        {
            Candidate(1, "z-last", last),
            Candidate(1, "z-tie", normalTie),
            Candidate(3, "a-tie", normalTie),
            Candidate(2, "high", normalHigh),
            Candidate(4, "first", first)
        };

        IReadOnlyList<SkillTriggerCandidate> ordered = TriggerScheduler.Order(input, 3, new[] { 1, 2, 3, 4 });

        Assert.Equal(new[] { "first", "high", "a-tie", "z-tie", "z-last" }, ordered.Select(candidate => candidate.Skill.SkillId));
    }

    [Fact]
    public void LimitedSkill_GrantsAndExpiresTemporarySubskillThroughLegalChoices()
    {
        ContentRegistry content = TestContent.Load().Registry;
        string opponent = content.Generals.Keys.First(id => id != "jifenxin");
        MatchConfig config = new(
            "skill-temp", BuiltInModeIds.Duel, 7701,
            new[]
            {
                new PlayerConfig(1, "p1", "P1", "jifenxin"),
                new PlayerConfig(2, "p2", "P2", opponent)
            },
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
        GameEngine engine = GameEngine.Start(config, content);
        ChoiceRequest request = engine.State.PendingChoice ?? throw new XunitException("Expected a play choice.");
        Assert.Contains(request.Options, option => option.OptionId == "action:skill:limit_break");

        EngineStepResult step = engine.Advance(1, ChoiceResult.Select(request.RequestId, request.StateRevision, "action:skill:limit_break"));

        Assert.NotEqual(EngineProgress.Rejected, step.Progress);
        Assert.Contains("limit_break_empower", engine.State.Players[1].TemporarySkillIds);
        Assert.Equal(2, engine.State.Players[1].Marks["limit_break_damage"]);

        DeterministicBotPolicy policy = new();
        int guard = 0;
        while (engine.State.Players[1].TemporarySkillIds.Contains("limit_break_empower") && guard++ < 200)
        {
            ChoiceRequest pending = engine.State.PendingChoice ?? throw new XunitException("Expected a legal choice while expiring the temporary skill.");
            GameView view = engine.BuildView(ViewerContext.ForPlayer(pending.ActingSeatId));
            ChoiceResult choice = policy.Choose(view, pending, new BotContext(BotDifficulty.Standard, 7701UL ^ (ulong)engine.State.Revision));
            EngineStepResult result = engine.Advance(pending.ActingSeatId, choice);
            Assert.False(result.Progress is EngineProgress.Rejected or EngineProgress.Faulted, result.ErrorKey);
        }

        Assert.DoesNotContain("limit_break_empower", engine.State.Players[1].TemporarySkillIds);
        Assert.False(engine.State.Players[1].Marks.ContainsKey("limit_break_damage"));
    }

    [Fact]
    public void BuiltInEffectRegistry_RejectsInvalidContextsAndProducesTypedIntent()
    {
        EffectRegistry effects = BuiltInEffects.CreateRegistry();
        GameState state = new() { MatchId = "effect", ModeId = BuiltInModeIds.Duel };
        PlayerState owner = new() { SeatId = 1, PlayerId = "p1", DisplayName = "P1", GeneralId = "g", Health = 4, MaxHealth = 4 };
        state.Players.Add(1, owner);
        SkillEffectResult skill = effects.GetSkillEffect("skill.active_draw").Resolve(new SkillContext(
            state, owner, null, null, Array.Empty<int>(), Array.Empty<string>()));
        Assert.True(skill.WasApplied);
        Assert.Single(skill.Events);
        Assert.Equal(RuleEventKind.SkillTriggered, skill.Events[0].Kind);

        CardState detached = new() { InstanceId = "card", DefinitionId = "slash", Zone = CardZone.Hand, OwnerSeatId = 1 };
        CardEffectResult invalid = effects.GetCardEffect("card.slash").Resolve(new CardEffectContext(state, owner, detached, Array.Empty<PlayerState>(), null));
        Assert.False(invalid.WasApplied);
        state.Cards.Add(detached.InstanceId, detached);
        CardEffectResult valid = effects.GetCardEffect("card.slash").Resolve(new CardEffectContext(state, owner, detached, Array.Empty<PlayerState>(), null));
        Assert.True(valid.WasApplied);
        Assert.Equal(RuleEventKind.CardUsed, Assert.Single(valid.Events).Kind);
    }

    [Fact]
    public void LockedAutoDodge_RespondsWithoutOfferingAnIllegalDecline()
    {
        ContentRegistry content = TestContent.Load().Registry;
        GameEngine? engine = null;
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            MatchConfig config = new(
                $"auto-dodge-{seed}",
                BuiltInModeIds.Duel,
                seed,
                new[]
                {
                    new PlayerConfig(1, "source", "Source", "chenchen"),
                    new PlayerConfig(2, "target", "Target", "lishengming")
                },
                "standard_military_161",
                content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
                1);
            GameEngine candidate = GameEngine.Start(config, content);
            if (candidate.State.CurrentSeatId == 1
                && candidate.State.Players[1].Hand.Any(cardId => content.Cards[candidate.State.Cards[cardId].DefinitionId].EffectId
                    is "card.slash" or "card.fire_slash" or "card.thunder_slash"))
            {
                engine = candidate;
                break;
            }
        }
        Assert.NotNull(engine);
        string slashId = engine.State.Players[1].Hand.First(cardId => content.Cards[engine.State.Cards[cardId].DefinitionId].EffectId
            is "card.slash" or "card.fire_slash" or "card.thunder_slash");
        int healthBefore = engine.State.Players[2].Health;

        Submit(engine, $"action:use:{slashId}");
        Submit(engine, "seat:2");

        Assert.Equal(healthBefore, engine.State.Players[2].Health);
        Assert.Equal(engine.State.RoundNumber, engine.State.Players[2].Marks["skill:auto_dodge:round"]);
        Assert.NotEqual("response.dodge", engine.State.PendingChoice?.PromptKey);
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent is
        {
            Kind: RuleEventKind.SkillTriggered,
            SourceSeatId: 2,
            Payload: TextRuleEventPayload { Arguments: not null }
        } && ((TextRuleEventPayload)entry.RuleEvent.Payload).Arguments!["skillId"] == "auto_dodge");
    }

    private static void Submit(GameEngine engine, string optionId)
    {
        ChoiceRequest request = engine.State.PendingChoice ?? throw new XunitException("Expected a pending choice.");
        EngineStepResult result = engine.Advance(request.ActingSeatId, ChoiceResult.Select(request.RequestId, request.StateRevision, optionId));
        Assert.False(result.Progress is EngineProgress.Rejected or EngineProgress.Faulted, result.ErrorKey);
    }

    private static SkillTriggerCandidate Candidate(int seat, string id, SkillTriggerDefinition trigger) => new(
        seat,
        new SkillDefinitionV2(id, id, id, "skill.active_draw", SkillTags.Locked, SkillUsageScope.Unlimited, new[] { trigger }),
        trigger);
}
