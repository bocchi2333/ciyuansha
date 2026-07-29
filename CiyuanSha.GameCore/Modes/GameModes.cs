using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Determinism;
using CiyuanSha.GameCore.Domain;

namespace CiyuanSha.GameCore.Modes;

public static class BuiltInModeIds
{
    public const string Duel = "duel";
    public const string Identity = "identity";
    public const string TeamTwoVersusTwo = "team_2v2";
    public const string Boss = "boss_pve";
}

public sealed record PlayerConfig(
    int SeatId,
    string PlayerId,
    string DisplayName,
    string GeneralId,
    SeatController Controller = SeatController.Human,
    bool IsBoss = false,
    BotDifficulty BotDifficulty = BotDifficulty.Standard);

public sealed record MatchConfig(
    string MatchId,
    string ModeId,
    ulong Seed,
    IReadOnlyList<PlayerConfig> Players,
    string DeckId,
    IReadOnlyList<ContentPackReference> ContentPacks,
    int StartingSeatId = 0,
    string BossDefinitionId = "");

public sealed record ModeValidationResult(bool IsValid, string ErrorKey)
{
    public static ModeValidationResult Valid { get; } = new(true, string.Empty);

    public static ModeValidationResult Invalid(string errorKey) => new(false, errorKey);
}

public sealed record VictoryResult(IReadOnlyList<int> WinnerSeatIds, string ResultKey);

public interface IGameMode
{
    string ModeId { get; }

    int MinimumPlayers { get; }

    int MaximumPlayers { get; }

    ModeValidationResult Validate(MatchConfig config, ContentRegistry content);

    void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random);

    VictoryResult? CheckVictory(GameState state);

    double GetAttitude(GameState state, int observerSeatId, int targetSeatId);

    bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer);
}

public abstract class GameModeBase : IGameMode
{
    public abstract string ModeId { get; }

    public abstract int MinimumPlayers { get; }

    public abstract int MaximumPlayers { get; }

    public virtual ModeValidationResult Validate(MatchConfig config, ContentRegistry content)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(content);
        if (config.Players.Count < MinimumPlayers || config.Players.Count > MaximumPlayers)
        {
            return ModeValidationResult.Invalid("mode.invalid_player_count");
        }

        if (config.Players.Select(player => player.SeatId).Distinct().Count() != config.Players.Count)
        {
            return ModeValidationResult.Invalid("mode.duplicate_seat");
        }

        if (config.Players.Any(player => !content.Generals.ContainsKey(player.GeneralId)))
        {
            return ModeValidationResult.Invalid("mode.unknown_general");
        }

        if (!content.Decks.ContainsKey(config.DeckId))
        {
            return ModeValidationResult.Invalid("mode.unknown_deck");
        }

        return ModeValidationResult.Valid;
    }

    public abstract void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random);

    public abstract VictoryResult? CheckVictory(GameState state);

    public abstract double GetAttitude(GameState state, int observerSeatId, int targetSeatId);

    public virtual bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer) =>
        viewer.Role == ViewerRole.OmniscientReplay || viewer.SeatId == subjectSeatId;

    protected static IEnumerable<PlayerState> Alive(GameState state) => state.Players.Values.Where(player => player.IsAlive);
}

public sealed class DuelMode : GameModeBase
{
    public override string ModeId => BuiltInModeIds.Duel;

    public override int MinimumPlayers => 2;

    public override int MaximumPlayers => 2;

    public override void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random)
    {
        foreach ((PlayerState player, int index) in state.Players.Values.OrderBy(player => player.SeatId).Select((player, index) => (player, index)))
        {
            player.Role = IdentityRole.Duelist;
            player.Team = index == 0 ? TeamSide.A : TeamSide.B;
        }
    }

    public override VictoryResult? CheckVictory(GameState state)
    {
        List<int> alive = Alive(state).Select(player => player.SeatId).ToList();
        return alive.Count <= 1 ? new VictoryResult(alive, "victory.duel") : null;
    }

    public override double GetAttitude(GameState state, int observerSeatId, int targetSeatId) =>
        observerSeatId == targetSeatId ? 1 : -1;

    public override bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer) => true;
}

public sealed class IdentityMode : GameModeBase
{
    public override string ModeId => BuiltInModeIds.Identity;

