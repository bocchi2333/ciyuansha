using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Skills;

namespace CiyuanSha.GameCore.Content;

public sealed record ContentPackReference(string PackId, string Version, string ContentHash);

public sealed record ContentPackDependency(string PackId, string MinimumVersion);

public sealed class ContentPackManifest
{
    public required string PackId { get; init; }

    public required string DisplayName { get; init; }

    public required string Version { get; init; }

    public required string EngineApiVersion { get; init; }

    public required string License { get; init; }

    public required string Attribution { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public List<ContentPackDependency> Dependencies { get; init; } = new();

    public List<string> ContentFiles { get; init; } = new();
}

public sealed class ContentPackData
{
    public List<CardDefinition> Cards { get; init; } = new();

    public List<GeneralDefinitionV2> Generals { get; init; } = new();

    public List<SkillDefinitionV2> Skills { get; init; } = new();

    public List<DeckDefinition> Decks { get; init; } = new();

    public List<BossDefinition> Bosses { get; init; } = new();
}

public sealed record CardDefinition(
    string CardId,
    string DisplayName,
    string Description,
    CardCategory Category,
    string EffectId,
    EquipmentSlot? EquipmentSlot = null,
    int AttackRange = 0,
    bool CanRecast = false,
    IReadOnlyDictionary<string, double>? AiValues = null);

public sealed record DeckCardEntry(string CardId, CardSuit Suit, int Rank, int Copies = 1);

public sealed record DeckDefinition(string DeckId, string DisplayName, IReadOnlyList<DeckCardEntry> Cards);

public sealed record GeneralDefinitionV2(
    string GeneralId,
    string DisplayName,
    string Title,
    int MaximumHealth,
    string Gender,
    string Faction,
    string Artwork,
    IReadOnlyList<string> SkillIds,
    string Summary = "");

public sealed record BossDefinition(
    string BossId,
    string GeneralId,
    int BaseHealth,
    int HealthPerHero,
    double PhaseTwoHealthRatio,
    IReadOnlyList<string> PhaseTwoSkillIds);

public sealed class LoadedContentPack
{
    public required string RootPath { get; init; }

    public required ContentPackManifest Manifest { get; init; }

    public required ContentPackData Data { get; init; }

    public required string ComputedHash { get; init; }

    public ContentPackReference Reference => new(Manifest.PackId, Manifest.Version, ComputedHash);
}

public sealed record ContentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ContentValidationResult Success { get; } = new(true, Array.Empty<string>());
}

public interface IContentPack
{
    ContentPackReference Reference { get; }

    ContentPackManifest Manifest { get; }

    ContentPackData Data { get; }
}

public static class ContentPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static LoadedContentPack LoadDirectory(string rootPath, bool requireDeclaredHash = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string fullRoot = Path.GetFullPath(rootPath);
        string manifestPath = Path.Combine(fullRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException($"Content pack manifest is missing: {manifestPath}");
        }

        ContentPackManifest manifest = JsonSerializer.Deserialize<ContentPackManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException($"Content pack manifest is invalid: {manifestPath}");
        ValidateSafeRelativePaths(manifest.ContentFiles);

        ContentPackData merged = new();
        foreach (string relativeFile in manifest.ContentFiles.OrderBy(value => value, StringComparer.Ordinal))
        {
            string contentPath = Path.GetFullPath(Path.Combine(fullRoot, relativeFile));
            if (!contentPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(contentPath))
            {
                throw new InvalidDataException($"Content file is missing or outside the pack: {relativeFile}");
            }

            ContentPackData part = JsonSerializer.Deserialize<ContentPackData>(File.ReadAllText(contentPath), JsonOptions)
                ?? throw new InvalidDataException($"Content file is invalid: {relativeFile}");
            merged.Cards.AddRange(part.Cards);
            merged.Generals.AddRange(part.Generals);
            merged.Skills.AddRange(part.Skills);
            merged.Decks.AddRange(part.Decks);
            merged.Bosses.AddRange(part.Bosses);
        }

        string computedHash = ComputeContentHash(fullRoot, manifest.ContentFiles);
        if (requireDeclaredHash
            && (string.IsNullOrWhiteSpace(manifest.ContentHash)
                || !string.Equals(manifest.ContentHash, computedHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Content hash mismatch for pack {manifest.PackId}. Expected '{manifest.ContentHash}', actual '{computedHash}'.");
        }

        return new LoadedContentPack
        {
            RootPath = fullRoot,
            Manifest = manifest,
            Data = merged,
            ComputedHash = computedHash
        };
    }

    public static string ComputeContentHash(string rootPath, IEnumerable<string> relativeFiles)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string relativeFile in relativeFiles.OrderBy(value => value, StringComparer.Ordinal))
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(relativeFile.Replace('\\', '/'));
            hash.AppendData(nameBytes);
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(File.ReadAllBytes(Path.Combine(rootPath, relativeFile)));
            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ValidateSafeRelativePaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Path.IsPathRooted(path)
                || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == ".."))
            {
                throw new InvalidDataException($"Unsafe content path: {path}");
            }
        }
    }
}

