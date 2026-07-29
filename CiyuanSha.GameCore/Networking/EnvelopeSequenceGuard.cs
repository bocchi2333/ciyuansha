namespace CiyuanSha.GameCore.Networking;

/// <summary>
/// Per-transport-session replay guard for client envelopes. Gaps are allowed,
/// but a sequence must be positive and strictly newer than the last accepted
/// envelope from that logical peer.
/// </summary>
public sealed class EnvelopeSequenceGuard
{
    private readonly Dictionary<int, long> _lastAccepted = new();

    public bool TryAccept(int logicalPeerId, long sequence, out string errorKey)
    {
        if (logicalPeerId <= 0 || sequence <= 0)
        {
            errorKey = "network.invalid_sequence";
            return false;
        }
        if (_lastAccepted.TryGetValue(logicalPeerId, out long previous) && sequence <= previous)
        {
            errorKey = "network.replayed_sequence";
            return false;
        }
        _lastAccepted[logicalPeerId] = sequence;
        errorKey = string.Empty;
        return true;
    }

    public void Reset(int logicalPeerId) => _lastAccepted.Remove(logicalPeerId);

    public void Clear() => _lastAccepted.Clear();
}
