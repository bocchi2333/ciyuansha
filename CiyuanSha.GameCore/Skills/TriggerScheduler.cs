using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Skills;

public sealed record SkillTriggerCandidate(
    int OwnerSeatId,
    SkillDefinitionV2 Skill,
    SkillTriggerDefinition Trigger);

/// <summary>Stable Noname-style trigger ordering for a single event stage.</summary>
public static class TriggerScheduler
{
    public static IReadOnlyList<SkillTriggerCandidate> Order(
        IEnumerable<SkillTriggerCandidate> candidates,
        int currentSeatId,
        IReadOnlyList<int> turnOrder)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(turnOrder);
        Dictionary<int, int> seatOrder = BuildSeatOrder(currentSeatId, turnOrder);
        return candidates
            .OrderBy(candidate => candidate.Trigger.FirstDo ? 0 : candidate.Trigger.LastDo ? 2 : 1)
            .ThenByDescending(candidate => candidate.Trigger.Priority)
            .ThenBy(candidate => seatOrder.GetValueOrDefault(candidate.OwnerSeatId, int.MaxValue))
            .ThenBy(candidate => candidate.Skill.SkillId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<SkillTriggerCandidate> Match(
        GameState state,
        IEnumerable<SkillDefinitionV2> skills,
        RuleEventKind eventKind,
        RuleEventStage eventStage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(skills);
        Dictionary<string, SkillDefinitionV2> definitions = skills.ToDictionary(skill => skill.SkillId, StringComparer.Ordinal);
        List<SkillTriggerCandidate> candidates = new();
        foreach (PlayerState player in state.Players.Values.Where(player => player.IsAlive))
        {
            foreach (string skillId in player.SkillIds.Concat(player.TemporarySkillIds).Distinct(StringComparer.Ordinal))
            {
                if (!definitions.TryGetValue(skillId, out SkillDefinitionV2? skill))
                {
                    continue;
                }
                candidates.AddRange(skill.Triggers
                    .Where(trigger => trigger.EventKind == eventKind && trigger.EventStage == eventStage)
                    .Select(trigger => new SkillTriggerCandidate(player.SeatId, skill, trigger)));
            }
        }
        return Order(candidates, state.CurrentSeatId, state.TurnOrder);
    }

    private static Dictionary<int, int> BuildSeatOrder(int currentSeatId, IReadOnlyList<int> turnOrder)
    {
        Dictionary<int, int> result = new();
        if (turnOrder.Count == 0)
        {
            return result;
        }
        int start = -1;
        for (int index = 0; index < turnOrder.Count; index++)
        {
            if (turnOrder[index] == currentSeatId)
            {
                start = index;
                break;
            }
        }
        if (start < 0)
        {
            start = 0;
        }
        for (int offset = 0; offset < turnOrder.Count; offset++)
        {
            result[turnOrder[(start + offset) % turnOrder.Count]] = offset;
        }
        return result;
    }
}
