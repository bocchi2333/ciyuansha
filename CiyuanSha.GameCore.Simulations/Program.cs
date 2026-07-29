using System.Collections.Concurrent;
using System.Text.Json;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Simulation;
using CiyuanSha.GameCore.Skills;

int matchesPerCombination = ReadIntArgument(args, "--matches", 20);
int startIndex = ReadNonNegativeIntArgument(args, "--start-index", 0);
int maximumDecisions = ReadIntArgument(args, "--max-decisions", 50000);
int parallelism = ReadIntArgument(args, "--parallelism", Environment.ProcessorCount);
string modeFilter = ReadStringArgument(args, "--mode", string.Empty, makeFullPath: false);
string difficultyFilter = ReadStringArgument(args, "--difficulty", string.Empty, makeFullPath: false);
string packsRoot = ReadStringArgument(args, "--packs", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Data", "Packs")));
string outputPath = ReadStringArgument(args, "--output", string.Empty);
EffectRegistry effects = BuiltInEffects.CreateRegistry();
LoadedContentPack[] packs = Directory.GetDirectories(packsRoot)
    .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
    .OrderBy(directory => directory, StringComparer.Ordinal)
    .Select(directory => ContentPackLoader.LoadDirectory(directory))
    .ToArray();
ContentRegistry content = new();
ContentValidationResult validation = content.Register(packs, effects);
if (!validation.IsValid)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, validation.Errors));
    return 2;
}

