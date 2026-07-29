using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;

namespace CiyuanSha.GameCore.Tests;

public sealed class CardRuleVectorTests
{
    [Fact]
    public void FireAttack_RevealsThenRequiresMatchingSuitDiscard()
    {
        ContentRegistry content = TestContent.Load().Registry;
        GameEngine engine = FindEngine(content, BuiltInModeIds.Duel, 2, card => card.DefinitionId == "fire_attack");
        int sourceSeatId = engine.State.CurrentSeatId;
        int targetSeatId = engine.State.Players.Keys.Single(seatId => seatId != sourceSeatId);
        string fireAttackId = engine.State.Players[sourceSeatId].Hand.Single(cardId => engine.State.Cards[cardId].DefinitionId == "fire_attack");

        Submit(engine, $"action:use:{fireAttackId}");
        Submit(engine, $"seat:{targetSeatId}");
        DeclineNullification(engine);
        ChoiceRequest reveal = engine.State.PendingChoice ?? throw new XunitException("Expected a Fire Attack reveal choice.");
        Assert.Equal("card.fire_attack.choose_reveal", reveal.PromptKey);
        ChoiceOption revealOption = reveal.Options.First(option =>
            engine.State.Players[sourceSeatId].Hand.Any(sourceCardId =>
                engine.State.Cards[sourceCardId].Suit == engine.State.Cards[option.EntityId].Suit));
        int healthBefore = engine.State.Players[targetSeatId].Health;
        Submit(engine, revealOption.OptionId);
        ChoiceRequest discard = engine.State.PendingChoice ?? throw new XunitException("Expected a Fire Attack discard choice.");
        ChoiceOption matchingDiscard = discard.Options.First(option => option.OptionId.StartsWith("card:", StringComparison.Ordinal));
        Submit(engine, matchingDiscard.OptionId);

        Assert.Equal(healthBefore - 1, engine.State.Players[targetSeatId].Health);
        Assert.Contains(engine.Journal.Entries, entry => entry.RuleEvent?.Kind == CiyuanSha.GameCore.Events.RuleEventKind.CardRevealed);
    }

    [Fact]
    public void BorrowSword_UsesSlashOrTransfersWeapon()
    {
        ContentRegistry content = TestContent.Load().Registry;
        GameEngine attackEngine = FindEngine(content, BuiltInModeIds.Duel, 2, card => card.DefinitionId == "borrow_sword");
        int source = attackEngine.State.CurrentSeatId;
        int holder = attackEngine.State.Players.Keys.Single(seatId => seatId != source);
        string borrow = attackEngine.State.Players[source].Hand.Single(cardId => attackEngine.State.Cards[cardId].DefinitionId == "borrow_sword");
        string weapon = EquipDefinition(attackEngine, holder, "crossbow", borrow);
        string slash = PutDefinitionInHand(attackEngine, holder, "slash", borrow, weapon);
        RefreshPlayChoice(attackEngine);
        int healthBefore = attackEngine.State.Players[source].Health;

        Submit(attackEngine, $"action:use:{borrow}");
        Submit(attackEngine, $"seat:{holder}");
        DeclineNullification(attackEngine);
        Submit(attackEngine, $"seat:{source}");
        Submit(attackEngine, $"card:{slash}");
        Submit(attackEngine, "control:decline");

        Assert.Equal(healthBefore - 1, attackEngine.State.Players[source].Health);
        Assert.Equal(weapon, attackEngine.State.Players[holder].Equipment[EquipmentSlot.Weapon]);

        GameEngine transferEngine = FindEngine(content, BuiltInModeIds.Duel, 2, card => card.DefinitionId == "borrow_sword");
        source = transferEngine.State.CurrentSeatId;
        holder = transferEngine.State.Players.Keys.Single(seatId => seatId != source);
        borrow = transferEngine.State.Players[source].Hand.Single(cardId => transferEngine.State.Cards[cardId].DefinitionId == "borrow_sword");
        weapon = EquipDefinition(transferEngine, holder, "crossbow", borrow);
        RefreshPlayChoice(transferEngine);
        Submit(transferEngine, $"action:use:{borrow}");
        Submit(transferEngine, $"seat:{holder}");
        DeclineNullification(transferEngine);
        Submit(transferEngine, $"seat:{source}");
        Submit(transferEngine, "control:decline");

        Assert.False(transferEngine.State.Players[holder].Equipment.ContainsKey(EquipmentSlot.Weapon));
        Assert.Contains(weapon, transferEngine.State.Players[source].Hand);
    }

