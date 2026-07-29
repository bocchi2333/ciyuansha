namespace CiyuanSha.Networking;

/// <summary>
/// Replicated public skill metadata and lightweight runtime usage state.
/// </summary>
public class NetworkSkillState
{
    public string SkillId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string UsageScope { get; set; } = "Unlimited";

    public string TriggerPriority { get; set; } = "Normal";

    public string TriggerTimings { get; set; } = "None";

    public bool IsActiveSkill { get; set; }

    public string TargetingMode { get; set; } = "None";

    public int MinTargetCount { get; set; }

    public int MaxTargetCount { get; set; }

    public bool RequiresHandCardCost { get; set; }

    public int UsesThisTurn { get; set; }
}
