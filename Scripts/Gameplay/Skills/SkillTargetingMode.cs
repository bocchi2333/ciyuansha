namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Declares how an active skill expects targets.
/// </summary>
public enum SkillTargetingMode
{
    None = 0,
    Self = 1,
    OtherCharacter = 2,
    AnyCharacter = 3,
    MultipleCharacters = 4
}