public sealed class ContentRegistry
{
    private readonly Dictionary<string, LoadedContentPack> _packs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CardDefinition> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GeneralDefinitionV2> _generals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillDefinitionV2> _skills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeckDefinition> _decks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BossDefinition> _bosses = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LoadedContentPack> Packs => _packs;

    public IReadOnlyDictionary<string, CardDefinition> Cards => _cards;

    public IReadOnlyDictionary<string, GeneralDefinitionV2> Generals => _generals;

    public IReadOnlyDictionary<string, SkillDefinitionV2> Skills => _skills;

    public IReadOnlyDictionary<string, DeckDefinition> Decks => _decks;

    public IReadOnlyDictionary<string, BossDefinition> Bosses => _bosses;

    public ContentValidationResult Register(IEnumerable<LoadedContentPack> packs, EffectRegistry effects)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(effects);
        List<LoadedContentPack> incoming = packs.OrderBy(pack => pack.Manifest.PackId, StringComparer.Ordinal).ToList();
        List<string> errors = Validate(incoming, effects);
        if (errors.Count > 0)
        {
            return new ContentValidationResult(false, errors);
        }

        foreach (LoadedContentPack pack in incoming)
        {
            _packs.Add(pack.Manifest.PackId, pack);
            AddUnique(pack.Data.Cards, card => card.CardId, _cards, "card");
            AddUnique(pack.Data.Generals, general => general.GeneralId, _generals, "general");
            AddUnique(pack.Data.Skills, skill => skill.SkillId, _skills, "skill");
            AddUnique(pack.Data.Decks, deck => deck.DeckId, _decks, "deck");
            AddUnique(pack.Data.Bosses, boss => boss.BossId, _bosses, "boss");
        }

