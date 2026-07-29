using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;

namespace CiyuanSha.GameCore.Tests;

public sealed class ChoiceAndEventTests
{
    [Fact]
    public void Choice_RejectsWrongActorStaleAndIllegalOptions()
    {
        ChoiceRequest request = new(
            "choice-1",
            9,
            2,
            ChoiceKind.SelectCard,
            "choose",
            new[] { new ChoiceOption("card:a", ChoiceOptionKind.Card, "A") });

        Assert.False(request.Validate(ChoiceResult.Select("choice-1", 9, "card:a"), 1, 9).IsValid);
        Assert.False(request.Validate(ChoiceResult.Select("choice-1", 8, "card:a"), 2, 9).IsValid);
        Assert.False(request.Validate(ChoiceResult.Select("choice-1", 9, "card:b"), 2, 9).IsValid);
        Assert.True(request.Validate(ChoiceResult.Select("choice-1", 9, "card:a"), 2, 9).IsValid);
    }

    [Fact]
    public void Choice_EnforcesCountsDuplicatesDisabledOptionsAndCancellationPolicy()
    {
        ChoiceRequest request = new(
            "choice-many",
            12,
            3,
            ChoiceKind.SelectCard,
            "choose-many",
            new[]
            {
                new ChoiceOption("a", ChoiceOptionKind.Card, "A"),
                new ChoiceOption("b", ChoiceOptionKind.Card, "B"),
                new ChoiceOption("disabled", ChoiceOptionKind.Card, "Disabled", IsEnabled: false)
            },
            MinimumSelections: 1,
            MaximumSelections: 2,
            AllowCancel: false);

        Assert.Equal("choice.selection_count", request.Validate(ChoiceResult.Select(request.RequestId, 12), 3, 12).ErrorKey);
        Assert.Equal("choice.selection_count", request.Validate(ChoiceResult.Select(request.RequestId, 12, "a", "b", "disabled"), 3, 12).ErrorKey);
        Assert.Equal("choice.duplicate_option", request.Validate(ChoiceResult.Select(request.RequestId, 12, "a", "a"), 3, 12).ErrorKey);
        Assert.Equal("choice.illegal_option", request.Validate(ChoiceResult.Select(request.RequestId, 12, "disabled"), 3, 12).ErrorKey);
        Assert.Equal("choice.cancel_not_allowed", request.Validate(ChoiceResult.Cancel(request.RequestId, 12), 3, 12).ErrorKey);
        Assert.True((request with { AllowCancel = true }).Validate(ChoiceResult.Cancel(request.RequestId, 12), 3, 12).IsValid);
    }

    [Fact]
    public void PrivateChoice_RedactsOptionsFromSpectatorAndOtherPlayers()
    {
        ChoiceRequest request = new(
            "choice-1",
            3,
            2,
            ChoiceKind.SelectCard,
            "choose",
            new[] { new ChoiceOption("secret", ChoiceOptionKind.Card, "secret") });

        Assert.Empty(request.RedactFor(ViewerContext.Spectator).Options);
        Assert.Empty(request.RedactFor(ViewerContext.ForPlayer(1)).Options);
        Assert.Single(request.RedactFor(ViewerContext.ForPlayer(2)).Options);
        Assert.Single(request.RedactFor(ViewerContext.OmniscientReplay).Options);
    }

    [Fact]
    public void ResolutionStack_ProcessesNextBeforeAfter()
    {
        List<string> resolved = new();
        long sequence = 0;
        ResolutionFrame? nextFrame = null;
        ResolutionFrame? afterFrame = null;
        ResolutionFrame root = Frame("root", ref sequence, context =>
        {
            resolved.Add("root");
            nextFrame = Frame("next", ref sequence, _ => { resolved.Add("next"); return ResolutionDecision.Continue; });
            afterFrame = Frame("after", ref sequence, _ => { resolved.Add("after"); return ResolutionDecision.Continue; });
            context.EnqueueNext(nextFrame);
            context.EnqueueAfter(afterFrame);
            return ResolutionDecision.Continue;
        });
        ResolutionStack stack = new();
        stack.Enqueue(root);

        while (stack.HasWork)
        {
            ResolutionStep result = stack.Advance();
            Assert.NotEqual(ResolutionProgress.Faulted, result.Progress);
        }

        Assert.Equal(new[] { "root", "next", "after" }, resolved);
        Assert.Equal("root", nextFrame!.Event.ParentEventId);
        Assert.Equal("root", afterFrame!.Event.ParentEventId);
    }

