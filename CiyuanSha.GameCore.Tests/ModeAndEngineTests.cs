using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Tests;

public sealed class ModeAndEngineTests
{
    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Standard)]
    [InlineData(BotDifficulty.Hard)]
    public void BotPolicy_IsDeterministicAndLegal(BotDifficulty difficulty)
    {
        ChoiceRequest request = new(
            "choice-42", 5, 1, ChoiceKind.SelectAction, "choose",
            new[]
            {
                new ChoiceOption("a", ChoiceOptionKind.Action, "A", AiValue: 1),
                new ChoiceOption("b", ChoiceOptionKind.Action, "B", AiValue: 3),
                new ChoiceOption("disabled", ChoiceOptionKind.Action, "X", IsEnabled: false, AiValue: 100)
            });
        GameView view = EmptyView(request);
        DeterministicBotPolicy policy = new();
        BotContext context = new(difficulty, 12345, EvaluateFuture: (option, depth) => option.OptionId == "b" ? depth : 0);

        ChoiceResult first = policy.Choose(view, request, context);
        ChoiceResult second = policy.Choose(view, request, context);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Equal(first.StateRevision, second.StateRevision);
        Assert.Equal(first.IsCancelled, second.IsCancelled);
        Assert.Equal(first.SelectedOptionIds, second.SelectedOptionIds);
        Assert.True(request.Validate(first, 1, 5).IsValid);
        Assert.DoesNotContain("disabled", first.SelectedOptionIds);
    }

    [Fact]
    public void IdentityEngine_StartsAtPlayChoiceAndRedactsRolesAndHands()
    {
        (ContentRegistry content, _) = TestContent.Load();
        MatchConfig config = CreateConfig(content, BuiltInModeIds.Identity, 4);

        GameEngine engine = GameEngine.Start(config, content);

        Assert.Equal(MatchStatus.Running, engine.State.Status);
        Assert.Equal(GamePhase.Play, engine.State.Phase);
        Assert.NotNull(engine.State.PendingChoice);
        Assert.Equal(6, engine.State.Players[engine.State.CurrentSeatId].Hand.Count);
        GameView spectator = engine.BuildView(ViewerContext.Spectator);
        Assert.Empty(spectator.PrivateHandCardIds);
        Assert.Empty(spectator.PendingChoice!.Options);
        Assert.Single(spectator.Players.Where(player => player.Role == IdentityRole.Lord));
    }

    [Fact]
    public void FourBuiltInModes_ValidateTheirDeclaredPlayerCounts()
    {
        (ContentRegistry content, _) = TestContent.Load();
        GameModeRegistry modes = new();

        Assert.True(modes.Get(BuiltInModeIds.Duel).Validate(CreateConfig(content, BuiltInModeIds.Duel, 2), content).IsValid);
        Assert.True(modes.Get(BuiltInModeIds.Identity).Validate(CreateConfig(content, BuiltInModeIds.Identity, 4), content).IsValid);
        Assert.True(modes.Get(BuiltInModeIds.TeamTwoVersusTwo).Validate(CreateConfig(content, BuiltInModeIds.TeamTwoVersusTwo, 4), content).IsValid);

        MatchConfig boss = CreateConfig(content, BuiltInModeIds.Boss, 3) with
        {
            BossDefinitionId = "boss_jifenxin",
            Players = new[]
            {
                new PlayerConfig(1, "boss", "Boss", "jifenxin", SeatController.Bot, IsBoss: true),
                new PlayerConfig(2, "p2", "P2", "xingjianya"),
                new PlayerConfig(3, "p3", "P3", "huanglubaiquan")
            }
        };
        Assert.True(modes.Get(BuiltInModeIds.Boss).Validate(boss, content).IsValid);
    }

    private static MatchConfig CreateConfig(ContentRegistry content, string modeId, int playerCount)
    {
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(playerCount).ToArray();
        return new MatchConfig(
            $"test-{modeId}", modeId, 1234,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
    }

    private static GameView EmptyView(ChoiceRequest request) => new()
    {
        MatchId = "test", Revision = request.StateRevision, ModeId = BuiltInModeIds.Duel,
        Status = MatchStatus.Running, Phase = GamePhase.Play, CurrentSeatId = 1, RoundNumber = 1,
        Players = Array.Empty<PlayerView>(), PublicCards = Array.Empty<CardView>(), PrivateHandCardIds = Array.Empty<string>(),
        DrawPileCount = 0, DiscardPileCount = 0, PendingChoice = request, ContentPacks = Array.Empty<ContentPackReference>(),
        WinnerSeatIds = Array.Empty<int>(), ResultMessage = string.Empty, StateHash = string.Empty
    };
}