    [Fact]
    public void MassTrick_NullificationCancelsOnlyCurrentTarget()
    {
        ContentRegistry content = TestContent.Load().Registry;
        GameEngine engine = FindEngine(content, BuiltInModeIds.TeamTwoVersusTwo, 4, card => card.DefinitionId == "barbarians");
        int source = engine.State.CurrentSeatId;
        int[] targets = SeatOrderAfter(engine, source).Where(seatId => seatId != source).ToArray();
        string barbarians = engine.State.Players[source].Hand.Single(cardId => engine.State.Cards[cardId].DefinitionId == "barbarians");
        RemoveDefinitionFromAllHands(engine, "nullification");
        string nullification = PutDefinitionInHand(engine, targets[0], "nullification", barbarians);
        int firstHealth = engine.State.Players[targets[0]].Health;

        Submit(engine, $"action:use:{barbarians}");
        ChoiceRequest nullRequest = engine.State.PendingChoice ?? throw new XunitException("Expected a Nullification choice.");
        Assert.Equal("response.nullification", nullRequest.PromptKey);
        Assert.Equal(targets[0], nullRequest.ActingSeatId);
        Submit(engine, $"card:{nullification}");

        Assert.Equal(firstHealth, engine.State.Players[targets[0]].Health);
        ChoiceRequest next = engine.State.PendingChoice ?? throw new XunitException("Expected the next mass-trick response.");
        Assert.Equal(targets[1], next.ActingSeatId);
        Assert.Equal("response.slash", next.PromptKey);
    }

    [Fact]
    public void ArmorAndWeaponPassives_ApplyToSlashDamage()
    {
        ContentRegistry content = TestContent.Load().Registry;

        GameEngine vine = FindEngine(content, BuiltInModeIds.Duel, 2, IsPhysicalSlash);
        (int source, int target, string slash) = PrepareSlash(vine);
        EquipDefinition(vine, target, "vine_armor", slash);
        int health = vine.State.Players[target].Health;
        UseSlashAt(vine, slash, target);
        Assert.Equal(health, vine.State.Players[target].Health);

        GameEngine qinggang = FindEngine(content, BuiltInModeIds.Duel, 2, IsPhysicalSlash);
        (source, target, slash) = PrepareSlash(qinggang);
        EquipDefinition(qinggang, source, "qinggang_sword", slash);
        EquipDefinition(qinggang, target, "vine_armor", slash);
        health = qinggang.State.Players[target].Health;
        UseSlashAt(qinggang, slash, target, declineDodge: true);
        Assert.Equal(health - 1, qinggang.State.Players[target].Health);

        GameEngine guding = FindEngine(content, BuiltInModeIds.Duel, 2, IsPhysicalSlash);
        (source, target, slash) = PrepareSlash(guding);
        EquipDefinition(guding, source, "guding_blade", slash);
        ClearHand(guding, target);
        health = guding.State.Players[target].Health;
        UseSlashAt(guding, slash, target, declineDodge: true);
        Assert.Equal(health - 2, guding.State.Players[target].Health);

        GameEngine silver = FindEngine(content, BuiltInModeIds.Duel, 2, IsPhysicalSlash);
        (source, target, slash) = PrepareSlash(silver);
        EquipDefinition(silver, target, "silver_lion", slash);
        ClearHand(silver, target);
        silver.State.Players[source].Marks["wine_damage"] = 1;
        health = silver.State.Players[target].Health;
        UseSlashAt(silver, slash, target, declineDodge: true);
        Assert.Equal(health - 1, silver.State.Players[target].Health);
    }

