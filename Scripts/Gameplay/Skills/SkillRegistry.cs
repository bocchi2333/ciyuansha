using System;
using System.Collections.Generic;
using System.Linq;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Central registry for all built-in skills.
/// </summary>
public static class SkillRegistry
{
    private static readonly Dictionary<string, SkillDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    static SkillRegistry()
    {
        Register<ActiveDrawSkill>("active_draw");
        Register<AutoDodgeSkill>("auto_dodge");
        Register<BonusDrawSkill>("bonus_draw");
        Register<DiscardStrikeSkill>("discard_strike");
        Register<FirstDamageBoostSkill>("first_damage_boost");
        Register<JudgementDrawSkill>("judgement_draw");
        Register<OffTurnLossDrawSkill>("off_turn_loss_draw");
        Register<PainDrawSkill>("pain_draw");
        Register<BladeConversionSkill>("blade_conversion");
        Register<MirrorJudgementSkill>("mirror_judgement");
        Register<ElementalResonanceSkill>("elemental_resonance");
        Register<AllySupplySkill>("ally_supply");
        Register<LimitBreakSkill>("limit_break");
    }

    public static IReadOnlyCollection<SkillDefinition> All => _definitions.Values;

    public static bool TryGetDefinition(string skillId, out SkillDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(skillId) && _definitions.TryGetValue(skillId.Trim(), out SkillDefinition? existing))
        {
            definition = existing;
            return true;
        }

        definition = null!;
        return false;
    }

    public static CharacterSkill? Create(string skillId)
    {
        return TryGetDefinition(skillId, out SkillDefinition definition)
            ? definition.Create()
            : null;
    }

    private static void Register<TSkill>(string skillId)
        where TSkill : CharacterSkill, new()
    {
        TSkill sample = new();
        string resolvedSkillId = string.IsNullOrWhiteSpace(skillId) ? sample.SkillId : skillId.Trim();
        _definitions[resolvedSkillId] = new SkillDefinition(
            resolvedSkillId,
            sample.DisplayName,
            sample.Description,
            sample.TriggerTimings,
            sample.UsageScope,
            sample.TriggerPriority,
            sample.IsActiveSkill,
            sample.TargetingMode,
            sample.MinTargetCount,
            sample.MaxTargetCount,
            sample.RequiresHandCardCost,
            () => new TSkill());
    }

    public static IReadOnlyList<SkillDefinition> ResolveMany(IEnumerable<string>? skillIds)
    {
        if (skillIds is null)
        {
            return Array.Empty<SkillDefinition>();
        }

        return skillIds
            .Select(skillId => TryGetDefinition(skillId, out SkillDefinition definition) ? definition : null)
            .Where(definition => definition is not null)
            .Cast<SkillDefinition>()
            .ToList();
    }
}