string[] modes = { BuiltInModeIds.Duel, BuiltInModeIds.Identity, BuiltInModeIds.TeamTwoVersusTwo, BuiltInModeIds.Boss };
BotDifficulty[] difficulties = { BotDifficulty.Easy, BotDifficulty.Standard, BotDifficulty.Hard };
if (!string.IsNullOrWhiteSpace(modeFilter))
{
    modes = modes.Where(mode => string.Equals(mode, modeFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
}
if (!string.IsNullOrWhiteSpace(difficultyFilter))
{
    if (!Enum.TryParse(difficultyFilter, true, out BotDifficulty parsedDifficulty))
    {
        Console.Error.WriteLine($"Unknown difficulty: {difficultyFilter}");
        return 2;
    }
    difficulties = new[] { parsedDifficulty };
}
if (modes.Length == 0)
{
    Console.Error.WriteLine($"Unknown mode: {modeFilter}");
    return 2;
}
ConcurrentBag<SimulationResult> results = new();
ConcurrentDictionary<string, int> generalAppearances = new(StringComparer.Ordinal);
List<(string Mode, BotDifficulty Difficulty, int Index)> work = (from mode in modes
    from difficulty in difficulties
    from index in Enumerable.Range(startIndex, matchesPerCombination)
    select (mode, difficulty, index)).ToList();

int completedWorkItems = 0;
Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, item =>
{
    MatchConfig config = CreateConfig(content, item.Mode, item.Difficulty, item.Index);
    foreach (PlayerConfig player in config.Players)
    {
        generalAppearances.AddOrUpdate(player.GeneralId, 1, (_, count) => count + 1);
    }
    results.Add(new MatchSimulationRunner(content).Run(config, maximumDecisions));
    int completed = Interlocked.Increment(ref completedWorkItems);
    if (completed % 100 == 0 || completed == work.Count)
    {
        Console.Error.WriteLine($"progress {completed}/{work.Count}");
    }
});

SimulationResult[] ordered = results
    .OrderBy(result => result.ModeId, StringComparer.Ordinal)
    .ThenBy(result => result.Seed)
    .ToArray();
var report = new
{
    GeneratedUtc = DateTimeOffset.UtcNow,
    EngineApiVersion = ProtocolV2.EngineApiVersion,
    ProtocolVersion = ProtocolV2.Version,
    ContentPacks = content.Packs.Values
        .Select(pack => pack.Reference)
        .OrderBy(pack => pack.PackId, StringComparer.Ordinal)
        .ToArray(),
    ModeFilter = modeFilter,
    DifficultyFilter = difficultyFilter,
    MatchesPerModeDifficulty = matchesPerCombination,
    StartIndex = startIndex,
    MaximumDecisionsPerMatch = maximumDecisions,
    Parallelism = parallelism,
    Total = ordered.Length,
    Completed = ordered.Count(result => result.Status == MatchStatus.Completed),
    Faulted = ordered.Count(result => result.Status == MatchStatus.Faulted || result.ErrorKey.Length > 0),
    RejectedChoices = ordered.Sum(result => result.RejectedChoices),
    MaximumDecisions = ordered.Length == 0 ? 0 : ordered.Max(result => result.Decisions),
    DeterminismDigest = ComputeDigest(ordered),
    GeneralAppearances = generalAppearances.OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
    Matrix = ordered.GroupBy(result => new { result.ModeId, Difficulty = ParseDifficulty(result.MatchId) })
        .Select(group => new
        {
            group.Key.ModeId,
            group.Key.Difficulty,
            Matches = group.Count(),
            Completed = group.Count(result => result.Status == MatchStatus.Completed),
            Rejected = group.Sum(result => result.RejectedChoices),
            MaxDecisions = group.Max(result => result.Decisions)
        })
        .OrderBy(row => row.ModeId, StringComparer.Ordinal)
        .ThenBy(row => row.Difficulty)
        .ToArray(),
    Failures = ordered.Where(result => result.Status != MatchStatus.Completed || result.ErrorKey.Length > 0).ToArray()
};
string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (!string.IsNullOrWhiteSpace(outputPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
    File.WriteAllText(outputPath, reportJson + Environment.NewLine);
}
Console.WriteLine(reportJson);
return report.Faulted == 0 && report.RejectedChoices == 0 && report.Completed == report.Total ? 0 : 1;

static MatchConfig CreateConfig(ContentRegistry content, string mode, BotDifficulty difficulty, int index)
{
    int playerCount = mode is BuiltInModeIds.Identity or BuiltInModeIds.TeamTwoVersusTwo ? 4 : mode == BuiltInModeIds.Duel ? 2 : 3;
    string[] generalIds = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    List<PlayerConfig> players = new();
    for (int seat = 1; seat <= playerCount; seat++)
    {
        string generalId = generalIds[(index * playerCount + seat - 1) % generalIds.Length];
        bool isBoss = mode == BuiltInModeIds.Boss && seat == 1;
        if (isBoss)
        {
            generalId = "jifenxin";
        }
        players.Add(new PlayerConfig(seat, $"bot-{seat}", $"Bot {seat}", generalId, SeatController.Bot, isBoss, difficulty));
    }
    ulong seed = 0xC1A0_0000UL + (ulong)(Array.IndexOf(new[] { BuiltInModeIds.Duel, BuiltInModeIds.Identity, BuiltInModeIds.TeamTwoVersusTwo, BuiltInModeIds.Boss }, mode) * 1_000_000)
        + (ulong)((int)difficulty * 100_000) + (uint)index;
    return new MatchConfig(
        $"simulation-{mode}-{difficulty}-{index}",
        mode,
        seed,
        players,
        "standard_military_161",
        content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
        1,
        mode == BuiltInModeIds.Boss ? "boss_jifenxin" : string.Empty);
}

static string ComputeDigest(IEnumerable<SimulationResult> results)
{
    string joined = string.Join("\n", results.Select(result => $"{result.MatchId}:{result.Seed}:{result.FinalStateHash}:{result.Decisions}"));
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
}

static BotDifficulty ParseDifficulty(string matchId) => Enum.GetValues<BotDifficulty>()
    .First(difficulty => matchId.Contains($"-{difficulty}-", StringComparison.Ordinal));

static int ReadIntArgument(string[] values, string name, int fallback)
{
    int index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length && int.TryParse(values[index + 1], out int parsed) && parsed > 0 ? parsed : fallback;
}

static int ReadNonNegativeIntArgument(string[] values, string name, int fallback)
{
    int index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length && int.TryParse(values[index + 1], out int parsed) && parsed >= 0 ? parsed : fallback;
}

static string ReadStringArgument(string[] values, string name, string fallback, bool makeFullPath = true)
{
    int index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length)
    {
        return fallback;
    }
    return makeFullPath ? Path.GetFullPath(values[index + 1]) : values[index + 1];
}
