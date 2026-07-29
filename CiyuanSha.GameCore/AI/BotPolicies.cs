using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Determinism;
using CiyuanSha.GameCore.Domain;

namespace CiyuanSha.GameCore.AI;

public sealed record BotContext(
    BotDifficulty Difficulty,
    ulong DecisionSeed,
    int NodeBudget = 64,
    Func<ChoiceOption, int, double>? EvaluateFuture = null);

public interface IBotPolicy
{
    ChoiceResult Choose(GameView view, ChoiceRequest request, BotContext context);
}

/// <summary>
/// Host-side deterministic policy. It only receives a redacted GameView and
/// the legal ChoiceRequest, so it cannot inspect hidden opponent cards.
/// </summary>
public sealed class DeterministicBotPolicy : IBotPolicy
{
    public ChoiceResult Choose(GameView view, ChoiceRequest request, BotContext context)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        List<ChoiceOption> legal = request.Options.Where(option => option.IsEnabled).ToList();
        if (legal.Count < request.MinimumSelections)
        {
            if (request.AllowCancel)
            {
                return ChoiceResult.Cancel(request.RequestId, request.StateRevision);
            }

            throw new InvalidOperationException($"Choice {request.RequestId} has fewer legal options than required.");
        }

        return context.Difficulty switch
        {
            BotDifficulty.Easy => ChooseEasy(request, legal, context),
            BotDifficulty.Standard => ChooseScored(request, legal, context, includeFuture: false),
            BotDifficulty.Hard => ChooseScored(request, legal, context, includeFuture: true),
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Difficulty, "Unknown bot difficulty.")
        };
    }

    private static ChoiceResult ChooseEasy(ChoiceRequest request, IReadOnlyList<ChoiceOption> legal, BotContext context)
    {
        DeterministicRandom random = new(context.DecisionSeed ^ StableHash(request.RequestId));
        int selectionCount = request.MinimumSelections;
        List<ChoiceOption> pool = legal.OrderBy(option => option.OptionId, StringComparer.Ordinal).ToList();
        List<string> selected = new();
        for (int index = 0; index < selectionCount; index++)
        {
            int selectedIndex = random.NextInt(pool.Count);
            selected.Add(pool[selectedIndex].OptionId);
            pool.RemoveAt(selectedIndex);
        }

        return new ChoiceResult(request.RequestId, request.StateRevision, selected);
    }

    private static ChoiceResult ChooseScored(
        ChoiceRequest request,
        IReadOnlyList<ChoiceOption> legal,
        BotContext context,
        bool includeFuture)
    {
        int explored = 0;
        List<(ChoiceOption Option, double Score)> scored = legal
            .OrderBy(option => option.OptionId, StringComparer.Ordinal)
            .Select(option =>
            {
                double score = option.AiValue + ReadNumericTag(option, "ai.benefit") - ReadNumericTag(option, "ai.risk");
                if (includeFuture && context.EvaluateFuture is not null && explored < context.NodeBudget)
                {
                    int remaining = Math.Max(0, context.NodeBudget - explored);
                    int depthBudget = Math.Min(2, remaining);
                    score += context.EvaluateFuture(option, depthBudget);
                    explored += Math.Max(1, depthBudget);
                }
                return (option, score);
            })
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.option.OptionId, StringComparer.Ordinal)
            .Select(item => (item.option, item.score))
            .ToList();

        if (request.AllowCancel && request.MinimumSelections == 0 && scored.All(item => item.Score < 0))
        {
            return ChoiceResult.Cancel(request.RequestId, request.StateRevision);
        }

        int count = request.MinimumSelections;
        while (count < request.MaximumSelections && count < scored.Count && scored[count].Score > 0)
        {
            count++;
        }

        return new ChoiceResult(
            request.RequestId,
            request.StateRevision,
            scored.Take(count).Select(item => item.Option.OptionId).ToArray());
    }

    private static double ReadNumericTag(ChoiceOption option, string key) =>
        option.SafeTags.TryGetValue(key, out string? value)
        && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }
}
