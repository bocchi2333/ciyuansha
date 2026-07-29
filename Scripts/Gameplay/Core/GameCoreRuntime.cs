using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Events;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.GameCore.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.Gameplay.Core;

/// <summary>
/// Owns the Godot-independent authoritative rules runtime. Godot nodes consume
/// redacted views and submit ChoiceResult values; they never mutate GameState.
/// </summary>
public sealed class GameCoreRuntime
{
    public const string DefaultDeckId = "standard_military_161";

    private readonly Dictionary<int, string> _seatTokens = new();
    private readonly DeterministicBotPolicy _botPolicy = new();
    private readonly GameModeRegistry _modes = new();
    private string _initialStateHash = string.Empty;

    private GameCoreRuntime(ContentRegistry content, EffectRegistry effects, string packsRoot)
    {
        Content = content;
        Effects = effects;
        PacksRoot = packsRoot;
    }

    public ContentRegistry Content { get; }

    public EffectRegistry Effects { get; }

    public string PacksRoot { get; }

    public GameEngine? Engine { get; private set; }

    public MatchConfig? MatchConfig { get; private set; }

    public IReadOnlyDictionary<int, string> SeatTokens => _seatTokens;

    public IReadOnlyList<ContentPackReference> ContentPacks => Content.Packs.Values
        .OrderBy(pack => pack.Manifest.PackId, StringComparer.Ordinal)
        .Select(pack => pack.Reference)
        .ToArray();

    public bool IsRunning => Engine?.State.Status == MatchStatus.Running;

    public static GameCoreRuntime LoadDefault()
    {
        string packsRoot = ProjectSettings.GlobalizePath("res://Data/Packs");
        return Load(packsRoot);
    }

    public static GameCoreRuntime Load(string packsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packsRoot);
        string fullRoot = Path.GetFullPath(packsRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Content pack root does not exist: {fullRoot}");
        }

