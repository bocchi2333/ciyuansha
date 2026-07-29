using System.Text.Json;
using System.Text.Json.Serialization;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Replay;

public sealed record ReplayVerificationResult(bool IsValid, string ErrorKey, long DivergentSequence = 0)
{
    public static ReplayVerificationResult Valid { get; } = new(true, string.Empty);
}

/// <summary>
/// Offline replay controller. It never starts networking or AI; seeking rebuilds
/// the deterministic engine from accepted ChoiceResult entries.
/// </summary>
public sealed class ReplayPlaybackSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ReplayDocument _document;
    private readonly ContentRegistry _content;
    private readonly GameModeRegistry _modes;
    private readonly MatchConfig _config;
    private GameEngine _engine;

    public ReplayPlaybackSession(ReplayDocument document, ContentRegistry content, GameModeRegistry? modes = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _modes = modes ?? new GameModeRegistry();
        ReplayValidationResult validation = ReplayArchive.ValidateContent(document.Header, content.Packs.Values.Select(pack => pack.Reference).ToArray());
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        }
        if (string.IsNullOrWhiteSpace(document.Header.MatchConfigJson))
        {
            throw new InvalidDataException("Replay is missing its deterministic match configuration.");
        }
        _config = JsonSerializer.Deserialize<MatchConfig>(document.Header.MatchConfigJson, JsonOptions)
            ?? throw new InvalidDataException("Replay match configuration is invalid.");
        _engine = GameEngine.Start(_config, _content, _modes);
        IsPaused = true;
        Speed = 1;
        Viewer = document.Header.IsOmniscient ? ViewerContext.OmniscientReplay : ViewerContext.Spectator;
    }

    public long Cursor { get; private set; }

    public long MaximumCursor => _document.Entries.LastOrDefault()?.Sequence ?? 0;

    public bool IsPaused { get; private set; }

    public double Speed { get; private set; }

    public ViewerContext Viewer { get; private set; }

    public RuleJournalEntry? CurrentEntry => _document.Entries.LastOrDefault(entry => entry.Sequence <= Cursor);

    public GameView CurrentView => _engine.BuildView(Viewer);

    public void Play() => IsPaused = false;

    public void Pause() => IsPaused = true;

    public void SetSpeed(double speed)
    {
        if (speed is not (0.5 or 1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Replay speed must be 0.5, 1, 2, or 4.");
        }
        Speed = speed;
    }

    public void SetViewer(ViewerContext viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (!_document.Header.IsOmniscient && viewer.Role == ViewerRole.OmniscientReplay)
        {
            throw new InvalidOperationException("This replay does not contain an omniscient view.");
        }
        if (viewer.Role == ViewerRole.Player
            && (viewer.SeatId is null || _document.Header.Players.All(player => player.SeatId != viewer.SeatId)))
        {
            throw new ArgumentOutOfRangeException(nameof(viewer), "Replay player seat is unknown.");
        }
        Viewer = viewer;
    }

    public bool StepForward()
    {
        if (Cursor >= MaximumCursor)
        {
            Pause();
            return false;
        }
        Seek(Cursor + 1);
        return true;
    }

    public bool StepBackward()
    {
        if (Cursor == 0)
        {
            return false;
        }
        Seek(Cursor - 1);
        return true;
    }

    public int Tick()
    {
        if (IsPaused)
        {
            return 0;
        }
        int steps = Speed < 1 ? 1 : (int)Speed;
        int advanced = 0;
        for (int index = 0; index < steps && StepForward(); index++)
        {
            advanced++;
        }
        return advanced;
    }

    public void Seek(long journalSequence)
    {
        if (journalSequence < 0 || journalSequence > MaximumCursor)
        {
            throw new ArgumentOutOfRangeException(nameof(journalSequence));
        }
        _engine = GameEngine.Start(_config, _content, _modes);
        foreach (RuleJournalEntry entry in _document.Entries.Where(entry => entry.Sequence <= journalSequence && entry.Kind == JournalEntryKind.ChoiceAccepted))
        {
            if (entry.ChoiceResult is null || _engine.State.PendingChoice is null)
            {
                throw new InvalidDataException($"Replay choice cannot be applied at sequence {entry.Sequence}.");
            }
            EngineStepResult step = _engine.Advance(_engine.State.PendingChoice.ActingSeatId, entry.ChoiceResult);
            if (step.Progress is EngineProgress.Rejected or EngineProgress.Faulted)
            {
                throw new InvalidDataException($"Replay diverged at sequence {entry.Sequence}: {step.ErrorKey}.");
            }
        }
        Cursor = journalSequence;
    }

    public ReplayVerificationResult Verify()
    {
        try
        {
            Seek(MaximumCursor);
        }
        catch (InvalidDataException exception)
        {
            long sequence = CurrentEntry?.Sequence ?? 0;
            return new ReplayVerificationResult(false, exception.Message, sequence);
        }
        IReadOnlyList<RuleJournalEntry> generated = _engine.Journal.Entries;
        int sharedCount = Math.Min(generated.Count, _document.Entries.Count);
        for (int index = 0; index < sharedCount; index++)
        {
            if (!string.Equals(generated[index].EntryHash, _document.Entries[index].EntryHash, StringComparison.OrdinalIgnoreCase))
            {
                return new ReplayVerificationResult(false, "replay.journal_diverged", index + 1);
            }
        }
        if (generated.Count != _document.Entries.Count)
        {
            return new ReplayVerificationResult(false, "replay.journal_length_mismatch", sharedCount + 1);
        }
        string finalHash = _engine.State.ComputeCanonicalHash();
        return string.Equals(finalHash, _document.Header.FinalStateHash, StringComparison.OrdinalIgnoreCase)
            ? ReplayVerificationResult.Valid
            : new ReplayVerificationResult(false, "replay.final_state_hash_mismatch", MaximumCursor);
    }
}
