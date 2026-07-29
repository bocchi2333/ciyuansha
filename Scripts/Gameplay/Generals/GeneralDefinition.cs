using System.Collections.Generic;
using CiyuanSha.Gameplay.Characters;

namespace CiyuanSha.Gameplay.Generals;

/// <summary>
/// Static general configuration used to initialize characters.
/// </summary>
public class GeneralDefinition
{
    public string GeneralId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public PlayerGender Gender { get; set; } = PlayerGender.Unknown;

    public int MaxHealth { get; set; } = 4;

    public string CardFileName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> SkillIds { get; set; } = new();
}
