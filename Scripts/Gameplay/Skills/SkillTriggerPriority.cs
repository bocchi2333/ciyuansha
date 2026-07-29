namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Relative ordering for automatic skill hooks on the rule event bus.
/// Higher values resolve earlier.
/// </summary>
public enum SkillTriggerPriority
{
    Low = -100,
    Normal = 0,
    High = 100,
    Forced = 200
}