    public override int MinimumPlayers => 4;

    public override int MaximumPlayers => 4;

    public override void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random)
    {
        List<PlayerState> ordered = state.Players.Values.OrderBy(player => player.SeatId).ToList();
        int lordIndex = config.StartingSeatId == 0
            ? random.NextInt(ordered.Count)
            : ordered.FindIndex(player => player.SeatId == config.StartingSeatId);
        if (lordIndex < 0)
        {
            lordIndex = 0;
        }

        PlayerState lord = ordered[lordIndex];
        lord.Role = IdentityRole.Lord;
        lord.Team = TeamSide.A;

        List<IdentityRole> hiddenRoles = new() { IdentityRole.Loyalist, IdentityRole.Rebel, IdentityRole.Renegade };
        random.Shuffle(hiddenRoles);
        int hiddenIndex = 0;
        foreach (PlayerState player in ordered.Where(player => player.SeatId != lord.SeatId))
        {
            player.Role = hiddenRoles[hiddenIndex++];
            player.Team = player.Role switch
            {
                IdentityRole.Lord or IdentityRole.Loyalist => TeamSide.A,
                IdentityRole.Rebel => TeamSide.B,
                _ => TeamSide.None
            };
        }

        int lordTurnIndex = state.TurnOrder.IndexOf(lord.SeatId);
        state.CurrentSeatIndex = lordTurnIndex < 0 ? 0 : lordTurnIndex;
    }

    public override VictoryResult? CheckVictory(GameState state)
    {
        PlayerState lord = state.Players.Values.Single(player => player.Role == IdentityRole.Lord);
        List<PlayerState> alive = Alive(state).ToList();
        if (!lord.IsAlive)
        {
            PlayerState? soleRenegade = alive.Count == 1 && alive[0].Role == IdentityRole.Renegade ? alive[0] : null;
            if (soleRenegade is not null)
            {
                return new VictoryResult(new[] { soleRenegade.SeatId }, "victory.renegade");
            }

            return new VictoryResult(
                state.Players.Values.Where(player => player.Role == IdentityRole.Rebel).Select(player => player.SeatId).ToArray(),
                "victory.rebels");
        }

        if (alive.All(player => player.Role is IdentityRole.Lord or IdentityRole.Loyalist))
        {
            return new VictoryResult(
                state.Players.Values.Where(player => player.Role is IdentityRole.Lord or IdentityRole.Loyalist).Select(player => player.SeatId).ToArray(),
                "victory.lord");
        }

        return null;
    }

    public override double GetAttitude(GameState state, int observerSeatId, int targetSeatId)
    {
        if (observerSeatId == targetSeatId)
        {
            return 1;
        }

        PlayerState observer = state.Players[observerSeatId];
        PlayerState target = state.Players[targetSeatId];
        return observer.Role switch
        {
            IdentityRole.Lord or IdentityRole.Loyalist => target.Role is IdentityRole.Lord or IdentityRole.Loyalist ? 1 : -1,
            IdentityRole.Rebel => target.Role == IdentityRole.Rebel ? 1 : -1,
            IdentityRole.Renegade => target.Role == IdentityRole.Renegade ? 1 : 0,
            _ => 0
        };
    }

    public override bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer) =>
        state.Players[subjectSeatId].Role == IdentityRole.Lord
        || viewer.Role == ViewerRole.OmniscientReplay
        || viewer.SeatId == subjectSeatId
        || state.Status == MatchStatus.Completed;
}

public sealed class TeamTwoVersusTwoMode : GameModeBase
{
    public override string ModeId => BuiltInModeIds.TeamTwoVersusTwo;

    public override int MinimumPlayers => 4;

    public override int MaximumPlayers => 4;

    public override void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random)
    {
        foreach ((PlayerState player, int index) in state.Players.Values.OrderBy(player => player.SeatId).Select((player, index) => (player, index)))
        {
            bool teamA = index % 2 == 0;
            player.Role = teamA ? IdentityRole.TeamA : IdentityRole.TeamB;
            player.Team = teamA ? TeamSide.A : TeamSide.B;
        }
    }

    public override VictoryResult? CheckVictory(GameState state)
    {
        HashSet<TeamSide> aliveTeams = Alive(state).Select(player => player.Team).ToHashSet();
        if (aliveTeams.Count != 1)
        {
            return null;
        }

        TeamSide winner = aliveTeams.Single();
        return new VictoryResult(state.Players.Values.Where(player => player.Team == winner).Select(player => player.SeatId).ToArray(), "victory.team");
    }

    public override double GetAttitude(GameState state, int observerSeatId, int targetSeatId) =>
        state.Players[observerSeatId].Team == state.Players[targetSeatId].Team ? 1 : -1;

    public override bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer) => true;
}

