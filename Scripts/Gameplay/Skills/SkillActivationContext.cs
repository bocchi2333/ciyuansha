using System.Collections.Generic;
using CiyuanSha.Gameplay.Characters;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Runtime payload for active skill activation commands.
/// </summary>
public sealed class SkillActivationContext
{
    public SkillActivationContext(PlayerCharacter source, IReadOnlyList<PlayerCharacter> targets, string cardInstanceId = "")
    {
        Source = source;
        Targets = targets;
        CardInstanceId = cardInstanceId ?? string.Empty;
    }

    public PlayerCharacter Source { get; }

    public IReadOnlyList<PlayerCharacter> Targets { get; }

    public string CardInstanceId { get; }

    public bool IsCancelled { get; private set; }

    public string CancelReason { get; private set; } = string.Empty;

    public void Cancel(string reason)
    {
        IsCancelled = true;
        CancelReason = reason ?? string.Empty;
    }
}
