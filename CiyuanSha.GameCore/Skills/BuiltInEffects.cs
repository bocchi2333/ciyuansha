using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Skills;

public static class BuiltInEffects
{
    public static EffectRegistry CreateRegistry()
    {
        EffectRegistry registry = new();
        foreach (string effectId in SkillEffectIds)
        {
            registry.Register(new DeclarativeSkillEffect(effectId));
        }
        foreach (string effectId in CardEffectIds)
        {
            registry.Register(new DeclarativeCardEffect(effectId));
        }
        return registry;
    }

    public static IReadOnlyList<string> SkillEffectIds { get; } = new[]
    {
        "skill.active_draw",
        "skill.off_turn_loss_draw",
        "skill.auto_dodge",
        "skill.bonus_draw",
        "skill.discard_strike",
        "skill.judgement_draw",
        "skill.first_damage_boost",
        "skill.pain_draw",
        "skill.blade_conversion",
        "skill.mirror_judgement",
        "skill.elemental_resonance",
        "skill.ally_supply",
        "skill.limit_break",
        "skill.boss_pressure",
        "skill.boss_phase_two"
    };

    public static IReadOnlyList<string> CardEffectIds { get; } = new[]
    {
        "card.slash",
        "card.fire_slash",
        "card.thunder_slash",
        "card.dodge",
        "card.peach",
        "card.wine",
        "card.duel",
        "card.dismantle",
        "card.snatch",
        "card.ex_nihilo",
        "card.nullification",
        "card.barbarians",
        "card.arrow_barrage",
        "card.peach_garden",
        "card.harvest",
        "card.indulgence",
        "card.supply_shortage",
        "card.lightning",
        "card.iron_chain",
        "card.fire_attack",
        "card.borrow_sword",
        "card.equipment"
    };

    private sealed class DeclarativeSkillEffect : ISkillEffect
    {
        public DeclarativeSkillEffect(string effectId) => EffectId = effectId;

        public string EffectId { get; }

        public bool CanTrigger(SkillContext context) => context.Owner.IsAlive;

        public ChoiceRequest? BuildCostChoice(SkillContext context, Func<string> requestIdFactory) => null;

        public SkillEffectResult Resolve(SkillContext context) => new(true, Array.Empty<RuleEvent>());

        public double GetAiValue(SkillContext context) => 0;
    }

    private sealed class DeclarativeCardEffect : ICardEffect
    {
        public DeclarativeCardEffect(string effectId) => EffectId = effectId;

        public string EffectId { get; }

        public CardEffectResult Resolve(CardEffectContext context) => new(true, Array.Empty<RuleEvent>());
    }
}
