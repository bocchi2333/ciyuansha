using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Networking;

namespace CiyuanSha.GameCore.Tests;

public sealed class ProtocolV2Tests
{
    [Fact]
    public void EnvelopeSequenceGuardRejectsDuplicateAndOutOfOrderPackets()
    {
        EnvelopeSequenceGuard guard = new();

        Assert.True(guard.TryAccept(7, 1, out string firstError));
        Assert.Equal(string.Empty, firstError);
        Assert.False(guard.TryAccept(7, 1, out string duplicateError));
        Assert.Equal("network.replayed_sequence", duplicateError);
        Assert.True(guard.TryAccept(7, 3, out _));
        Assert.False(guard.TryAccept(7, 2, out string staleError));
        Assert.Equal("network.replayed_sequence", staleError);

        Assert.True(guard.TryAccept(8, 1, out _));
        guard.Reset(7);
        Assert.True(guard.TryAccept(7, 1, out _));
        Assert.False(guard.TryAccept(0, 1, out string peerError));
        Assert.Equal("network.invalid_sequence", peerError);
        Assert.False(guard.TryAccept(7, 0, out string sequenceError));
        Assert.Equal("network.invalid_sequence", sequenceError);
    }

    private static readonly ContentPackReference[] Packs =
    {
        new("core-rules", "2.0.0", "aabbcc")
    };

    [Fact]
    public void Handshake_RequiresExactProtocolEngineAndPackSet()
    {
        Assert.True(Validate(ProtocolV2.Version, ProtocolV2.EngineApiVersion, Packs).IsValid);
        Assert.Equal("network.protocol_version_mismatch", Validate(1, ProtocolV2.EngineApiVersion, Packs).ErrorKey);
        Assert.Equal("network.engine_version_mismatch", Validate(ProtocolV2.Version, "1.0.0", Packs).ErrorKey);
        Assert.Equal(
            "network.content_pack_mismatch",
            Validate(ProtocolV2.Version, ProtocolV2.EngineApiVersion, new[] { Packs[0] with { ContentHash = "changed" } }).ErrorKey);
        Assert.Equal(
            "network.content_pack_mismatch",
            Validate(ProtocolV2.Version, ProtocolV2.EngineApiVersion, Array.Empty<ContentPackReference>()).ErrorKey);
    }

    [Fact]
    public void ReconnectToken_IsOpaqueUniqueAndComparedExactly()
    {
        string first = ReconnectTokenFactory.Create();
        string second = ReconnectTokenFactory.Create();

        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, second);
        Assert.True(ReconnectTokenFactory.FixedTimeEquals(first, first));
        Assert.False(ReconnectTokenFactory.FixedTimeEquals(first, second));
        Assert.False(ReconnectTokenFactory.FixedTimeEquals(first, first.ToUpperInvariant()));
    }

    private static ProtocolValidationResult Validate(
        int version,
        string engineVersion,
        IReadOnlyList<ContentPackReference> packs) => ProtocolV2Validator.ValidateHandshake(
        new HandshakeRequestV2(version, engineVersion, "player", string.Empty, false, packs),
        Packs);
}
