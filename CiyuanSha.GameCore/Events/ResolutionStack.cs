using CiyuanSha.GameCore.Choices;

namespace CiyuanSha.GameCore.Events;

public enum ResolutionProgress
{
    Progressed = 0,
    WaitingForChoice = 1,
    Completed = 2,
    Faulted = 3
}

public sealed record ResolutionStep(
    ResolutionProgress Progress,
    RuleEvent? Event = null,
    ChoiceRequest? PendingChoice = null,
    string Error = "");

public sealed record ResolutionDecision(
    ChoiceRequest? PendingChoice = null,
    bool CancelEvent = false,
    string Fault = "")
{
    public static ResolutionDecision Continue { get; } = new();

    public static ResolutionDecision Wait(ChoiceRequest request) => new(request);

    public static ResolutionDecision Cancel { get; } = new(CancelEvent: true);

    public static ResolutionDecision Fail(string error) => new(Fault: error);
}

public delegate ResolutionDecision ResolutionHandler(ResolutionContext context);

public sealed class ResolutionContext
{
    private readonly ResolutionFrame _frame;

    internal ResolutionContext(ResolutionStack stack, ResolutionFrame frame, ChoiceResult? submittedChoice)
    {
        Stack = stack;
        _frame = frame;
        SubmittedChoice = submittedChoice;
    }

    public ResolutionStack Stack { get; }

    public RuleEvent Event => _frame.Event;

    public ChoiceResult? SubmittedChoice { get; }

    public void EnqueueNext(ResolutionFrame frame) => _frame.Next.Enqueue(frame);

    public void EnqueueAfter(ResolutionFrame frame) => _frame.After.Enqueue(frame);
}

public sealed class ResolutionFrame
{
    public ResolutionFrame(RuleEvent ruleEvent, ResolutionHandler? handler = null)
    {
        Event = ruleEvent;
        Handler = handler;
    }

    public RuleEvent Event { get; internal set; }

    public ResolutionHandler? Handler { get; }

    public Queue<ResolutionFrame> Next { get; } = new();

    public Queue<ResolutionFrame> After { get; } = new();

    public ChoiceRequest? PendingChoice { get; internal set; }

    internal bool HandlerCompleted { get; set; }

    internal bool IsCancelled { get; set; }

    internal int Depth { get; set; }
}

/// <summary>
/// Deterministic parent/child event scheduler inspired by Noname's GameEvent
/// lifecycle, independently implemented in C#.
/// </summary>
public sealed class ResolutionStack
{
    public const int DefaultMaximumDepth = 64;
    public const int DefaultMaximumSteps = 10_000;

    private readonly Stack<ResolutionFrame> _frames = new();
    private readonly Queue<ResolutionFrame> _roots = new();
    private int _steps;

    public ResolutionStack(int maximumDepth = DefaultMaximumDepth, int maximumSteps = DefaultMaximumSteps)
    {
        MaximumDepth = maximumDepth;
        MaximumSteps = maximumSteps;
    }

    public int MaximumDepth { get; }

    public int MaximumSteps { get; }

    public bool HasWork => _frames.Count > 0 || _roots.Count > 0;

    public ChoiceRequest? PendingChoice => _frames.Count == 0 ? null : _frames.Peek().PendingChoice;

    public void Enqueue(ResolutionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Depth = 0;
        _roots.Enqueue(frame);
    }

