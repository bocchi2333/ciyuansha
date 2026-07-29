using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Tests;

public sealed class ModeVictoryAndRewardTests
{
    [Fact]
    public void DuelVictory_ReturnsTheLastLivingSeat()
    {
        (ContentRegistry content, _) = TestContent.Load();
        GameEngine engine = Start(content, BuiltInModeIds.Duel, 2);
        engine.State.Players[2].IsAlive = false;

        VictoryResult result = Assert.IsType<VictoryResult>(new DuelMode().CheckVictory(engine.State));
        Assert.Equal(new[] { 1 }, result.WinnerSeatIds);
        Assert.Equal("victory.duel", result.ResultKey);
    }

    [Fact]
    public void IdentityVictory_CoversLordRebelAndSoleRenegadeOutcomes()
    {
        (ContentRegistry content, _) = TestContent.Load();

        GameEngine lordEngine = Start(content, BuiltInModeIds.Identity, 4);
        PlayerState lord = ByRole(lordEngine, IdentityRole.Lord);
        foreach (PlayerState enemy in lordEngine.State.Players.Values.Where(player => player.Role is IdentityRole.Rebel or IdentityRole.Renegade))
        {
            enemy.IsAlive = false;
        }
        VictoryResult lordVictory = Assert.IsType<VictoryResult>(new IdentityMode().CheckVictory(lordEngine.State));
        Assert.Equal("victory.lord", lordVictory.ResultKey);
        Assert.Equal(lordEngine.State.Players.Values.Where(player => player.Role is IdentityRole.Lord or IdentityRole.Loyalist).Select(player => player.SeatId).Order(), lordVictory.WinnerSeatIds.Order());

        GameEngine rebelEngine = Start(content, BuiltInModeIds.Identity, 4);
        ByRole(rebelEngine, IdentityRole.Lord).IsAlive = false;
        VictoryResult rebelVictory = Assert.IsType<VictoryResult>(new IdentityMode().CheckVictory(rebelEngine.State));
        Assert.Equal("victory.rebels", rebelVictory.ResultKey);
        Assert.Equal(rebelEngine.State.Players.Values.Where(player => player.Role == IdentityRole.Rebel).Select(player => player.SeatId), rebelVictory.WinnerSeatIds);

        GameEngine renegadeEngine = Start(content, BuiltInModeIds.Identity, 4);
        PlayerState renegade = ByRole(renegadeEngine, IdentityRole.Renegade);
        foreach (PlayerState player in renegadeEngine.State.Players.Values.Where(player => player != renegade)) player.IsAlive = false;
        VictoryResult renegadeVictory = Assert.IsType<VictoryResult>(new IdentityMode().CheckVictory(renegadeEngine.State));
        Assert.Equal("victory.renegade", renegadeVictory.ResultKey);
        Assert.Equal(new[] { renegade.SeatId }, renegadeVictory.WinnerSeatIds);
        Assert.True(lord.IsAlive);
    }

    [Fact]
    public void IdentityRoles_ArePrivateUntilCompletionAndLordIsAlwaysPublic()
    {
        (ContentRegistry content, _) = TestContent.Load();
        GameEngine engine = Start(content, BuiltInModeIds.Identity, 4);
        GameView live = engine.BuildView(ViewerContext.Spectator);
        Assert.Single(live.Players, player => player.IsRoleVisible);
        Assert.Equal(IdentityRole.Lord, live.Players.Single(player => player.IsRoleVisible).Role);

        engine.State.Status = MatchStatus.Completed;
        GameView completed = engine.BuildView(ViewerContext.Spectator);
        Assert.All(completed.Players, player => Assert.True(player.IsRoleVisible));
    }

    [Fact]
    public void IdentityKillRewards_DrawThreeForRebelAndStripLordForLoyalist()
    {
        (ContentRegistry content, _) = TestContent.Load();
        GameEngine rebelKill = StartIdentityWithSlash(content, 1);
        PlayerState killer = ByRole(rebelKill, IdentityRole.Lord);
        PlayerState rebel = ByRole(rebelKill, IdentityRole.Rebel);
        PrepareLethalSlash(rebelKill, killer, rebel);
        int before = killer.Hand.Count;
        ResolveLethalSlash(rebelKill, killer, rebel);
        Assert.Equal(before + 2, killer.Hand.Count);
        Assert.Contains(rebelKill.Journal.Entries, entry => entry.RuleEvent?.Kind == CiyuanSha.GameCore.Events.RuleEventKind.CardsDrawn
            && entry.RuleEvent.SourceSeatId == killer.SeatId
            && entry.RuleEvent.Payload is CiyuanSha.GameCore.Events.ValueRuleEventPayload { Value: 3 });

        GameEngine loyalistKill = StartIdentityWithSlash(content, 1001);
        PlayerState punishedLord = ByRole(loyalistKill, IdentityRole.Lord);
        PlayerState loyalist = ByRole(loyalistKill, IdentityRole.Loyalist);
        PrepareLethalSlash(loyalistKill, punishedLord, loyalist, equipWeapon: true);
        Assert.NotEmpty(punishedLord.Hand);
        Assert.NotEmpty(punishedLord.Equipment);
        ResolveLethalSlash(loyalistKill, punishedLord, loyalist);
        Assert.Empty(punishedLord.Hand);
        Assert.Empty(punishedLord.Equipment);
    }

