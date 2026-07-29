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
        ResolutionFrame root = Frame("root", ref sequence, context =>
        {
            resolved.Add("root");
            context.EnqueueNext(Frame("next", ref sequence, _ => { resolved.Add("next"); return ResolutionDecision.Continue; }));
            context.EnqueueAfter(Frame("after", ref sequence, _ => { resolved.Add("after"); return ResolutionDecision.Continue; }));
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
    }

    private static ResolutionFrame Frame(string id, ref long sequence, ResolutionHandler handler) => new(
        new RuleEvent(id, ++sequence, string.Empty, RuleEventKind.Custom, RuleEventStage.Created, 0, Array.Empty<int>(), EmptyRuleEventPayload.Instance),
        handler);
}