        return ContentValidationResult.Success;
    }

    private List<string> Validate(IReadOnlyList<LoadedContentPack> incoming, EffectRegistry effects)
    {
        List<string> errors = new();
        Dictionary<string, LoadedContentPack> combined = _packs.Values
            .Concat(incoming)
            .GroupBy(pack => pack.Manifest.PackId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (IGrouping<string, LoadedContentPack> duplicate in incoming.GroupBy(pack => pack.Manifest.PackId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate content pack: {duplicate.Key}");
        }

        foreach (LoadedContentPack pack in incoming)
        {
            if (_packs.ContainsKey(pack.Manifest.PackId))
            {
                errors.Add($"Content pack is already registered: {pack.Manifest.PackId}");
            }

            if (!string.Equals(pack.Manifest.License, "GPL-3.0-only", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Pack {pack.Manifest.PackId} must declare GPL-3.0-only.");
            }

            if (!string.Equals(pack.Manifest.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
            {
                errors.Add($"Pack {pack.Manifest.PackId} targets engine API {pack.Manifest.EngineApiVersion}, expected {ProtocolV2.EngineApiVersion}.");
            }

            foreach (ContentPackDependency dependency in pack.Manifest.Dependencies)
            {
                if (!combined.TryGetValue(dependency.PackId, out LoadedContentPack? dependencyPack))
                {
                    errors.Add($"Pack {pack.Manifest.PackId} is missing dependency {dependency.PackId}.");
                    continue;
                }

                if (!Version.TryParse(dependencyPack.Manifest.Version, out Version? actual)
                    || !Version.TryParse(dependency.MinimumVersion, out Version? minimum)
                    || actual < minimum)
                {
                    errors.Add($"Pack {pack.Manifest.PackId} requires {dependency.PackId} >= {dependency.MinimumVersion}.");
                }
            }

            foreach (CardDefinition card in pack.Data.Cards.Where(card => !string.IsNullOrWhiteSpace(card.EffectId)))
            {
                if (!effects.HasCardEffect(card.EffectId))
                {
                    errors.Add($"Card {card.CardId} references unknown effect {card.EffectId}.");
                }
            }

            foreach (SkillDefinitionV2 skill in pack.Data.Skills.Where(skill => !string.IsNullOrWhiteSpace(skill.EffectId)))
            {
                if (!effects.HasSkillEffect(skill.EffectId))
                {
                    errors.Add($"Skill {skill.SkillId} references unknown effect {skill.EffectId}.");
                }
            }
        }

        AddDuplicateErrors(incoming.SelectMany(pack => pack.Data.Cards).Select(card => card.CardId), "card", errors, _cards.Keys);
        AddDuplicateErrors(incoming.SelectMany(pack => pack.Data.Generals).Select(general => general.GeneralId), "general", errors, _generals.Keys);
        AddDuplicateErrors(incoming.SelectMany(pack => pack.Data.Skills).Select(skill => skill.SkillId), "skill", errors, _skills.Keys);
        AddDuplicateErrors(incoming.SelectMany(pack => pack.Data.Decks).Select(deck => deck.DeckId), "deck", errors, _decks.Keys);
        AddDuplicateErrors(incoming.SelectMany(pack => pack.Data.Bosses).Select(boss => boss.BossId), "boss", errors, _bosses.Keys);

        Dictionary<string, CardDefinition> allCards = _cards.Values
            .Concat(incoming.SelectMany(pack => pack.Data.Cards))
            .GroupBy(card => card.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, SkillDefinitionV2> allSkills = _skills.Values
            .Concat(incoming.SelectMany(pack => pack.Data.Skills))
            .GroupBy(skill => skill.SkillId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, GeneralDefinitionV2> allGenerals = _generals.Values
            .Concat(incoming.SelectMany(pack => pack.Data.Generals))
            .GroupBy(general => general.GeneralId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (GeneralDefinitionV2 general in allGenerals.Values)
        {
            foreach (string skillId in general.SkillIds.Where(skillId => !allSkills.ContainsKey(skillId)))
            {
                errors.Add($"General {general.GeneralId} references unknown skill {skillId}.");
            }
        }
        foreach (SkillDefinitionV2 skill in allSkills.Values)
        {
            foreach (string subSkillId in (skill.SubSkillIds ?? Array.Empty<string>()).Where(subSkillId => !allSkills.ContainsKey(subSkillId)))
            {
                errors.Add($"Skill {skill.SkillId} references unknown sub-skill {subSkillId}.");
            }
            foreach (SkillMarkDefinition mark in skill.Marks ?? Array.Empty<SkillMarkDefinition>())
            {
                if (mark.MaximumValue < 0 || mark.InitialValue < 0 || mark.InitialValue > mark.MaximumValue)
                {
                    errors.Add($"Skill {skill.SkillId} has invalid mark bounds for {mark.MarkId}.");
                }
            }
        }
        foreach (DeckDefinition deck in _decks.Values.Concat(incoming.SelectMany(pack => pack.Data.Decks)))
        {
            foreach (string cardId in deck.Cards.Select(card => card.CardId).Where(cardId => !allCards.ContainsKey(cardId)).Distinct(StringComparer.Ordinal))
            {
                errors.Add($"Deck {deck.DeckId} references unknown card {cardId}.");
            }
        }
        foreach (BossDefinition boss in _bosses.Values.Concat(incoming.SelectMany(pack => pack.Data.Bosses)))
        {
            if (!allGenerals.ContainsKey(boss.GeneralId))
            {
                errors.Add($"Boss {boss.BossId} references unknown general {boss.GeneralId}.");
            }
            foreach (string skillId in boss.PhaseTwoSkillIds.Where(skillId => !allSkills.ContainsKey(skillId)))
            {
                errors.Add($"Boss {boss.BossId} references unknown phase-two skill {skillId}.");
            }
        }
        return errors;
    }

    private static void AddDuplicateErrors(IEnumerable<string> values, string type, ICollection<string> errors, IEnumerable<string> existing)
    {
        HashSet<string> existingSet = new(existing, StringComparer.Ordinal);
        foreach (string duplicate in values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"Duplicate {type}: {duplicate}");
        }

        foreach (string collision in values.Where(existingSet.Contains).Distinct(StringComparer.Ordinal))
        {
            errors.Add($"Existing {type} would be replaced: {collision}");
        }
    }

    private static void AddUnique<T>(IEnumerable<T> values, Func<T, string> keySelector, IDictionary<string, T> destination, string type)
    {
        foreach (T value in values)
        {
            string key = keySelector(value);
            if (!destination.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Duplicate {type} escaped validation: {key}");
            }
        }
    }
}
