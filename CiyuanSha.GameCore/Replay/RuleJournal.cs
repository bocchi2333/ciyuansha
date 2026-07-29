using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Events;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CiyuanSha.GameCore.Replay;

public enum JournalEntryKind
{
    RuleEvent = 0,
    ChoiceAccepted = 1,
    RandomConsumed = 2,
    Checkpoint = 3,
    Fault = 4
}

public sealed record RuleJournalEntry(
    long Sequence,
    JournalEntryKind Kind,
    string StateHash,
    RuleEvent? RuleEvent = null,
    ChoiceResult? ChoiceResult = null,
    ulong? RandomValue = null,
    string Message = "",
    string PreviousEntryHash = "",
    string EntryHash = "");

public sealed record RuleJournalDelta(
    long FromCursor,
    long ToCursor,
    IReadOnlyList<RuleJournalEntry> Entries);

public sealed class RuleJournal
{
    private readonly List<RuleJournalEntry> _entries = new();
    private readonly int _retainedDeltaEntries;

    public RuleJournal(int retainedDeltaEntries = 512)
    {
        if (retainedDeltaEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedDeltaEntries));
        }
        _retainedDeltaEntries = retainedDeltaEntries;
    }

    public long Cursor => _entries.Count == 0 ? 0 : _entries[^1].Sequence;

    public IReadOnlyList<RuleJournalEntry> Entries => _entries;

    public RuleJournalEntry Append(
        JournalEntryKind kind,
        string stateHash,
        RuleEvent? ruleEvent = null,
        ChoiceResult? choiceResult = null,
        ulong? randomValue = null,
        string message = "")
    {
        RuleJournalEntry entry = new(
            Cursor + 1,
            kind,
            stateHash,
            ruleEvent,
            choiceResult,
            randomValue,
            message,
            _entries.LastOrDefault()?.EntryHash ?? string.Empty);
        entry = entry with { EntryHash = ComputeEntryHash(entry) };
        _entries.Add(entry);
        return entry;
    }

    public static string ComputeEntryHash(RuleJournalEntry entry)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            entry.Sequence,
            Kind = (int)entry.Kind,
            entry.StateHash,
            entry.RuleEvent,
            entry.ChoiceResult,
            entry.RandomValue,
            entry.Message,
            entry.PreviousEntryHash
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public bool TryGetDelta(long afterCursor, out RuleJournalDelta delta)
    {
        long oldestRetained = Math.Max(0, Cursor - _retainedDeltaEntries);
        if (afterCursor < oldestRetained || afterCursor > Cursor)
        {
            delta = new RuleJournalDelta(afterCursor, Cursor, Array.Empty<RuleJournalEntry>());
            return false;
        }

        IReadOnlyList<RuleJournalEntry> entries = _entries
            .Where(entry => entry.Sequence > afterCursor)
            .ToArray();
        delta = new RuleJournalDelta(afterCursor, Cursor, entries);
        return true;
    }
}

public sealed record ReplayHeader(
    string FormatVersion,
    string EngineApiVersion,
    int ProtocolVersion,
    string MatchId,
    string ModeId,
    ulong Seed,
    IReadOnlyList<ContentPackReference> ContentPacks,
    IReadOnlyList<ReplayPlayerInfo> Players,
    string InitialStateHash,
    string FinalStateHash,
    bool IsOmniscient,
    string MatchConfigJson = "");

public sealed record ReplayPlayerInfo(int SeatId, string DisplayName, string GeneralId);

public sealed record ReplayCheckpoint(long JournalSequence, string StateJson, string StateHash);

public sealed record ReplayDocument(
    ReplayHeader Header,
    IReadOnlyList<RuleJournalEntry> Entries,
    IReadOnlyList<ReplayCheckpoint> Checkpoints);