    [Fact]
    public void Fangtian_AllowsThreeTargetsWhenSlashIsLastHandCard()
    {
        ContentRegistry content = TestContent.Load().Registry;
        GameEngine engine = FindEngine(content, BuiltInModeIds.TeamTwoVersusTwo, 4, IsPhysicalSlash);
        int source = engine.State.CurrentSeatId;
        string slash = engine.State.Players[source].Hand.First(cardId => IsPhysicalSlash(engine.State.Cards[cardId]));
        foreach (string other in engine.State.Players[source].Hand.Where(cardId => cardId != slash).ToArray())
        {
            MoveToDrawPile(engine, other);
        }
        EquipDefinition(engine, source, "fangtian_halberd", slash);

        Submit(engine, $"action:use:{slash}");
        ChoiceRequest targets = engine.State.PendingChoice ?? throw new XunitException("Expected a Fangtian target choice.");
        Assert.Equal(3, targets.MaximumSelections);
        Assert.Equal(3, targets.Options.Count);
    }

    private static (int Source, int Target, string Slash) PrepareSlash(GameEngine engine)
    {
        int source = engine.State.CurrentSeatId;
        int target = engine.State.Players.Keys.Single(seatId => seatId != source);
        string slash = engine.State.Players[source].Hand.First(cardId => IsPhysicalSlash(engine.State.Cards[cardId]));
        return (source, target, slash);
    }

    private static void UseSlashAt(GameEngine engine, string slashId, int targetSeatId, bool declineDodge = false)
    {
        Submit(engine, $"action:use:{slashId}");
        Submit(engine, $"seat:{targetSeatId}");
        if (declineDodge && engine.State.PendingChoice?.PromptKey == "response.dodge")
        {
            Submit(engine, "control:decline");
        }
    }

