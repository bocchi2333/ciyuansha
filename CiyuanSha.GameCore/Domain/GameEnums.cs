namespace CiyuanSha.GameCore.Domain;

public enum MatchStatus
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Faulted = 3
}

public enum GamePhase
{
    NotStarted = 0,
    Preparation = 1,
    Judgement = 2,
    Draw = 3,
    Play = 4,
    Discard = 5,
    End = 6,
    Finished = 7
}

public enum PhaseDirectiveKind
{
    Skip = 0,
    Replace = 1,
    Extra = 2
}

/// <summary>
/// Deterministic phase mutation. Extra inserts <see cref="ReplacementPhase"/>
/// immediately before <see cref="Phase"/> and resumes the original phase after
/// the inserted phase completes.
/// </summary>
public sealed record PhaseDirective(
    long Sequence,
    int SeatId,
    PhaseDirectiveKind Kind,
    GamePhase Phase,
    GamePhase ReplacementPhase = GamePhase.NotStarted,
    string SourceId = "");

public enum CardZone
{
    None = 0,
    DrawPile = 1,
    Hand = 2,
    Equipment = 3,
    Judgement = 4,
    Processing = 5,
    DiscardPile = 6,
    Removed = 7
}

public enum CardSuit
{
    None = 0,
    Spade = 1,
    Heart = 2,
    Club = 3,
    Diamond = 4
}

public enum CardCategory
{
    Basic = 0,
    Trick = 1,
    DelayedTrick = 2,
    Equipment = 3
}

public enum EquipmentSlot
{
    Weapon = 0,
    Armor = 1,
    OffensiveMount = 2,
    DefensiveMount = 3,
    Treasure = 4
}

public enum DamageNature
{
    Physical = 0,
    Fire = 1,
    Thunder = 2
}

public enum SeatController
{
    Human = 0,
    Bot = 1,
    Offline = 2
}

public enum IdentityRole
{
    None = 0,
    Lord = 1,
    Loyalist = 2,
    Rebel = 3,
    Renegade = 4,
    Duelist = 5,
    TeamA = 6,
    TeamB = 7,
    Hero = 8,
    Boss = 9
}

public enum TeamSide
{
    None = 0,
    A = 1,
    B = 2,
    Heroes = 3,
    Boss = 4
}

public enum ViewerRole
{
    Player = 0,
    Spectator = 1,
    OmniscientReplay = 2
}

public enum BotDifficulty
{
    Easy = 0,
    Standard = 1,
    Hard = 2
}