        EffectRegistry effects = BuiltInEffects.CreateRegistry();
        LoadedContentPack[] packs = Directory.GetDirectories(fullRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .Select(directory => ContentPackLoader.LoadDirectory(directory))
            .ToArray();
        ContentRegistry content = new();
        ContentValidationResult validation = content.Register(packs, effects);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(System.Environment.NewLine, validation.Errors));
        }

        return new GameCoreRuntime(content, effects, fullRoot);
    }

    public MatchConfig CreateMatchConfig(
        string modeId,
        IEnumerable<LanPlayerInfo> lobbyPlayers,
        ulong seed,
        string matchId = "",
        string bossDefinitionId = "boss_jifenxin")
    {
        ArgumentNullException.ThrowIfNull(lobbyPlayers);
        List<LanPlayerInfo> participants = lobbyPlayers
            .Where(player => !player.IsSpectator)
            .OrderBy(player => player.SeatId > 0 ? player.SeatId : player.PeerId)
            .ToList();
        List<PlayerConfig> players = new();
        int nextSeat = 1;
        foreach (LanPlayerInfo participant in participants)
        {
            int seatId = participant.SeatId > 0 ? participant.SeatId : nextSeat;
            nextSeat = Math.Max(nextSeat + 1, seatId + 1);
            string generalId = ResolveGeneralId(participant.CharacterId);
            bool isBoss = modeId == BuiltInModeIds.Boss && participant.IsBoss;
            players.Add(new PlayerConfig(
                seatId,
                participant.PlayerId.Length > 0 ? participant.PlayerId : $"peer-{participant.PeerId}",
                participant.PlayerName,
                generalId,
                participant.IsBot ? SeatController.Bot : SeatController.Human,
                isBoss,
                participant.BotDifficulty));
        }

        if (modeId == BuiltInModeIds.Boss && players.All(player => !player.IsBoss))
        {
            int bossSeat = players.Count == 0 ? 1 : players.Max(player => player.SeatId) + 1;
            players.Add(new PlayerConfig(
                bossSeat,
                "boss",
                "Boss",
                Content.Bosses[bossDefinitionId].GeneralId,
                SeatController.Bot,
                IsBoss: true,
                BotDifficulty.Hard));
        }

        return new MatchConfig(
            string.IsNullOrWhiteSpace(matchId) ? Guid.NewGuid().ToString("N") : matchId,
            modeId,
            seed,
            players,
            DefaultDeckId,
            ContentPacks,
            players.FirstOrDefault()?.SeatId ?? 0,
            modeId == BuiltInModeIds.Boss ? bossDefinitionId : string.Empty);
    }

    public EngineStepResult Start(MatchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Engine = GameEngine.Start(config, Content, _modes);
        MatchConfig = config;
        _initialStateHash = Engine.State.ComputeCanonicalHash();
        _seatTokens.Clear();
        foreach (PlayerConfig player in config.Players.Where(player => player.Controller == SeatController.Human))
        {
            _seatTokens[player.SeatId] = ReconnectTokenFactory.Create();
        }

        return AdvanceBots(new EngineStepResult(
            Engine.State.PendingChoice is null ? EngineProgress.Progressed : EngineProgress.WaitingForChoice,
            Engine.State.Revision,
            Array.Empty<RuleEvent>(),
            Engine.State.PendingChoice,
            Engine.State.ComputeCanonicalHash()));
    }

    public EngineStepResult SubmitChoice(int seatId, string reconnectToken, ChoiceResult result)
    {
        if (Engine is null)
        {
            return Rejected("engine.not_started");
        }
        if (!_seatTokens.TryGetValue(seatId, out string? expected)
            || !ReconnectTokenFactory.FixedTimeEquals(expected, reconnectToken))
        {
            return Rejected("network.invalid_reconnect_token");
        }

        return AdvanceBots(Engine.Advance(seatId, result));
    }

    public GameView BuildView(ViewerContext viewer) =>
        Engine?.BuildView(viewer) ?? throw new InvalidOperationException("The GameCore match has not started.");

    public MatchSnapshotV2 BuildSnapshot(ViewerContext viewer) => new(
        ProtocolV2.Version,
        ProtocolV2.EngineApiVersion,
        Engine?.Journal.Cursor ?? 0,
        BuildView(viewer));

    public RuleJournalDelta? BuildDelta(long afterCursor) => Engine?.BuildDelta(afterCursor);

    public bool TryResumeSeat(int seatId, string token) =>
        _seatTokens.TryGetValue(seatId, out string? expected)
        && ReconnectTokenFactory.FixedTimeEquals(expected, token);

    public void AssignSeatToken(int seatId, string token)
    {
        if (seatId <= 0 || string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A positive seat and non-empty token are required.");
        }
        _seatTokens[seatId] = token;
    }

    public async Task ExportReplayAsync(string path, bool omniscient, CancellationToken cancellationToken = default)
    {
        if (Engine is null || MatchConfig is null)
        {
            throw new InvalidOperationException("No match is available to export.");
        }

        string finalHash = Engine.State.ComputeCanonicalHash();
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            MatchConfig.MatchId,
            MatchConfig.ModeId,
            MatchConfig.Seed,
            MatchConfig.ContentPacks,
            MatchConfig.Players.Select(player => new ReplayPlayerInfo(player.SeatId, player.DisplayName, player.GeneralId)).ToArray(),
            _initialStateHash,
            finalHash,
            omniscient,
            JsonSerializer.Serialize(MatchConfig, ReplayJsonOptions));
        string stateJson = JsonSerializer.Serialize(Engine.State, ReplayJsonOptions);
        ReplayCheckpoint checkpoint = new(Engine.Journal.Cursor, stateJson, finalHash);
        ReplayDocument document = new(header, Engine.Journal.Entries.ToArray(), new[] { checkpoint });
        await ReplayArchive.WriteAsync(path, document, cancellationToken);
    }

    public async Task ExportReproductionBundleAsync(string path, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (Engine is null || MatchConfig is null)
        {
            throw new InvalidOperationException("No match is available for reproduction export.");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await using FileStream file = new(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: true);
        await WriteZipJsonAsync(archive, "match-config.json", MatchConfig, cancellationToken);
        await WriteZipJsonAsync(archive, "state.json", Engine.State, cancellationToken);
        await WriteZipJsonAsync(archive, "journal-tail.json", Engine.Journal.Entries.TakeLast(128).ToArray(), cancellationToken);
        await WriteZipJsonAsync(archive, "fault.json", new
        {
            EngineVersion = ProtocolV2.EngineApiVersion,
            StateHash = Engine.State.ComputeCanonicalHash(),
            JournalCursor = Engine.Journal.Cursor,
            Exception = exception?.ToString() ?? Engine.State.ResultMessage
        }, cancellationToken);
    }

    private EngineStepResult AdvanceBots(EngineStepResult current)
    {
        if (Engine is null || MatchConfig is null)
        {
            return current;
        }

        EngineStepResult step = current;
        int decisions = 0;
        while (step.Progress == EngineProgress.WaitingForChoice
            && step.PendingChoice is ChoiceRequest request
            && Engine.State.Players.TryGetValue(request.ActingSeatId, out PlayerState? player)
            && player.Controller == SeatController.Bot)
        {
            if (++decisions > 4096)
            {
                throw new InvalidOperationException("Bot decision safety limit exceeded.");
            }
            PlayerConfig config = MatchConfig.Players.Single(item => item.SeatId == request.ActingSeatId);
            GameView view = Engine.BuildView(ViewerContext.ForPlayer(request.ActingSeatId));
            ulong decisionSeed = MatchConfig.Seed ^ (ulong)Engine.State.Revision ^ ((ulong)(uint)request.ActingSeatId << 32);
            ChoiceResult choice = _botPolicy.Choose(view, request, new BotContext(config.BotDifficulty, decisionSeed));
            step = Engine.Advance(request.ActingSeatId, choice);
        }
        return step;
    }

    private EngineStepResult Rejected(string errorKey) => new(
        EngineProgress.Rejected,
        Engine?.State.Revision ?? 0,
        Array.Empty<RuleEvent>(),
        Engine?.State.PendingChoice,
        Engine?.State.ComputeCanonicalHash() ?? string.Empty,
        errorKey);

    private string ResolveGeneralId(string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Content.Generals.ContainsKey(requested))
        {
            return requested;
        }
        return Content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).First();
    }

    private static async Task WriteZipJsonAsync<T>(ZipArchive archive, string name, T value, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, ReplayJsonOptions, cancellationToken);
    }

    private static readonly JsonSerializerOptions ReplayJsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
}
