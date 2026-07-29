using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Simulation;

namespace CiyuanSha.GameCore.Tests;

public sealed class SimulationTests
{
    [Theory]
    [InlineData(BuiltInModeIds.Duel, 2)]
    [InlineData(BuiltInModeIds.Identity, 4)]
    [InlineData(BuiltInModeIds.TeamTwoVersusTwo, 4)]
    [InlineData(BuiltInModeIds.Boss, 3)]
    public void BuiltInMode_CompletesDeterministicHeadlessMatch(string modeId, int playerCount)
    {
        (ContentRegistry content, _) = TestContent.Load();
        MatchConfig config = CreateBotConfig(content, modeId, playerCount, 9701);
        MatchSimulationRunner runner = new(content);

        SimulationResult first = runner.Run(config, 12000);
        SimulationResult second = runner.Run(config, 12000);

        Assert.True(first.Status == MatchStatus.Completed, first.ErrorKey);
        Assert.True(first.RejectedChoices == 0, first.ErrorKey);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Decisions, second.Decisions);
    }

    private static MatchConfig CreateBotConfig(ContentRegistry content, string modeId, int playerCount, ulong seed)
    {
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(playerCount).ToArray();
        List<PlayerConfig> players = generals.Select((general, index) => new PlayerConfig(
            index + 1,
            $"bot-{index + 1}",
            $"Bot {index + 1}",
            general,
            SeatController.Bot,
            IsBoss: modeId == BuiltInModeIds.Boss && index == 0,
            BotDifficulty.Standard)).ToList();
        if (modeId == BuiltInModeIds.Boss)
        {
            players[0] = players[0] with { GeneralId = "jifenxin", IsBoss = true, Controller = SeatController.Bot };
        }
        return new MatchConfig(
            $"sim-{modeId}-{seed}",
            modeId,
            seed,
            players,
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
            players[0].SeatId,
            modeId == BuiltInModeIds.Boss ? "boss_jifenxin" : string.Empty);
    }
}
