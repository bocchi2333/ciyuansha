using System.Security.Cryptography;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Replay;

namespace CiyuanSha.GameCore.Networking;

public static class ProtocolV2
{
    public const int Version = 2;
    public const string EngineApiVersion = "2.0.0";
}

public enum NetworkMessageKindV2
{
    Handshake = 0,
    HandshakeResult = 1,
    LobbyCommand = 2,
    SubmitChoice = 3,
    MatchSnapshot = 4,
    JournalDelta = 5,
    RequestSync = 6,
    Error = 7
}

public sealed record NetworkEnvelopeV2(
    int ProtocolVersion,
    NetworkMessageKindV2 MessageKind,
    string MatchId,
    long ClientSequence,
    string PayloadJson);

public sealed record HandshakeRequestV2(
    int ProtocolVersion,
    string EngineApiVersion,
    string PlayerName,
    string ReconnectToken,
    bool JoinAsSpectator,
    IReadOnlyList<ContentPackReference> ContentPacks);

public sealed record HandshakeResultV2(
    bool Accepted,
    string ErrorKey,
    int AssignedSeatId,
    string ReconnectToken,
    long StateRevision);

public enum LobbyCommandKindV2
{
    SelectGeneral = 0,
    SetReady = 1,
    SetMode = 2,
    AddBot = 3,
    RemoveBot = 4,
    SetBotDifficulty = 5,
    TakeOverOfflineSeat = 6,
    StartMatch = 7,
    ReturnToLobby = 8
}

public sealed record LobbyCommandV2(
    LobbyCommandKindV2 Kind,
    string StringValue = "",
    int IntegerValue = 0,
    bool BooleanValue = false);

public sealed record SubmitChoiceCommandV2(
    int ActingSeatId,
    string ReconnectToken,
    ChoiceResult Result);

public sealed record MatchSnapshotV2(
    int ProtocolVersion,
    string EngineApiVersion,
    long JournalCursor,
    GameView View);

public sealed record JournalDeltaV2(
    long FromCursor,
    long ToCursor,
    IReadOnlyList<RuleJournalEntry> Entries,
    string StateHash);

public sealed record ProtocolValidationResult(bool IsValid, string ErrorKey)
{
    public static ProtocolValidationResult Valid { get; } = new(true, string.Empty);

    public static ProtocolValidationResult Invalid(string errorKey) => new(false, errorKey);
}

public static class ProtocolV2Validator
{
    public static ProtocolValidationResult ValidateHandshake(
        HandshakeRequestV2 request,
        IReadOnlyList<ContentPackReference> authoritativePacks)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion != ProtocolV2.Version)
        {
            return ProtocolValidationResult.Invalid("network.protocol_version_mismatch");
        }

        if (!string.Equals(request.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
        {
            return ProtocolValidationResult.Invalid("network.engine_version_mismatch");
        }

        Dictionary<string, ContentPackReference> client = request.ContentPacks.ToDictionary(pack => pack.PackId, StringComparer.Ordinal);
        foreach (ContentPackReference expected in authoritativePacks)
        {
            if (!client.TryGetValue(expected.PackId, out ContentPackReference? actual)
                || !string.Equals(expected.Version, actual.Version, StringComparison.Ordinal)
                || !string.Equals(expected.ContentHash, actual.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return ProtocolValidationResult.Invalid("network.content_pack_mismatch");
            }
        }

        if (client.Count != authoritativePacks.Count)
        {
            return ProtocolValidationResult.Invalid("network.content_pack_mismatch");
        }

        return ProtocolValidationResult.Valid;
    }
}

public static class ReconnectTokenFactory
{
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string expected, string supplied)
    {
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected ?? string.Empty);
        byte[] suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
