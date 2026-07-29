using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Networking;

namespace CiyuanSha.GameCore.Replay;

public sealed record ReplayValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ReplayValidationResult Valid { get; } = new(true, Array.Empty<string>());
}

public static class ReplayArchive
{
    public const string FileExtension = ".cysreplay";
    public const string FormatVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task WriteAsync(string path, ReplayDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: true);
        await WriteJsonEntryAsync(archive, "header.json", document.Header, cancellationToken).ConfigureAwait(false);

        ZipArchiveEntry eventsEntry = archive.CreateEntry("events.jsonl", CompressionLevel.SmallestSize);
        await using (Stream eventStream = eventsEntry.Open())
        await using (StreamWriter writer = new(eventStream, new UTF8Encoding(false)))
        {
            foreach (RuleJournalEntry entry in document.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions)).ConfigureAwait(false);
            }
        }

        await WriteJsonEntryAsync(archive, "checkpoints.json", document.Checkpoints, cancellationToken).ConfigureAwait(false);
        if (document.SafeViewCheckpoints.Count > 0)
        {
            await WriteJsonEntryAsync(archive, "views.json", document.SafeViewCheckpoints, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<ReplayDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive archive = new(file, ZipArchiveMode.Read, leaveOpen: true);
        ReplayHeader header = await ReadJsonEntryAsync<ReplayHeader>(archive, "header.json", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ReplayCheckpoint> checkpoints = await ReadJsonEntryAsync<List<ReplayCheckpoint>>(archive, "checkpoints.json", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ReplayViewCheckpoint> views = archive.GetEntry("views.json") is null
            ? Array.Empty<ReplayViewCheckpoint>()
            : await ReadJsonEntryAsync<List<ReplayViewCheckpoint>>(archive, "views.json", cancellationToken).ConfigureAwait(false);

        ZipArchiveEntry eventsEntry = archive.GetEntry("events.jsonl")
            ?? throw new InvalidDataException("Replay is missing events.jsonl.");
        List<RuleJournalEntry> events = new();
        await using Stream eventStream = eventsEntry.Open();
        using StreamReader reader = new(eventStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            events.Add(JsonSerializer.Deserialize<RuleJournalEntry>(line, JsonOptions)
                ?? throw new InvalidDataException("Replay contains an invalid journal entry."));
        }

        ValidateMonotonicEntries(events, requireContiguous: header.IsOmniscient);
        if (header.IsOmniscient)
        {
            ValidateHashChain(events);
        }
        ValidateViewCheckpoints(views);
        return new ReplayDocument(header, events, checkpoints, views);
    }

    public static ReplayValidationResult ValidateContent(ReplayHeader header, IReadOnlyList<ContentPackReference> availablePacks)
    {
        List<string> errors = new();
        if (!string.Equals(header.FormatVersion, FormatVersion, StringComparison.Ordinal))
        {
            errors.Add($"Unsupported replay format: {header.FormatVersion}");
        }

        if (!string.Equals(header.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
        {
            errors.Add($"Replay engine mismatch: {header.EngineApiVersion}");
        }

        if (header.ProtocolVersion != ProtocolV2.Version)
        {
            errors.Add($"Replay protocol mismatch: {header.ProtocolVersion}");
        }

        Dictionary<string, ContentPackReference> available = availablePacks.ToDictionary(pack => pack.PackId, StringComparer.Ordinal);
        foreach (ContentPackReference required in header.ContentPacks)
        {
            if (!available.TryGetValue(required.PackId, out ContentPackReference? actual))
            {
                errors.Add($"Missing content pack: {required.PackId} {required.Version}");
                continue;
            }

            if (!string.Equals(required.Version, actual.Version, StringComparison.Ordinal)
                || !string.Equals(required.ContentHash, actual.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Content pack mismatch: {required.PackId}");
            }
        }

        return errors.Count == 0 ? ReplayValidationResult.Valid : new ReplayValidationResult(false, errors);
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string name, T value, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadJsonEntryAsync<T>(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Replay is missing {name}.");
        await using Stream stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Replay contains invalid {name}.");
    }

    private static void ValidateMonotonicEntries(IReadOnlyList<RuleJournalEntry> entries, bool requireContiguous)
    {
        long previous = 0;
        foreach (RuleJournalEntry entry in entries)
        {
            if (entry.Sequence <= previous || (requireContiguous && entry.Sequence != previous + 1))
            {
                throw new InvalidDataException("Replay journal sequence is missing or out of order.");
            }
            previous = entry.Sequence;
        }
    }

    private static void ValidateViewCheckpoints(IReadOnlyList<ReplayViewCheckpoint> views)
    {
        long previous = -1;
        foreach (ReplayViewCheckpoint view in views)
        {
            if (view.JournalSequence <= previous)
            {
                throw new InvalidDataException("Replay view checkpoints are duplicated or out of order.");
            }
            previous = view.JournalSequence;
        }
    }

    private static void ValidateHashChain(IReadOnlyList<RuleJournalEntry> entries)
    {
        string previous = string.Empty;
        foreach (RuleJournalEntry entry in entries)
        {
            if (!string.Equals(entry.PreviousEntryHash, previous, StringComparison.Ordinal)
                || !string.Equals(entry.EntryHash, RuleJournal.ComputeEntryHash(entry), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Replay journal hash chain failed at sequence {entry.Sequence}.");
            }
            previous = entry.EntryHash;
        }
    }
}
