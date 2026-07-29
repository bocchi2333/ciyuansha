using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CiyuanSha.GameCore.Tests;

public sealed class ReplayTests
{
    [Fact]
    public async Task Archive_RoundTripsJournalAndValidatesPacks()
    {
        (ContentRegistry content, _) = TestContent.Load();
        ContentPackReference[] packs = content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray();
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            "match",
            "duel",
            1,
            packs,
            new[] { new ReplayPlayerInfo(1, "A", "xingjianya") },
            "initial",
            "final",
            true);
        RuleJournal journal = new();
        RuleJournalEntry entry = journal.Append(JournalEntryKind.Checkpoint, "hash", message: "checkpoint");
        ReplayDocument source = new(header, new[] { entry }, new[] { new ReplayCheckpoint(1, "{}", "hash") });
        string path = Path.Combine(Path.GetTempPath(), $"ciyuansha-{Guid.NewGuid():N}.cysreplay");
        try
        {
            await ReplayArchive.WriteAsync(path, source);
            ReplayDocument loaded = await ReplayArchive.ReadAsync(path);

            Assert.Equal(source.Header.FormatVersion, loaded.Header.FormatVersion);
            Assert.Equal(source.Header.EngineApiVersion, loaded.Header.EngineApiVersion);
            Assert.Equal(source.Header.ProtocolVersion, loaded.Header.ProtocolVersion);
            Assert.Equal(source.Header.MatchId, loaded.Header.MatchId);
            Assert.Equal(source.Header.ModeId, loaded.Header.ModeId);
            Assert.Equal(source.Header.Seed, loaded.Header.Seed);
            Assert.Equal(source.Header.ContentPacks, loaded.Header.ContentPacks);
            Assert.Equal(source.Header.Players, loaded.Header.Players);
            Assert.Equal(source.Header.InitialStateHash, loaded.Header.InitialStateHash);
            Assert.Equal(source.Header.FinalStateHash, loaded.Header.FinalStateHash);
            Assert.Equal(source.Header.IsOmniscient, loaded.Header.IsOmniscient);
            Assert.Equal(source.Entries, loaded.Entries);
            Assert.True(ReplayArchive.ValidateContent(loaded.Header, packs).IsValid);
            Assert.False(ReplayArchive.ValidateContent(loaded.Header, packs.Skip(1).ToArray()).IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Archive_RejectsTamperedJournalEntry()
    {
        (ContentRegistry content, _) = TestContent.Load();
        ContentPackReference[] packs = content.Packs.Values.Select(pack => pack.Reference).ToArray();
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            "tamper",
            "duel",
            2,
            packs,
            Array.Empty<ReplayPlayerInfo>(),
            "initial",
            "final",
            true);
        RuleJournal journal = new();
        RuleJournalEntry valid = journal.Append(JournalEntryKind.Checkpoint, "state", message: "valid");
        RuleJournalEntry tampered = valid with { Message = "tampered" };
        string path = Path.Combine(Path.GetTempPath(), $"ciyuansha-tampered-{Guid.NewGuid():N}.cysreplay");
        try
        {
            await ReplayArchive.WriteAsync(path, new ReplayDocument(header, new[] { tampered }, Array.Empty<ReplayCheckpoint>()));
            await Assert.ThrowsAsync<InvalidDataException>(() => ReplayArchive.ReadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validation_RejectsWrongFormatEngineProtocolAndPackHash()
    {
        (ContentRegistry content, _) = TestContent.Load();
        ContentPackReference[] packs = content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray();
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            "versions",
            BuiltInModeIds.Duel,
            1,
            packs,
            Array.Empty<ReplayPlayerInfo>(),
            "initial",
            "final",
            true);

        Assert.True(ReplayArchive.ValidateContent(header, packs).IsValid);
        Assert.False(ReplayArchive.ValidateContent(header with { FormatVersion = "999" }, packs).IsValid);
        Assert.False(ReplayArchive.ValidateContent(header with { EngineApiVersion = "999" }, packs).IsValid);
        Assert.False(ReplayArchive.ValidateContent(header with { ProtocolVersion = 999 }, packs).IsValid);
        Assert.False(ReplayArchive.ValidateContent(header, packs.Select((pack, index) => index == 0
            ? pack with { ContentHash = new string('0', 64) }
            : pack).ToArray()).IsValid);
    }

    [Fact]
    public void Playback_ReexecutesChoicesAndMatchesFinalHash()
    {
        (ContentRegistry content, _) = TestContent.Load();
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(2).ToArray();
        MatchConfig config = new(
            "replay-simulation",
            BuiltInModeIds.Duel,
            81173,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index}", $"P{index}", general, SeatController.Bot)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
        GameEngine engine = GameEngine.Start(config, content);
        string initialHash = engine.State.ComputeCanonicalHash();
        DeterministicBotPolicy policy = new();
        int decisions = 0;
        while (engine.State.Status == MatchStatus.Running && decisions++ < 12000)
        {
            var request = engine.State.PendingChoice ?? throw new XunitException("Expected a replay choice.");
            GameView view = engine.BuildView(ViewerContext.ForPlayer(request.ActingSeatId));
            var choice = policy.Choose(view, request, new BotContext(BotDifficulty.Standard, config.Seed ^ (ulong)engine.State.Revision));
            EngineStepResult step = engine.Advance(request.ActingSeatId, choice);
            Assert.False(step.Progress is EngineProgress.Rejected or EngineProgress.Faulted, step.ErrorKey);
        }
        Assert.Equal(MatchStatus.Completed, engine.State.Status);
        JsonSerializerOptions jsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            config.MatchId,
            config.ModeId,
            config.Seed,
            config.ContentPacks,
            config.Players.Select(player => new ReplayPlayerInfo(player.SeatId, player.DisplayName, player.GeneralId)).ToArray(),
            initialHash,
            engine.State.ComputeCanonicalHash(),
            true,
            JsonSerializer.Serialize(config, jsonOptions));
        ReplayDocument document = new(header, engine.Journal.Entries.ToArray(), Array.Empty<ReplayCheckpoint>());

        ReplayPlaybackSession playback = new(document, content);
        ReplayVerificationResult verification = playback.Verify();

        Assert.True(verification.IsValid, $"{verification.ErrorKey} at {verification.DivergentSequence}");
        Assert.Equal(header.FinalStateHash, playback.CurrentView.StateHash);
    }

    [Fact]
    public async Task LimitedReplay_ContainsOnlyRedactedEventsAndLockedViewCheckpoints()
    {
        (ContentRegistry content, _) = TestContent.Load();
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(2).ToArray();
        MatchConfig config = new(
            "limited-replay", BuiltInModeIds.Duel, 6621,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray());
        ViewerContext viewer = ViewerContext.ForPlayer(1);
        GameEngine engine = GameEngine.Start(config, content);
        List<ReplayViewCheckpoint> views = new() { new ReplayViewCheckpoint(0, engine.BuildView(viewer)) };
        DeterministicBotPolicy policy = new();
        for (int decision = 0; decision < 8 && engine.State.Status == MatchStatus.Running; decision++)
        {
            ChoiceRequest request = engine.State.PendingChoice ?? throw new XunitException("Expected a limited replay choice.");
            ChoiceResult choice = policy.Choose(
                engine.BuildView(ViewerContext.ForPlayer(request.ActingSeatId)),
                request,
                new BotContext(BotDifficulty.Standard, config.Seed ^ (ulong)engine.State.Revision));
            EngineStepResult step = engine.Advance(request.ActingSeatId, choice);
            Assert.False(step.Progress is EngineProgress.Rejected or EngineProgress.Faulted, step.ErrorKey);
            views.Add(new ReplayViewCheckpoint(engine.Journal.Cursor, engine.BuildView(viewer)));
        }
        RuleJournalDelta redacted = RuleJournalVisibility.Redact(
            new RuleJournalDelta(0, engine.Journal.Cursor, engine.Journal.Entries.ToArray()), viewer);
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            config.MatchId,
            config.ModeId,
            config.Seed,
            config.ContentPacks,
            config.Players.Select(player => new ReplayPlayerInfo(player.SeatId, player.DisplayName, player.GeneralId)).ToArray(),
            views[0].View.StateHash,
            views[^1].View.StateHash,
            false,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }),
            1);
        ReplayDocument document = new(header, redacted.Entries, Array.Empty<ReplayCheckpoint>(), views);
        string path = Path.Combine(Path.GetTempPath(), $"ciyuansha-limited-{Guid.NewGuid():N}.cysreplay");
        try
        {
            await ReplayArchive.WriteAsync(path, document);
            ReplayDocument loaded = await ReplayArchive.ReadAsync(path);
            Assert.All(loaded.Entries, entry =>
            {
                Assert.Equal(JournalEntryKind.RuleEvent, entry.Kind);
                Assert.Null(entry.ChoiceResult);
                Assert.Null(entry.RandomValue);
                Assert.Empty(entry.StateHash);
                Assert.Empty(entry.EntryHash);
            });
            Assert.Empty(loaded.Checkpoints);
            Assert.NotEmpty(loaded.SafeViewCheckpoints);
            Assert.All(loaded.SafeViewCheckpoints.SelectMany(checkpoint => checkpoint.View.PrivateHandCards), card => Assert.Equal(1, card.OwnerSeatId));

            ReplayPlaybackSession playback = new(loaded, content);
            playback.Seek(playback.MaximumCursor);
            Assert.True(playback.Verify().IsValid);
            Assert.Equal(1, playback.Viewer.SeatId);
            Assert.Throws<InvalidOperationException>(() => playback.SetViewer(ViewerContext.OmniscientReplay));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