    private static GameEngine FindEngine(
        ContentRegistry content,
        string modeId,
        int playerCount,
        Func<CardState, bool> sourceCardPredicate)
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            MatchConfig config = CreateConfig(content, modeId, playerCount, seed);
            GameEngine engine = GameEngine.Start(config, content);
            PlayerState source = engine.State.Players[engine.State.CurrentSeatId];
            if (source.Hand.Any(cardId => sourceCardPredicate(engine.State.Cards[cardId])))
            {
                return engine;
            }
        }
        throw new XunitException("Could not find a deterministic opening hand for the rule vector.");
    }

    private static MatchConfig CreateConfig(ContentRegistry content, string modeId, int playerCount, ulong seed)
    {
        string[] generals = content.Generals.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(playerCount).ToArray();
        return new MatchConfig(
            $"vector-{modeId}-{seed}",
            modeId,
            seed,
            generals.Select((general, index) => new PlayerConfig(index + 1, $"p{index + 1}", $"P{index + 1}", general)).ToArray(),
            "standard_military_161",
            content.Packs.Values.Select(pack => pack.Reference).OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToArray(),
            1);
    }

    private static void Submit(GameEngine engine, params string[] optionIds)
    {
        ChoiceRequest request = engine.State.PendingChoice ?? throw new XunitException("Expected a pending choice.");
        EngineStepResult result = engine.Advance(request.ActingSeatId, ChoiceResult.Select(request.RequestId, request.StateRevision, optionIds));
        Assert.NotEqual(EngineProgress.Rejected, result.Progress);
        Assert.NotEqual(EngineProgress.Faulted, result.Progress);
    }

    private static void RefreshPlayChoice(GameEngine engine)
    {
        System.Reflection.MethodInfo method = typeof(GameEngine).GetMethod(
            "RequestPlayAction",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new XunitException("RequestPlayAction test hook was not found.");
        method.Invoke(engine, new object[] { engine.State.Players[engine.State.CurrentSeatId] });
    }

    private static void DeclineNullification(GameEngine engine)
    {
        int guard = 0;
        while (engine.State.PendingChoice?.PromptKey == "response.nullification")
        {
            Submit(engine, "control:decline");
            if (++guard > 20)
            {
                throw new XunitException("Nullification chain did not terminate.");
            }
        }
    }

    private static string EquipDefinition(GameEngine engine, int seatId, string definitionId, params string[] excluded)
    {
        string cardId = FindCard(engine, definitionId, excluded);
        DetachCard(engine, cardId);
        EquipmentSlot slot = definitionId switch
        {
            "vine_armor" or "silver_lion" or "eight_diagram" or "renwang_shield" => EquipmentSlot.Armor,
            "chitu" or "dawan" or "zixing" => EquipmentSlot.OffensiveMount,
            "jueying" or "dilu" or "zhuahuangfeidian" or "hualiu" => EquipmentSlot.DefensiveMount,
            _ => EquipmentSlot.Weapon
        };
        engine.State.Players[seatId].Equipment[slot] = cardId;
        CardState card = engine.State.Cards[cardId];
        card.Zone = CardZone.Equipment;
        card.OwnerSeatId = seatId;
        card.IsFaceUp = true;
        return cardId;
    }

    private static string PutDefinitionInHand(GameEngine engine, int seatId, string definitionId, params string[] excluded)
    {
        string cardId = FindCard(engine, definitionId, excluded);
        DetachCard(engine, cardId);
        engine.State.Players[seatId].Hand.Add(cardId);
        CardState card = engine.State.Cards[cardId];
        card.Zone = CardZone.Hand;
        card.OwnerSeatId = seatId;
        card.IsFaceUp = false;
        return cardId;
    }

    private static string FindCard(GameEngine engine, string definitionId, IEnumerable<string> excluded) => engine.State.Cards.Values
        .Where(card => card.DefinitionId == definitionId && !excluded.Contains(card.InstanceId, StringComparer.Ordinal))
        .OrderBy(card => card.InstanceId, StringComparer.Ordinal)
        .Select(card => card.InstanceId)
        .First();

    private static void DetachCard(GameEngine engine, string cardId)
    {
        engine.State.DrawPile.Remove(cardId);
        engine.State.DiscardPile.Remove(cardId);
        engine.State.ProcessingArea.Remove(cardId);
        foreach (PlayerState player in engine.State.Players.Values)
        {
            player.Hand.Remove(cardId);
            player.JudgementArea.Remove(cardId);
            foreach (EquipmentSlot slot in player.Equipment.Where(entry => entry.Value == cardId).Select(entry => entry.Key).ToArray())
            {
                player.Equipment.Remove(slot);
            }
        }
    }

    private static void ClearHand(GameEngine engine, int seatId)
    {
        foreach (string cardId in engine.State.Players[seatId].Hand.ToArray())
        {
            MoveToDrawPile(engine, cardId);
        }
    }

    private static void MoveToDrawPile(GameEngine engine, string cardId)
    {
        DetachCard(engine, cardId);
        engine.State.DrawPile.Insert(0, cardId);
        CardState card = engine.State.Cards[cardId];
        card.Zone = CardZone.DrawPile;
        card.OwnerSeatId = 0;
        card.IsFaceUp = false;
    }

    private static void RemoveDefinitionFromAllHands(GameEngine engine, string definitionId)
    {
        foreach (PlayerState player in engine.State.Players.Values)
        {
            foreach (string cardId in player.Hand.Where(cardId => engine.State.Cards[cardId].DefinitionId == definitionId).ToArray())
            {
                MoveToDrawPile(engine, cardId);
            }
        }
    }

    private static IEnumerable<int> SeatOrderAfter(GameEngine engine, int sourceSeatId)
    {
        int start = engine.State.TurnOrder.IndexOf(sourceSeatId);
        for (int offset = 0; offset < engine.State.TurnOrder.Count; offset++)
        {
            yield return engine.State.TurnOrder[(start + offset) % engine.State.TurnOrder.Count];
        }
    }

    private static bool IsPhysicalSlash(CardState card) => card.DefinitionId == "slash";
}
