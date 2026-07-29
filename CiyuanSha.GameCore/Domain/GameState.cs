using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;

namespace CiyuanSha.GameCore.Domain;

public sealed class GameState
{
    public string MatchId { get; init; } = string.Empty;

    public long Revision { get; set; }

    public ulong Seed { get; init; }

    public ulong RandomState { get; set; }

    public string ModeId { get; init; } = string.Empty;

    public MatchStatus Status { get; set; } = MatchStatus.NotStarted;

    public GamePhase Phase { get; set; } = GamePhase.NotStarted;

    public int RoundNumber { get; set; }

    public int CurrentSeatIndex { get; set; }

    public long NextPhaseDirectiveSequence { get; set; }

    public GamePhase ExtraPhaseResume { get; set; } = GamePhase.NotStarted;

    public List<PhaseDirective> PhaseDirectives { get; } = new();

    public List<int> TurnOrder { get; } = new();

    public SortedDictionary<int, PlayerState> Players { get; } = new();

    public SortedDictionary<string, CardState> Cards { get; } = new(StringComparer.Ordinal);

    public List<string> DrawPile { get; } = new();

    public List<string> ProcessingArea { get; } = new();

    public List<string> DiscardPile { get; } = new();

    public List<int> WinnerSeatIds { get; } = new();

    public string ResultMessage { get; set; } = string.Empty;

    public ChoiceRequest? PendingChoice { get; set; }

    public List<ContentPackReference> ContentPacks { get; } = new();

    public int CurrentSeatId => TurnOrder.Count == 0
        ? 0
        : TurnOrder[Math.Clamp(CurrentSeatIndex, 0, TurnOrder.Count - 1)];

    public string ComputeCanonicalHash()
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("matchId", MatchId);
            writer.WriteNumber("revision", Revision);
            writer.WriteNumber("seed", Seed);
            writer.WriteNumber("randomState", RandomState);
            writer.WriteString("mode", ModeId);
            writer.WriteNumber("status", (int)Status);
            writer.WriteNumber("phase", (int)Phase);
            writer.WriteNumber("round", RoundNumber);
            writer.WriteNumber("currentSeatIndex", CurrentSeatIndex);
            writer.WriteNumber("nextPhaseDirectiveSequence", NextPhaseDirectiveSequence);
            writer.WriteNumber("extraPhaseResume", (int)ExtraPhaseResume);

            writer.WriteStartArray("phaseDirectives");
            foreach (PhaseDirective directive in PhaseDirectives.OrderBy(value => value.Sequence))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", directive.Sequence);
                writer.WriteNumber("seat", directive.SeatId);
                writer.WriteNumber("kind", (int)directive.Kind);
                writer.WriteNumber("phase", (int)directive.Phase);
                writer.WriteNumber("replacement", (int)directive.ReplacementPhase);
                writer.WriteString("source", directive.SourceId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("turnOrder");
            foreach (int seat in TurnOrder)
            {
                writer.WriteNumberValue(seat);
            }
            writer.WriteEndArray();

            writer.WriteStartArray("players");
            foreach ((int seatId, PlayerState player) in Players)
            {
                writer.WriteStartObject();
                writer.WriteNumber("seat", seatId);
                writer.WriteString("player", player.PlayerId);
                writer.WriteString("name", player.DisplayName);
                writer.WriteString("general", player.GeneralId);
                writer.WriteNumber("role", (int)player.Role);
                writer.WriteNumber("team", (int)player.Team);
                writer.WriteNumber("controller", (int)player.Controller);
                writer.WriteNumber("health", player.Health);
                writer.WriteNumber("maxHealth", player.MaxHealth);
                writer.WriteBoolean("alive", player.IsAlive);
                writer.WriteBoolean("chained", player.IsChained);
                writer.WriteNumber("slashUses", player.SlashUsesThisTurn);
                writer.WriteNumber("wineUses", player.WineUsesThisTurn);

                WriteStringArray(writer, "hand", player.Hand);
                writer.WriteStartObject("equipment");
                foreach ((EquipmentSlot slot, string cardId) in player.Equipment.OrderBy(pair => pair.Key))
                {
                    writer.WriteString(((int)slot).ToString(), cardId);
                }
                writer.WriteEndObject();
                WriteStringArray(writer, "judgement", player.JudgementArea);
                WriteStringArray(writer, "skills", player.SkillIds.OrderBy(value => value, StringComparer.Ordinal));
                WriteStringArray(writer, "temporarySkills", player.TemporarySkillIds.OrderBy(value => value, StringComparer.Ordinal));
                WriteStringArray(writer, "tempFlags", player.TemporaryFlags.OrderBy(value => value, StringComparer.Ordinal));

                writer.WriteStartObject("marks");
                foreach ((string mark, int value) in player.Marks)
                {
                    writer.WriteNumber(mark, value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("cards");
            foreach ((string instanceId, CardState card) in Cards)
            {
                writer.WriteStartObject();
                writer.WriteString("id", instanceId);
                writer.WriteString("definition", card.DefinitionId);
                writer.WriteNumber("suit", (int)card.Suit);
                writer.WriteNumber("rank", card.Rank);
                writer.WriteNumber("zone", (int)card.Zone);
                writer.WriteNumber("owner", card.OwnerSeatId);
                writer.WriteBoolean("faceUp", card.IsFaceUp);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            WriteStringArray(writer, "drawPile", DrawPile);
            WriteStringArray(writer, "processing", ProcessingArea);
            WriteStringArray(writer, "discardPile", DiscardPile);

            writer.WriteStartArray("winners");
            foreach (int winner in WinnerSeatIds.Order())
            {
                writer.WriteNumberValue(winner);
            }
            writer.WriteEndArray();
            writer.WriteString("result", ResultMessage);
            writer.WriteString("pendingChoice", PendingChoice?.RequestId ?? string.Empty);

            writer.WriteStartArray("packs");
            foreach (ContentPackReference pack in ContentPacks.OrderBy(pack => pack.PackId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", pack.PackId);
                writer.WriteString("version", pack.Version);
                writer.WriteString("hash", pack.ContentHash);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}

public sealed class PlayerState
{
    public required int SeatId { get; init; }

    public required string PlayerId { get; init; }

    public required string DisplayName { get; init; }

    public required string GeneralId { get; set; }

    public SeatController Controller { get; set; }

    public IdentityRole Role { get; set; }

    public TeamSide Team { get; set; }

    public int Health { get; set; }

    public int MaxHealth { get; set; }

    public bool IsAlive { get; set; } = true;

    public bool IsChained { get; set; }

    public int SlashUsesThisTurn { get; set; }

    public int WineUsesThisTurn { get; set; }

    public List<string> Hand { get; } = new();

    public SortedDictionary<EquipmentSlot, string> Equipment { get; } = new();

    public List<string> JudgementArea { get; } = new();

    public SortedSet<string> SkillIds { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> TemporarySkillIds { get; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, int> Marks { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> TemporaryFlags { get; } = new(StringComparer.Ordinal);
}

public sealed class CardState
{
    public required string InstanceId { get; init; }

    public required string DefinitionId { get; init; }

    public CardSuit Suit { get; init; }

    public int Rank { get; init; }

    public CardZone Zone { get; set; }

    public int OwnerSeatId { get; set; }

    public bool IsFaceUp { get; set; }
}
