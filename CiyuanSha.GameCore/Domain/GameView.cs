using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;

namespace CiyuanSha.GameCore.Domain;

public sealed record ViewerContext(ViewerRole Role, int? SeatId = null)
{
    public static ViewerContext ForPlayer(int seatId) => new(ViewerRole.Player, seatId);

    public static ViewerContext Spectator { get; } = new(ViewerRole.Spectator);

    public static ViewerContext OmniscientReplay { get; } = new(ViewerRole.OmniscientReplay);
}

public sealed class GameView
{
    public required string MatchId { get; init; }

    public required long Revision { get; init; }

    public required string ModeId { get; init; }

    public required MatchStatus Status { get; init; }

    public required GamePhase Phase { get; init; }

    public required int CurrentSeatId { get; init; }

    public required int RoundNumber { get; init; }

    public required IReadOnlyList<PlayerView> Players { get; init; }

    public required IReadOnlyList<CardView> PublicCards { get; init; }

    public required IReadOnlyList<string> PrivateHandCardIds { get; init; }

    public required int DrawPileCount { get; init; }

    public required int DiscardPileCount { get; init; }

    public required ChoiceRequest? PendingChoice { get; init; }

    public required IReadOnlyList<ContentPackReference> ContentPacks { get; init; }

    public required IReadOnlyList<int> WinnerSeatIds { get; init; }

    public required string ResultMessage { get; init; }

    public required string StateHash { get; init; }
}

public sealed record PlayerView(
    int SeatId,
    string DisplayName,
    string GeneralId,
    IdentityRole Role,
    bool IsRoleVisible,
    TeamSide Team,
    SeatController Controller,
    int Health,
    int MaxHealth,
    bool IsAlive,
    bool IsChained,
    int HandCount,
    IReadOnlyDictionary<EquipmentSlot, string> Equipment,
    IReadOnlyList<string> JudgementArea,
    IReadOnlyDictionary<string, int> Marks,
    IReadOnlyList<string> SkillIds);

public sealed record CardView(
    string InstanceId,
    string DefinitionId,
    CardSuit Suit,
    int Rank,
    CardZone Zone,
    int OwnerSeatId);