    [Fact]
    public void ResolutionStack_SuspendsAndResumesTheSameFrame()
    {
        ChoiceRequest request = new(
            "wait-1", 1, 2, ChoiceKind.Confirm, "confirm",
            new[] { new ChoiceOption("yes", ChoiceOptionKind.Control, "yes") });
        ChoiceResult accepted = ChoiceResult.Select(request.RequestId, request.StateRevision, "yes");
        ChoiceResult? observed = null;
        RuleEvent ruleEvent = new("root", 1, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 2, Array.Empty<int>(), EmptyRuleEventPayload.Instance);
        ResolutionStack stack = new();
        stack.Enqueue(new ResolutionFrame(ruleEvent, context =>
        {
            if (context.SubmittedChoice is null)
            {
                return ResolutionDecision.Wait(request);
            }
            observed = context.SubmittedChoice;
            return ResolutionDecision.Continue;
        }));

        ResolutionStep step;
        do
        {
            step = stack.Advance();
        }
        while (step.Progress == ResolutionProgress.Progressed);
        Assert.Equal(ResolutionProgress.WaitingForChoice, step.Progress);
        Assert.Same(request, stack.PendingChoice);

        step = stack.Advance(accepted);
        Assert.NotEqual(ResolutionProgress.Faulted, step.Progress);
        while (stack.HasWork)
        {
            Assert.NotEqual(ResolutionProgress.Faulted, stack.Advance().Progress);
        }
        Assert.Same(accepted, observed);
    }

    [Fact]
    public void ResolutionStack_FaultsAtConfiguredStepAndDepthLimits()
    {
        ResolutionStack stepLimited = new(maximumDepth: 4, maximumSteps: 1);
        stepLimited.Enqueue(new ResolutionFrame(
            new RuleEvent("step-root", 1, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 0, Array.Empty<int>(), EmptyRuleEventPayload.Instance)));
        Assert.Equal(ResolutionProgress.Progressed, stepLimited.Advance().Progress);
        ResolutionStep stepFault = stepLimited.Advance();
        Assert.Equal(ResolutionProgress.Faulted, stepFault.Progress);
        Assert.Equal("resolution.maximum_steps_exceeded", stepFault.Error);

        ResolutionStack depthLimited = new(maximumDepth: 0, maximumSteps: 100);
        ResolutionFrame child = new(
            new RuleEvent("child", 2, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 0, Array.Empty<int>(), EmptyRuleEventPayload.Instance));
        ResolutionFrame root = new(
            new RuleEvent("root", 1, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 0, Array.Empty<int>(), EmptyRuleEventPayload.Instance),
            context =>
            {
                context.EnqueueNext(child);
                return ResolutionDecision.Continue;
            });
        depthLimited.Enqueue(root);
        ResolutionStep current;
        do
        {
            current = depthLimited.Advance();
        }
        while (current.Progress == ResolutionProgress.Progressed);
        Assert.Equal(ResolutionProgress.Faulted, current.Progress);
        Assert.Equal("resolution.maximum_depth_exceeded", current.Error);
        Assert.Equal("root", child.Event.ParentEventId);
    }

    private static ResolutionFrame Frame(string id, ref long sequence, ResolutionHandler handler) => new(
        new RuleEvent(id, ++sequence, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 0, Array.Empty<int>(), EmptyRuleEventPayload.Instance),
        handler);
}