    public void EnqueueAfterCurrent(ResolutionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_frames.Count == 0)
        {
            throw new InvalidOperationException("There is no active resolution frame.");
        }
        _frames.Peek().After.Enqueue(frame);
    }

    public ResolutionStep Advance(ChoiceResult? submittedChoice = null)
    {
        if (++_steps > MaximumSteps)
        {
            return new ResolutionStep(ResolutionProgress.Faulted, Error: "resolution.maximum_steps_exceeded");
        }

        if (_frames.Count == 0)
        {
            if (_roots.Count == 0)
            {
                return new ResolutionStep(ResolutionProgress.Completed);
            }

            _frames.Push(_roots.Dequeue());
        }

        ResolutionFrame frame = _frames.Peek();
        if (frame.Depth > MaximumDepth)
        {
            return new ResolutionStep(ResolutionProgress.Faulted, frame.Event.AtStage(RuleEventStage.Faulted), Error: "resolution.maximum_depth_exceeded");
        }

        if (frame.PendingChoice is not null)
        {
            if (submittedChoice is null)
            {
                return new ResolutionStep(ResolutionProgress.WaitingForChoice, frame.Event, frame.PendingChoice);
            }

            frame.PendingChoice = null;
        }

        switch (frame.Event.Stage)
        {
            case RuleEventStage.Created:
                return Move(frame, RuleEventStage.Before);
            case RuleEventStage.Before:
                return Move(frame, RuleEventStage.Begin);
            case RuleEventStage.Begin:
                return Move(frame, RuleEventStage.Resolve);
            case RuleEventStage.Resolve:
                return ResolveFrame(frame, submittedChoice);
            case RuleEventStage.End:
                return Move(frame, RuleEventStage.After);
            case RuleEventStage.After:
                if (frame.After.Count > 0)
                {
                    PushChild(frame.After.Dequeue(), frame.Depth + 1);
                    return new ResolutionStep(ResolutionProgress.Progressed, _frames.Peek().Event);
                }
                return Move(frame, frame.IsCancelled ? RuleEventStage.Cancelled : RuleEventStage.Completed);
            case RuleEventStage.Completed:
            case RuleEventStage.Cancelled:
                RuleEvent terminalEvent = frame.Event;
                _frames.Pop();
                return new ResolutionStep(HasWork ? ResolutionProgress.Progressed : ResolutionProgress.Completed, terminalEvent);
            default:
                return new ResolutionStep(ResolutionProgress.Faulted, frame.Event.AtStage(RuleEventStage.Faulted), Error: "resolution.invalid_stage");
        }
    }

    public void ResetStepBudget() => _steps = 0;

    private ResolutionStep ResolveFrame(ResolutionFrame frame, ChoiceResult? submittedChoice)
    {
        if (!frame.HandlerCompleted)
        {
            ResolutionDecision decision = frame.Handler?.Invoke(new ResolutionContext(this, frame, submittedChoice))
                ?? ResolutionDecision.Continue;
            if (!string.IsNullOrWhiteSpace(decision.Fault))
            {
                frame.Event = frame.Event.AtStage(RuleEventStage.Faulted);
                return new ResolutionStep(ResolutionProgress.Faulted, frame.Event, Error: decision.Fault);
            }

            if (decision.PendingChoice is not null)
            {
                frame.PendingChoice = decision.PendingChoice;
                return new ResolutionStep(ResolutionProgress.WaitingForChoice, frame.Event, decision.PendingChoice);
            }

            frame.IsCancelled = decision.CancelEvent;
            frame.HandlerCompleted = true;
        }

        if (frame.Next.Count > 0)
        {
            PushChild(frame.Next.Dequeue(), frame.Depth + 1);
            return new ResolutionStep(ResolutionProgress.Progressed, _frames.Peek().Event);
        }

        return Move(frame, RuleEventStage.End);
    }

    private void PushChild(ResolutionFrame child, int depth)
    {
        if (string.IsNullOrWhiteSpace(child.Event.ParentEventId) && _frames.Count > 0)
        {
            child.Event = child.Event with { ParentEventId = _frames.Peek().Event.EventId };
        }
        child.Depth = depth;
        _frames.Push(child);
    }

    private static ResolutionStep Move(ResolutionFrame frame, RuleEventStage stage)
    {
        frame.Event = frame.Event.AtStage(stage);
        return new ResolutionStep(ResolutionProgress.Progressed, frame.Event);
    }
}