    [Fact]
    public void TwoVersusTwo_AlternatesSeatsAndAwardsTheWholeSurvivingTeam()
    {
        (ContentRegistry content, _) = TestContent.Load();
        GameEngine engine = Start(content, BuiltInModeIds.TeamTwoVersusTwo, 4);
        Assert.Equal(new[] { TeamSide.A, TeamSide.B, TeamSide.A, TeamSide.B }, engine.State.Players.Values.OrderBy(player => player.SeatId).Select(player => player.Team));
        foreach (PlayerState opponent in engine.State.Players.Values.Where(player => player.Team == TeamSide.B)) opponent.IsAlive = false;

        VictoryResult result = Assert.IsType<VictoryResult>(new TeamTwoVersusTwoMode().CheckVictory(engine.State));
        Assert.Equal(new[] { 1, 3 }, result.WinnerSeatIds.Order());
        Assert.Equal("victory.team", result.ResultKey);
    }

    [Fact]
    public void BossMode_CoversBothVictoriesAndThresholdPhaseTransition()
    {
        (ContentRegistry content, _) = TestContent.Load();
        MatchConfig config = BossConfig(content);
        GameEngine phaseEngine = StartBossAtHeroPlayWithSlash(content);
        PlayerState boss = ByRole(phaseEngine, IdentityRole.Boss);
        BossDefinition definition = content.Bosses["boss_jifenxin"];
        int threshold = Math.Max(1, (int)Math.Ceiling(boss.MaxHealth * definition.PhaseTwoHealthRatio));
        boss.Health = threshold + 1;
        PlayerState heroAttacker = phaseEngine.State.Players[phaseEngine.State.CurrentSeatId];
        string slash = heroAttacker.Hand.First(cardId => content.Cards[phaseEngine.State.Cards[cardId].DefinitionId].EffectId == "card.slash");
        RemoveHandDefinitions(phaseEngine, boss, "dodge");
        Submit(phaseEngine, $"action:use:{slash}");
        Submit(phaseEngine, $"seat:{boss.SeatId}");
        if (phaseEngine.State.PendingChoice?.PromptKey == "response.dodge") Submit(phaseEngine, "control:decline");
        Assert.Equal(2, boss.Marks["boss_phase"]);
        Assert.Equal(threshold, boss.Health);
        Assert.All(definition.PhaseTwoSkillIds, skillId => Assert.Contains(skillId, boss.SkillIds));

        boss.IsAlive = false;
        VictoryResult heroes = Assert.IsType<VictoryResult>(new BossMode().CheckVictory(phaseEngine.State));
        Assert.Equal("victory.heroes", heroes.ResultKey);
        Assert.Equal(phaseEngine.State.Players.Values.Where(player => player.Role == IdentityRole.Hero).Select(player => player.SeatId).Order(), heroes.WinnerSeatIds.Order());

        GameEngine bossEngine = GameEngine.Start(config with { MatchId = "boss-wins" }, content);
        foreach (PlayerState hero in bossEngine.State.Players.Values.Where(player => player.Role == IdentityRole.Hero)) hero.IsAlive = false;
        VictoryResult bossVictory = Assert.IsType<VictoryResult>(new BossMode().CheckVictory(bossEngine.State));
        Assert.Equal("victory.boss", bossVictory.ResultKey);
        Assert.Equal(new[] { ByRole(bossEngine, IdentityRole.Boss).SeatId }, bossVictory.WinnerSeatIds);
    }

    private static PlayerState ByRole(GameEngine engine, IdentityRole role) => engine.State.Players.Values.Single(player => player.Role == role);

    private static GameEngine Start(ContentRegistry content, string modeId, int count) => GameEngine.Start(Config(content, modeId, count), content);

    private static MatchConfig Config(ContentRegistry content, string modeId, int count)
    {
        string[] generals = content.Generals.Keys.OrderBy(id => id, StringComparer.Ordinal).Take(count).ToArray();
        return new MatchConfig(
            $"mode-{modeId}", modeId, 884422,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
            1);
    }

