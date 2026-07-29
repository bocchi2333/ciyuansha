using System;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Static metadata and factory hook for a skill.
/// </summary>
public sealed record SkillDefinition(
    string SkillId,
    string DisplayName,
    string Description,
    SkillTriggerTiming TriggerTimings,
    SkillUsageScope UsageScope,
    SkillTriggerPriority TriggerPriority,
    bool IsActiveSkill,
    SkillTargetingMode TargetingMode,
    int MinTargetCount,
    int MaxTargetCount,
    bool RequiresHandCardCost,
    Func<CharacterSkill> Create);
