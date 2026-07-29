using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.Replay;

/// <summary>
/// Client-side limited replay capture. It stores only already-redacted journal
/// entries and GameViews received from the host; it never owns a GameState.
/// </summary>
public sealed class LocalReplayCaptureV2
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SortedDictionary<long, RuleJournalEntry> _entries = new();
    private readonly SortedDictionary<long, ReplayViewCheckpoint> _views = new();
    private MatchStartInfo? _start;
    private bool _saved;

    public string LastSavedPath { get; private set; } = string.Empty;

    public void Begin(MatchStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (_start?.MatchId == start.MatchId) return;
        _start = start;
        _saved = false;
        LastSavedPath = string.Empty;
        _entries.Clear();
        _views.Clear();
    }

    public void Record(long cursor, IEnumerable<RuleJournalEntry> entries, GameView view)
    {
        if (_start is null || !string.Equals(_start.MatchId, view.MatchId, StringComparison.Ordinal)) return;
        foreach (RuleJournalEntry entry in entries)
        {
            if (entry.ChoiceResult is not null || entry.RandomValue is not null
                || !string.IsNullOrEmpty(entry.EntryHash) || !string.IsNullOrEmpty(entry.PreviousEntryHash))
            {
                throw new InvalidDataException("A limited replay capture received non-redacted journal data.");
            }
            _entries[entry.Sequence] = entry;
        }
        if (_views.Count == 0) _views[0] = new ReplayViewCheckpoint(0, view);
        _views[cursor] = new ReplayViewCheckpoint(cursor, view);
    }

    public string? SaveIfCompleted(ViewerContext viewer, IReadOnlyCollection<LanPlayerInfo> lobbyPlayers)
    {
        if (_saved || _start is null || _views.Count == 0 || _views.Values.Last().View.Status != MatchStatus.Completed)
        {
            return null;
        }

        GameView finalView = _views.Values.Last().View;
        IReadOnlyList<PlayerConfig> players = finalView.Players
            .OrderBy(player => player.SeatId)
            .Select(player => new PlayerConfig(
                player.SeatId,
                lobbyPlayers.FirstOrDefault(lobby => lobby.SeatId == player.SeatId)?.PlayerId ?? $"seat-{player.SeatId}",
                player.DisplayName,
                player.GeneralId,
                player.Controller,
                player.Role == IdentityRole.Boss,
                lobbyPlayers.FirstOrDefault(lobby => lobby.SeatId == player.SeatId)?.BotDifficulty ?? BotDifficulty.Standard))
            .ToArray();
        MatchConfig config = new(
            _start.MatchId,
            _start.ModeId,
            _start.Seed,
            players,
            GameCoreRuntime.DefaultDeckId,
            _start.ContentPacks,
            players.FirstOrDefault()?.SeatId ?? 0,
            _start.ModeId == BuiltInModeIds.Boss ? "boss_jifenxin" : string.Empty);
        ReplayHeader header = new(
            ReplayArchive.FormatVersion,
            ProtocolV2.EngineApiVersion,
            ProtocolV2.Version,
            _start.MatchId,
            _start.ModeId,
            _start.Seed,
            _start.ContentPacks,
            players.Select(player => new ReplayPlayerInfo(player.SeatId, player.DisplayName, player.GeneralId)).ToArray(),
            _views[0].View.StateHash,
            finalView.StateHash,
            false,
            JsonSerializer.Serialize(config, JsonOptions),
            viewer.Role == ViewerRole.Player ? viewer.SeatId ?? 0 : 0);
        ReplayDocument document = new(
            header,
            _entries.Values.ToArray(),
            Array.Empty<ReplayCheckpoint>(),
            _views.Values.ToArray());

        string directory = ProjectSettings.GlobalizePath("user://Replays");
        Directory.CreateDirectory(directory);
        string label = viewer.Role == ViewerRole.Player ? $"seat-{viewer.SeatId}" : "public";
        LastSavedPath = Path.Combine(directory, $"{_start.MatchId}-{label}.cysreplay");
        Task.Run(() => ReplayArchive.WriteAsync(LastSavedPath, document)).GetAwaiter().GetResult();
        _saved = true;
        return LastSavedPath;
    }
}