    private static MatchConfig BossConfig(ContentRegistry content)
    {
        string[] generals = content.Generals.Keys.OrderBy(id => id, StringComparer.Ordinal).Take(3).ToArray();
        return new MatchConfig(
            "boss-threshold", BuiltInModeIds.Boss, 9911,
            new[]
            {
                new PlayerConfig(1, "boss", "Boss", "jifenxin", SeatController.Bot, true, BotDifficulty.Hard),
                new PlayerConfig(2, "p2", "P2", generals[1]),
                new PlayerConfig(3, "p3", "P3", generals[2])
            },
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
            1,
            "boss_jifenxin");
    }

    private static GameEngine StartIdentityWithSlash(ContentRegistry content, ulong firstSeed)
    {
        for (ulong seed = firstSeed; seed < firstSeed + 1000; seed++)
        {
            MatchConfig config = Config(content, BuiltInModeIds.Identity, 4) with { Seed = seed, MatchId = $"identity-reward-{seed}" };
            GameEngine engine = GameEngine.Start(config, content);
            PlayerState lord = ByRole(engine, IdentityRole.Lord);
            if (lord.SeatId == engine.State.CurrentSeatId
                && lord.Hand.Any(cardId => content.Cards[engine.State.Cards[cardId].DefinitionId].EffectId == "card.slash"))
            {
                return engine;
            }
        }
        throw new XunitException("No deterministic identity opening with a Slash was found.");
    }

    private static void PrepareLethalSlash(GameEngine engine, PlayerState killer, PlayerState target, bool equipWeapon = true)
    {
        target.Health = 1;
        foreach (PlayerState player in engine.State.Players.Values)
        {
            RemoveHandDefinitions(engine, player, "dodge", "peach", "wine");
        }
        if (equipWeapon)
        {
            string weapon = engine.State.DrawPile.First(cardId => engine.State.Cards[cardId].DefinitionId == "kylin_bow");
            engine.State.DrawPile.Remove(weapon);
            engine.State.Cards[weapon].Zone = CardZone.Equipment;
            engine.State.Cards[weapon].OwnerSeatId = killer.SeatId;
            killer.Equipment[EquipmentSlot.Weapon] = weapon;
        }
    }

    private static void ResolveLethalSlash(GameEngine engine, PlayerState killer, PlayerState target)
    {
        string slash = killer.Hand.First(cardId => engine.State.Cards[cardId].DefinitionId == "slash");
        Submit(engine, $"action:use:{slash}");
        ChoiceRequest targets = Assert.IsType<ChoiceRequest>(engine.State.PendingChoice);
        if (!targets.Options.Any(option => option.OptionId == $"seat:{target.SeatId}"))
        {
            throw new XunitException("The identity reward target is outside Slash range.");
        }
        Submit(engine, $"seat:{target.SeatId}");
        int guard = 0;
        while (target.IsAlive && engine.State.PendingChoice is not null)
        {
            ChoiceRequest request = engine.State.PendingChoice;
            if (!request.Options.Any(option => option.OptionId == "control:decline"))
            {
                throw new XunitException($"Unexpected lethal Slash request: {request.PromptKey}");
            }
            Submit(engine, "control:decline");
            if (++guard > 12) throw new XunitException("Dying rescue sequence did not terminate.");
        }
        Assert.False(target.IsAlive);
    }

    private static void RemoveHandDefinitions(GameEngine engine, PlayerState player, params string[] definitionIds)
    {
        foreach (string cardId in player.Hand.Where(cardId => definitionIds.Contains(
            engine.State.Cards[cardId].DefinitionId,
            StringComparer.Ordinal)).ToArray())
        {
            player.Hand.Remove(cardId);
            engine.State.Cards[cardId].Zone = CardZone.Removed;
            engine.State.Cards[cardId].OwnerSeatId = 0;
        }
    }

    private static GameEngine StartBossAtHeroPlayWithSlash(ContentRegistry content)
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            GameEngine engine = GameEngine.Start(BossConfig(content) with { Seed = seed, MatchId = $"boss-phase-{seed}" }, content);
            Submit(engine, "action:end_play");
            PlayerState current = engine.State.Players[engine.State.CurrentSeatId];
            if (current.Role == IdentityRole.Hero
                && engine.State.Phase == GamePhase.Play
                && current.Hand.Any(cardId => content.Cards[engine.State.Cards[cardId].DefinitionId].EffectId == "card.slash"))
            {
                return engine;
            }
        }
        throw new XunitException("No deterministic Boss opening reached a hero Slash.");
    }

    private static void Submit(GameEngine engine, params string[] options)
    {
        ChoiceRequest request = Assert.IsType<ChoiceRequest>(engine.State.PendingChoice);
        EngineStepResult result = engine.Advance(request.ActingSeatId, ChoiceResult.Select(request.RequestId, request.StateRevision, options));
        Assert.True(result.Progress != EngineProgress.Rejected, result.ErrorKey);
        Assert.True(result.Progress != EngineProgress.Faulted, result.ErrorKey);
    }
}
