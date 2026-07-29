using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Skills;

public static class BuiltInEffects
{
    public static EffectRegistry CreateRegistry()
    {
        EffectRegistry registry = new();
        foreach (string effectId in SkillEffectIds)
        {
            registry.Register(new BuiltInSkillEffect(effectId));
        }
        foreach (string effectId in CardEffectIds)
        {
            registry.Register(new BuiltInCardEffect(effectId));
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

    private sealed class BuiltInSkillEffect : ISkillEffect
    {
        public BuiltInSkillEffect(string effectId) => EffectId = effectId;

        public string EffectId { get; }

        public bool CanTrigger(SkillContext context) => context.Owner.IsAlive
            && context.State.Players.ContainsKey(context.Owner.SeatId);

        public ChoiceRequest? BuildCostChoice(SkillContext context, Func<string> requestIdFactory) => null;

        public SkillEffectResult Resolve(SkillContext context)
        {
            if (!CanTrigger(context))
            {
                return SkillEffectResult.NotApplied("skill.owner_unavailable");
            }
            RuleEvent intent = new(
                $"effect:{EffectId}:{context.Owner.SeatId}:{context.State.Revision}",
                context.State.Revision,
                context.TriggerEvent?.EventId ?? string.Empty,
                RuleEventKind.SkillTriggered,
                RuleEventStage.Created,
                context.Owner.SeatId,
                context.TargetSeatIds,
                new TextRuleEventPayload("skill.effect_resolved", new Dictionary<string, string> { ["effectId"] = EffectId }),
                SourceSkillId: EffectId);
            return new SkillEffectResult(true, new[] { intent });
        }

        public double GetAiValue(SkillContext context) => CanTrigger(context) ? 1 : double.NegativeInfinity;
    }

    private sealed class BuiltInCardEffect : ICardEffect
    {
        public BuiltInCardEffect(string effectId) => EffectId = effectId;

        public string EffectId { get; }

        public CardEffectResult Resolve(CardEffectContext context)
        {
            bool valid = context.Source.IsAlive
                && context.State.Cards.TryGetValue(context.Card.InstanceId, out CardState? authoritative)
                && ReferenceEquals(authoritative, context.Card)
                && context.Targets.All(target => target.IsAlive && context.State.Players.ContainsKey(target.SeatId));
            if (!valid)
            {
                return new CardEffectResult(false, Array.Empty<RuleEvent>(), FailureKey: "card.effect_context_invalid");
            }
            RuleEvent intent = new(
                $"effect:{EffectId}:{context.Card.InstanceId}:{context.State.Revision}",
                context.State.Revision,
                string.Empty,
                RuleEventKind.CardUsed,
                RuleEventStage.Created,
                context.Source.SeatId,
                context.Targets.Select(target => target.SeatId).ToArray(),
                new CardMoveRuleEventPayload(context.Card.InstanceId, context.Card.Zone, CardZone.Processing, context.Source.SeatId));
            return new CardEffectResult(true, new[] { intent });
        }
    }
}
