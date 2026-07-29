using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Events;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Replay;

namespace CiyuanSha.GameCore.Tests;

public sealed class NetworkPrivacyTests
{
    [Fact]
    public void Views_ExposeOnlyTheViewerHandAndUseRedactedHashes()
    {
        ContentRegistry content = TestContent.Load().Registry;
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(4).ToArray();
        MatchConfig config = new(
            "privacy", BuiltInModeIds.Identity, 9182,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
        GameEngine engine = GameEngine.Start(config, content);

        GameView player = engine.BuildView(ViewerContext.ForPlayer(2));
        GameView other = engine.BuildView(ViewerContext.ForPlayer(3));
        GameView spectator = engine.BuildView(ViewerContext.Spectator);

        Assert.Equal(engine.State.Players[2].Hand.Count, player.PrivateHandCards.Count);
        Assert.All(player.PrivateHandCards, card => Assert.Equal(2, card.OwnerSeatId));
        Assert.DoesNotContain(player.PrivateHandCards, card => card.OwnerSeatId == 3);
        Assert.All(other.PrivateHandCards, card => Assert.Equal(3, card.OwnerSeatId));
        Assert.Empty(spectator.PrivateHandCards);
        Assert.Empty(spectator.PendingChoice!.Options);
        Assert.NotEqual(engine.State.ComputeCanonicalHash(), spectator.StateHash);
        Assert.NotEqual(player.StateHash, other.StateHash);
    }

    [Fact]
    public void JournalDelta_RemovesChoicesRandomHashesAndOpponentDrawIdentity()
    {
        RuleJournal journal = new();
        journal.Append(JournalEntryKind.ChoiceAccepted, "full", choiceResult: ChoiceResult.Select("choice", 1, "card:secret"));
        journal.Append(JournalEntryKind.RandomConsumed, "full", randomValue: 42);
        RuleEvent draw = new(
            "draw", 1, string.Empty, RuleEventKind.CardMoved, RuleEventStage.Completed, 2, new[] { 2 },
            new CardMoveRuleEventPayload("card:secret", CardZone.DrawPile, CardZone.Hand, 0, 2));
        journal.Append(JournalEntryKind.RuleEvent, "full", draw);
        RuleJournalDelta raw = new(0, journal.Cursor, journal.Entries.ToArray());

        RuleJournalDelta spectator = RuleJournalVisibility.Redact(raw, ViewerContext.Spectator);
        RuleJournalEntry visible = Assert.Single(spectator.Entries);
        Assert.Equal(JournalEntryKind.RuleEvent, visible.Kind);
        Assert.Empty(visible.StateHash);
        Assert.Empty(visible.EntryHash);
        CardMoveRuleEventPayload move = Assert.IsType<CardMoveRuleEventPayload>(visible.RuleEvent!.Payload);
        Assert.Equal("hidden", move.CardInstanceId);

        RuleJournalDelta owner = RuleJournalVisibility.Redact(raw, ViewerContext.ForPlayer(2));
        CardMoveRuleEventPayload ownerMove = Assert.IsType<CardMoveRuleEventPayload>(Assert.Single(owner.Entries).RuleEvent!.Payload);
        Assert.Equal("card:secret", ownerMove.CardInstanceId);
    }

    [Fact]
    public void JournalDelta_UsesIncrementalWindowAndSignalsSnapshotFallback()
    {
        RuleJournal journal = new(retainedDeltaEntries: 2);
        journal.Append(JournalEntryKind.Checkpoint, "one");
        journal.Append(JournalEntryKind.Checkpoint, "two");
        journal.Append(JournalEntryKind.Checkpoint, "three");

        Assert.False(journal.TryGetDelta(0, out RuleJournalDelta stale));
        Assert.Empty(stale.Entries);
        Assert.Equal(3, stale.ToCursor);
        Assert.True(journal.TryGetDelta(1, out RuleJournalDelta retained));
        Assert.Equal(new long[] { 2, 3 }, retained.Entries.Select(entry => entry.Sequence));
        Assert.False(journal.TryGetDelta(4, out _));
    }
}