public sealed class BossMode : GameModeBase
{
    public override string ModeId => BuiltInModeIds.Boss;

    public override int MinimumPlayers => 2;

    public override int MaximumPlayers => 4;

    public override ModeValidationResult Validate(MatchConfig config, ContentRegistry content)
    {
        ModeValidationResult baseResult = base.Validate(config, content);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        if (config.Players.Count(player => player.IsBoss) != 1)
        {
            return ModeValidationResult.Invalid("mode.boss_requires_one_boss");
        }

        PlayerConfig boss = config.Players.Single(player => player.IsBoss);
        if (boss.Controller != SeatController.Bot)
        {
            return ModeValidationResult.Invalid("mode.boss_must_be_bot");
        }

        if (string.IsNullOrWhiteSpace(config.BossDefinitionId) || !content.Bosses.ContainsKey(config.BossDefinitionId))
        {
            return ModeValidationResult.Invalid("mode.unknown_boss_definition");
        }

        return ModeValidationResult.Valid;
    }

    public override void Setup(GameState state, MatchConfig config, ContentRegistry content, DeterministicRandom random)
    {
        BossDefinition definition = content.Bosses[config.BossDefinitionId];
        PlayerConfig bossConfig = config.Players.Single(player => player.IsBoss);
        PlayerState boss = state.Players[bossConfig.SeatId];
        boss.Role = IdentityRole.Boss;
        boss.Team = TeamSide.Boss;
        boss.MaxHealth = definition.BaseHealth + (config.Players.Count - 1) * definition.HealthPerHero;
        boss.Health = boss.MaxHealth;
        boss.Marks["boss_phase"] = 1;

        foreach (PlayerState hero in state.Players.Values.Where(player => player.SeatId != boss.SeatId))
        {
            hero.Role = IdentityRole.Hero;
            hero.Team = TeamSide.Heroes;
        }

        int bossIndex = state.TurnOrder.IndexOf(boss.SeatId);
        state.CurrentSeatIndex = bossIndex < 0 ? 0 : bossIndex;
    }

    public override VictoryResult? CheckVictory(GameState state)
    {
        PlayerState boss = state.Players.Values.Single(player => player.Role == IdentityRole.Boss);
        if (!boss.IsAlive)
        {
            return new VictoryResult(state.Players.Values.Where(player => player.Role == IdentityRole.Hero).Select(player => player.SeatId).ToArray(), "victory.heroes");
        }

        if (!state.Players.Values.Any(player => player.Role == IdentityRole.Hero && player.IsAlive))
        {
            return new VictoryResult(new[] { boss.SeatId }, "victory.boss");
        }

        return null;
    }

    public override double GetAttitude(GameState state, int observerSeatId, int targetSeatId) =>
        state.Players[observerSeatId].Team == state.Players[targetSeatId].Team ? 1 : -1;

    public override bool IsRoleVisibleTo(GameState state, int subjectSeatId, ViewerContext viewer) => true;
}

public sealed class GameModeRegistry
{
    private readonly Dictionary<string, IGameMode> _modes = new(StringComparer.Ordinal);

    public GameModeRegistry(bool registerBuiltIns = true)
    {
        if (registerBuiltIns)
        {
            Register(new DuelMode());
            Register(new IdentityMode());
            Register(new TeamTwoVersusTwoMode());
            Register(new BossMode());
        }
    }

    public IReadOnlyDictionary<string, IGameMode> Modes => _modes;

    public void Register(IGameMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (!_modes.TryAdd(mode.ModeId, mode))
        {
            throw new InvalidOperationException($"Mode is already registered: {mode.ModeId}");
        }
    }

    public IGameMode Get(string modeId) => _modes.TryGetValue(modeId, out IGameMode? mode)
        ? mode
        : throw new KeyNotFoundException($"Unknown mode: {modeId}");
}
