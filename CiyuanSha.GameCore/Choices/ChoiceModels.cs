using System.Collections.ObjectModel;
using CiyuanSha.GameCore.Domain;

namespace CiyuanSha.GameCore.Choices;

public enum ChoiceKind
{
    SelectAction = 0,
    SelectCard = 1,
    SelectTarget = 2,
    SelectControl = 3,
    Confirm = 4,
    Order = 5,
    UseOrRespond = 6,
    Discard = 7,
    SelectGeneral = 8
}

public enum ChoiceOptionKind
{
    Action = 0,
    Card = 1,
    Player = 2,
    Control = 3,
    Skill = 4,
    General = 5
}

public sealed record ChoiceOption(
    string OptionId,
    ChoiceOptionKind Kind,
    string LabelKey,
    string EntityId = "",
    bool IsEnabled = true,
    string DisabledReasonKey = "",
    double AiValue = 0,
    IReadOnlyDictionary<string, string>? Tags = null)
{
    public IReadOnlyDictionary<string, string> SafeTags { get; } =
        Tags ?? ReadOnlyDictionary<string, string>.Empty;
}

public sealed record ChoiceRequest(
    string RequestId,
    long StateRevision,
    int ActingSeatId,
    ChoiceKind Kind,
    string PromptKey,
    IReadOnlyList<ChoiceOption> Options,
    int MinimumSelections = 1,
    int MaximumSelections = 1,
    bool AllowCancel = false,
    string ParentEventId = "",
    IReadOnlyDictionary<string, string>? PromptArguments = null,
    bool IsPrivate = true)
{
    public IReadOnlyDictionary<string, string> SafePromptArguments { get; } =
        PromptArguments ?? ReadOnlyDictionary<string, string>.Empty;

    public ChoiceRequest RedactFor(ViewerContext viewer)
    {
        bool canSeeOptions = viewer.Role == ViewerRole.OmniscientReplay
            || (viewer.Role == ViewerRole.Player && viewer.SeatId == ActingSeatId);
        if (canSeeOptions || !IsPrivate)
        {
            return this;
        }

        return this with
        {
            Options = Array.Empty<ChoiceOption>(),
            PromptArguments = ReadOnlyDictionary<string, string>.Empty
        };
    }

    public ChoiceValidationResult Validate(ChoiceResult result, int submittingSeatId, long currentRevision)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(RequestId, result.RequestId, StringComparison.Ordinal))
        {
            return ChoiceValidationResult.Invalid("choice.request_mismatch");
        }

        if (ActingSeatId != submittingSeatId)
        {
            return ChoiceValidationResult.Invalid("choice.wrong_actor");
        }

        if (StateRevision != result.StateRevision || currentRevision != StateRevision)
        {
            return ChoiceValidationResult.Invalid("choice.stale_revision");
        }

        if (result.IsCancelled)
        {
            return AllowCancel
                ? ChoiceValidationResult.Valid
                : ChoiceValidationResult.Invalid("choice.cancel_not_allowed");
        }

        if (result.SelectedOptionIds.Count < MinimumSelections
            || result.SelectedOptionIds.Count > MaximumSelections)
        {
            return ChoiceValidationResult.Invalid("choice.selection_count");
        }

        HashSet<string> unique = new(result.SelectedOptionIds, StringComparer.Ordinal);
        if (unique.Count != result.SelectedOptionIds.Count)
        {
            return ChoiceValidationResult.Invalid("choice.duplicate_option");
        }

        Dictionary<string, ChoiceOption> allowed = Options
            .Where(option => option.IsEnabled)
            .ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        if (result.SelectedOptionIds.Any(optionId => !allowed.ContainsKey(optionId)))
        {
            return ChoiceValidationResult.Invalid("choice.illegal_option");
        }

        return ChoiceValidationResult.Valid;
    }
}

public sealed record ChoiceResult(
    string RequestId,
    long StateRevision,
    IReadOnlyList<string> SelectedOptionIds,
    bool IsCancelled = false)
{
    public static ChoiceResult Cancel(string requestId, long stateRevision) =>
        new(requestId, stateRevision, Array.Empty<string>(), true);

    public static ChoiceResult Select(string requestId, long stateRevision, params string[] optionIds) =>
        new(requestId, stateRevision, optionIds, false);
}

public sealed record ChoiceValidationResult(bool IsValid, string ErrorKey)
{
    public static ChoiceValidationResult Valid { get; } = new(true, string.Empty);

    public static ChoiceValidationResult Invalid(string errorKey) => new(false, errorKey);
}
