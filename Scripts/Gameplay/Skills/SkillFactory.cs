namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Creates skill instances from skill ids.
/// </summary>
public static class SkillFactory
{
    public static CharacterSkill? Create(string skillId)
    {
        return SkillRegistry.Create(skillId);
    }
}
