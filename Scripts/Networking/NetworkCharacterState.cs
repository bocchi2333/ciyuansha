using System.Collections.Generic;

namespace CiyuanSha.Networking;

/// <summary>
/// 网络同步用的角色状态快照。
/// </summary>
public class NetworkCharacterState
{
    public int PeerId { get; set; }

    public string CharacterId { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string Faction { get; set; } = "Neutral";

    public string Gender { get; set; } = "Unknown";

    public int MaxHealth { get; set; } = 4;

    public int CurrentHealth { get; set; } = 4;

    public bool IsDefeated { get; set; }

    public bool IsChained { get; set; }

    public int EffectiveAttackRange { get; set; } = 1;

    public int HandCardCount { get; set; }

    public string GeneralCardFileName { get; set; } = string.Empty;

    public string EquippedWeaponName { get; set; } = string.Empty;

    public string EquippedArmorName { get; set; } = string.Empty;

    public string EquippedOffensiveHorseName { get; set; } = string.Empty;

    public string EquippedDefensiveHorseName { get; set; } = string.Empty;

    public string EquippedTreasureName { get; set; } = string.Empty;

    public List<NetworkHandCardState> DelayedTricks { get; set; } = new();

    public List<NetworkSkillState> Skills { get; set; } = new();

    public List<NetworkHandCardState> HandCards { get; set; } = new();
}
