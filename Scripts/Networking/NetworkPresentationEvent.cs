namespace CiyuanSha.Networking;

/// <summary>
/// A short-lived audiovisual cue replicated with authoritative match snapshots.
/// </summary>
public class NetworkPresentationEvent
{
    public int Sequence { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string DamageType { get; set; } = string.Empty;

    public string CardType { get; set; } = string.Empty;

    public string ResponseKind { get; set; } = string.Empty;

    public int Value { get; set; }

    public int SourcePeerId { get; set; }

    public int TargetPeerId { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
