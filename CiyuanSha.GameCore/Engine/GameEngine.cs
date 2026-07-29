using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Determinism;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Events;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.GameCore.Skills;

namespace CiyuanSha.GameCore.Engine;

public sealed class GameEngine
{
    private readonly MatchConfig _config;
    private readonly ContentRegistry _content;
    private readonly IGameMode _mode;
    private readonly DeterministicRandom _random;
    private readonly ResolutionStack _resolutionStack = new();
    private readonly List<RuleEvent> _stepEvents = new();
    private PendingOperation? _pendingOperation;
    private long _eventSequence;
    private long _choiceSequence;

    private GameEngine(MatchConfig config, ContentRegistry content, IGameMode mode)
    {
        _config = config;
        _content = content;
        _mode = mode;
        _random = new DeterministicRandom(config.Seed);
        State = new GameState
        {
            MatchId = config.MatchId,
            Seed = config.Seed,
            RandomState = _random.State,
            ModeId = config.ModeId
        };
        Journal = new RuleJournal();
    }

    public GameState State { get; }

    public RuleJournal Journal { get; }

    public static GameEngine Start(
        MatchConfig config,
        ContentRegistry content,
        GameModeRegistry? modes = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(content);
        GameModeRegistry registry = modes ?? new GameModeRegistry();
        IGameMode mode = registry.Get(config.ModeId);
        ModeValidationResult validation = mode.Validate(config, content);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Match configuration is invalid: {validation.ErrorKey}");
        }

        GameEngine engine = new(config, content, mode);
        engine.Initialize();
        return engine;
    }

    public EngineStepResult Advance(ChoiceResult? result = null) =>
        Advance(State.PendingChoice?.ActingSeatId ?? 0, result);

    public EngineStepResult Advance(int submittingSeatId, ChoiceResult? result)
    {
        _stepEvents.Clear();
        if (State.Status == MatchStatus.Completed)
        {
            return BuildResult(EngineProgress.MatchCompleted);
        }
        if (State.Status == MatchStatus.Faulted)
        {
            return BuildResult(EngineProgress.Faulted, "engine.match_faulted");
        }

        try
        {
            if (State.PendingChoice is not null)
            {
                if (result is null)
                {
                    return BuildResult(EngineProgress.WaitingForChoice);
                }

                ChoiceValidationResult validation = State.PendingChoice.Validate(result, submittingSeatId, State.Revision);
                if (!validation.IsValid)
                {
                    return BuildResult(EngineProgress.Rejected, validation.ErrorKey);
                }

                AcceptChoice(result);
            }
            else if (result is not null)
            {
                return BuildResult(EngineProgress.Rejected, "choice.no_request_pending");
            }

            RunAutomaticFlow();
            if (State.Status == MatchStatus.Completed)
            {
                return BuildResult(EngineProgress.MatchCompleted);
            }

            return BuildResult(State.PendingChoice is null ? EngineProgress.Progressed : EngineProgress.WaitingForChoice);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            State.Status = MatchStatus.Faulted;
            State.ResultMessage = exception.Message;
            CommitMutation();
            Emit(RuleEventKind.StateFaulted, 0, Array.Empty<int>(), new TextRuleEventPayload("engine.fault", new Dictionary<string, string>
            {
                ["message"] = exception.Message
            }));
            Journal.Append(JournalEntryKind.Fault, State.ComputeCanonicalHash(), message: exception.ToString());
            return BuildResult(EngineProgress.Faulted, "engine.unhandled_fault");
        }
    }

    public GameView BuildView(ViewerContext viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        bool omniscient = viewer.Role == ViewerRole.OmniscientReplay;
        int viewerSeatId = viewer.SeatId ?? 0;
        List<PlayerView> players = new();
        foreach (PlayerState player in State.Players.Values.OrderBy(player => player.SeatId))
        {
            bool roleVisible = _mode.IsRoleVisibleTo(State, player.SeatId, viewer);
            players.Add(new PlayerView(
                player.SeatId,
                player.DisplayName,
                player.GeneralId,
                roleVisible ? player.Role : IdentityRole.None,
                roleVisible,
                roleVisible ? player.Team : TeamSide.None,
                player.Controller,
                player.Health,
                player.MaxHealth,
                player.IsAlive,
                player.IsChained,
                player.Hand.Count,
                new Dictionary<EquipmentSlot, string>(player.Equipment),
                player.JudgementArea.ToArray(),
                new Dictionary<string, int>(player.Marks),
                player.SkillIds.ToArray()));
        }

        List<CardView> publicCards = State.Cards.Values
            .Where(card => card.Zone is CardZone.Equipment or CardZone.Judgement or CardZone.Processing or CardZone.DiscardPile)
            .Select(card => new CardView(card.InstanceId, card.DefinitionId, card.Suit, card.Rank, card.Zone, card.OwnerSeatId))
            .ToList();
        IReadOnlyList<string> privateHand = omniscient
            ? State.Players.Values.SelectMany(player => player.Hand).ToArray()
            : State.Players.TryGetValue(viewerSeatId, out PlayerState? viewerPlayer)
                ? viewerPlayer.Hand.ToArray()
                : Array.Empty<string>();

        return new GameView
        {
            MatchId = State.MatchId,
            Revision = State.Revision,
            ModeId = State.ModeId,
            Status = State.Status,
            Phase = State.Phase,
            CurrentSeatId = State.CurrentSeatId,
            RoundNumber = State.RoundNumber,
            Players = players,
            PublicCards = publicCards,
            PrivateHandCardIds = privateHand,
            DrawPileCount = State.DrawPile.Count,
            DiscardPileCount = State.DiscardPile.Count,
            PendingChoice = State.PendingChoice?.RedactFor(viewer),
            ContentPacks = State.ContentPacks.ToArray(),
            WinnerSeatIds = State.WinnerSeatIds.ToArray(),
            ResultMessage = State.ResultMessage,
            StateHash = State.ComputeCanonicalHash()
        };
    }

    public RuleJournalDelta? BuildDelta(long afterCursor) => Journal.TryGetDelta(afterCursor, out RuleJournalDelta delta) ? delta : null;

    private void Initialize()
    {
        foreach (ContentPackReference pack in _config.ContentPacks.OrderBy(pack => pack.PackId, StringComparer.Ordinal))
        {
            if (!_content.Packs.TryGetValue(pack.PackId, out LoadedContentPack? loaded)
                || !string.Equals(pack.Version, loaded.Manifest.Version, StringComparison.Ordinal)
                || !string.Equals(pack.ContentHash, loaded.ComputedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Configured content pack is unavailable or mismatched: {pack.PackId}");
            }
            State.ContentPacks.Add(pack);
        }

        foreach (PlayerConfig playerConfig in _config.Players.OrderBy(player => player.SeatId))
        {
            GeneralDefinitionV2 general = _content.Generals[playerConfig.GeneralId];
            PlayerState player = new()
            {
                SeatId = playerConfig.SeatId,
                PlayerId = playerConfig.PlayerId,
                DisplayName = playerConfig.DisplayName,
                GeneralId = playerConfig.GeneralId,
                Controller = playerConfig.Controller,
                Health = general.MaximumHealth,
                MaxHealth = general.MaximumHealth
            };
            foreach (string skillId in general.SkillIds)
            {
                if (!_content.Skills.ContainsKey(skillId))
                {
                    throw new InvalidOperationException($"General {general.GeneralId} references unknown skill {skillId}.");
                }
                player.SkillIds.Add(skillId);
            }
            State.Players.Add(player.SeatId, player);
            State.TurnOrder.Add(player.SeatId);
        }

        CreateDeck(_content.Decks[_config.DeckId]);
        _mode.Setup(State, _config, _content, _random);
        State.Status = MatchStatus.Running;
        State.Phase = GamePhase.Preparation;
        State.RoundNumber = 1;
        DealOpeningHands();
        CommitMutation();
        Emit(RuleEventKind.MatchStarted, State.CurrentSeatId, State.TurnOrder, EmptyRuleEventPayload.Instance);
        Emit(RuleEventKind.TurnStarted, State.CurrentSeatId, new[] { State.CurrentSeatId }, new ValueRuleEventPayload(State.RoundNumber));
        EmitPhaseChanged(GamePhase.Preparation);
        RunAutomaticFlow();
    }

    private void CreateDeck(DeckDefinition deck)
    {
        int serial = 1;
        foreach (DeckCardEntry entry in deck.Cards)
        {
            if (!_content.Cards.ContainsKey(entry.CardId))
            {
                throw new InvalidOperationException($"Deck {deck.DeckId} references unknown card {entry.CardId}.");
            }

            for (int copy = 0; copy < entry.Copies; copy++)
            {
                string instanceId = $"card-{serial++:D4}";
                State.Cards.Add(instanceId, new CardState
                {
                    InstanceId = instanceId,
                    DefinitionId = entry.CardId,
                    Suit = entry.Suit,
                    Rank = entry.Rank,
                    Zone = CardZone.DrawPile
                });
                State.DrawPile.Add(instanceId);
            }
        }

        for (int index = State.DrawPile.Count - 1; index > 0; index--)
        {
            ulong randomValue = ConsumeRandom();
            int other = (int)(randomValue % (uint)(index + 1));
            (State.DrawPile[index], State.DrawPile[other]) = (State.DrawPile[other], State.DrawPile[index]);
        }
    }

    private void DealOpeningHands()
    {
        foreach (PlayerState player in State.Players.Values.OrderBy(player => player.SeatId))
        {
            DrawCards(player, 4);
        }
    }

    private void RunAutomaticFlow()
    {
        int guard = 0;
        while (State.Status == MatchStatus.Running && State.PendingChoice is null)
        {
            if (++guard > 128)
            {
                throw new InvalidOperationException("Automatic phase flow exceeded its safety limit.");
            }

            PlayerState current = State.Players[State.CurrentSeatId];
            if (!current.IsAlive)
            {
                AdvanceTurn();
                continue;
            }

            switch (State.Phase)
            {
                case GamePhase.Preparation:
                    ChangePhase(GamePhase.Judgement);
                    break;
                case GamePhase.Judgement:
                    ResolveJudgementArea(current);
                    if (State.PendingChoice is null)
                    {
                        ChangePhase(GamePhase.Draw);
                    }
                    break;
                case GamePhase.Draw:
                    int drawCount = 2;
                    if (HasSkill(current, "bonus_draw"))
                    {
                        drawCount++;
                    }
                    if (HasSkill(current, "boss_pressure"))
                    {
                        drawCount++;
                    }
                    DrawCards(current, drawCount);
                    ChangePhase(GamePhase.Play);
                    break;
                case GamePhase.Play:
                    RequestPlayAction(current);
                    break;
                case GamePhase.Discard:
                    RequestDiscardIfNeeded(current);
                    if (State.PendingChoice is null)
                    {
                        ChangePhase(GamePhase.End);
                    }
                    break;
                case GamePhase.End:
                    AdvanceTurn();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported phase: {State.Phase}");
            }
        }
    }

    private void AcceptChoice(ChoiceResult result)
    {
        ChoiceRequest acceptedRequest = State.PendingChoice!;
        PendingOperation operation = _pendingOperation ?? throw new InvalidOperationException("Pending choice has no operation.");
        Journal.Append(JournalEntryKind.ChoiceAccepted, State.ComputeCanonicalHash(), choiceResult: result);
        State.PendingChoice = null;
        _pendingOperation = null;
        CommitMutation();
        Emit(RuleEventKind.ChoiceAccepted, acceptedRequest.ActingSeatId, new[] { acceptedRequest.ActingSeatId }, new TextRuleEventPayload("choice.accepted"));

        if (result.IsCancelled)
        {
            HandleCancelledChoice(operation);
            return;
        }

        switch (operation.Kind)
        {
            case PendingOperationKind.PlayAction:
                ResolvePlayAction(operation.SourceSeatId, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.SelectCardTarget:
                ResolveCardTarget(operation, result.SelectedOptionIds.Select(ParseSeatOption).ToArray());
                break;
            case PendingOperationKind.RespondDodge:
                ResolveDodgeResponse(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.RespondSlash:
            case PendingOperationKind.DuelResponse:
                ResolveSlashResponse(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.DiscardToLimit:
                foreach (string optionId in result.SelectedOptionIds)
                {
                    DiscardFromHand(operation.SourceSeatId, ParseCardOption(optionId));
                }
                ChangePhase(GamePhase.End);
                break;
            case PendingOperationKind.SkillCost:
                ResolveSkillAction(operation, result);
                break;
            case PendingOperationKind.SelectSlashNature:
                ResolveSlashNature(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.DyingRescue:
                ResolveDyingRescue(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.SkillTarget:
                ResolveSkillTarget(operation, ParseSeatOption(result.SelectedOptionIds.Single()));
                break;
            case PendingOperationKind.JudgementReplacement:
                ResolveJudgementReplacement(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.SelectTargetCard:
                ResolveTargetCardSelection(operation, ParseCardOption(result.SelectedOptionIds.Single()));
                break;
            case PendingOperationKind.HarvestPick:
                ResolveHarvestPick(operation, ParseCardOption(result.SelectedOptionIds.Single()));
                break;
            case PendingOperationKind.NullificationResponse:
                ResolveNullificationResponse(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.FireAttackReveal:
                ResolveFireAttackReveal(operation, ParseCardOption(result.SelectedOptionIds.Single()));
                break;
            case PendingOperationKind.FireAttackDiscard:
                ResolveFireAttackDiscard(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.BorrowSwordVictim:
                ResolveBorrowSwordVictim(operation, ParseSeatOption(result.SelectedOptionIds.Single()));
                break;
            case PendingOperationKind.BorrowSwordSlash:
                ResolveBorrowSwordSlash(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.DoubleSwordsChoice:
                ResolveDoubleSwordsChoice(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.GreenDragonFollowUp:
                ResolveGreenDragonFollowUp(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.StoneAxeCost:
                ResolveStoneAxeCost(operation, result.SelectedOptionIds);
                break;
            case PendingOperationKind.IceSwordCards:
                ResolveIceSwordCards(operation, result.SelectedOptionIds);
                break;
            case PendingOperationKind.KylinMountChoice:
                ResolveKylinMountChoice(operation, result.SelectedOptionIds.Single());
                break;
            case PendingOperationKind.SerpentSpearPlayCost:
                ResolveSerpentSpearPlayCost(operation, result.SelectedOptionIds);
                break;
            case PendingOperationKind.SerpentSpearResponseCost:
                ResolveSerpentSpearResponseCost(operation, result.SelectedOptionIds);
                break;
            default:
                throw new InvalidOperationException($"Unsupported pending operation: {operation.Kind}");
        }
    }

    private void RequestPlayAction(PlayerState player)
    {
        List<ChoiceOption> options = new();
        foreach (string cardId in player.Hand.OrderBy(value => value, StringComparer.Ordinal))
        {
            CardState card = State.Cards[cardId];
            CardDefinition definition = _content.Cards[card.DefinitionId];
            if (CanUseCard(player, definition))
            {
                options.Add(new ChoiceOption(
                    $"action:use:{cardId}",
                    ChoiceOptionKind.Card,
                    definition.DisplayName,
                    cardId,
                    AiValue: definition.AiValues?.GetValueOrDefault("use") ?? 0));
            }
            if (definition.CanRecast)
            {
                options.Add(new ChoiceOption($"action:recast:{cardId}", ChoiceOptionKind.Action, "action.recast", cardId, AiValue: 0.5));
            }
        }

        if (HasEquippedCard(player, "serpent_spear")
            && player.Hand.Count >= 2
            && (player.SlashUsesThisTurn == 0 || HasEquippedCard(player, "crossbow"))
            && State.Players.Values.Any(target => target.IsAlive
                && target.SeatId != player.SeatId
                && EffectiveDistance(player.SeatId, target.SeatId) <= AttackRange(player)))
        {
            options.Add(new ChoiceOption(
                "action:serpent_spear",
                ChoiceOptionKind.Skill,
                "equipment.serpent_spear.virtual_slash",
                "serpent_spear",
                AiValue: 4));
        }

        foreach (string skillId in player.SkillIds)
        {
            SkillDefinitionV2 skill = _content.Skills[skillId];
            if ((skill.Tags & SkillTags.Active) != 0 && CanUseActiveSkill(player, skill))
            {
                options.Add(new ChoiceOption($"action:skill:{skillId}", ChoiceOptionKind.Skill, skill.DisplayName, skillId, AiValue: skill.Ai?.Order ?? 0));
            }
        }
        options.Add(new ChoiceOption("action:end_play", ChoiceOptionKind.Action, "action.end_play", AiValue: -0.1));
        SetChoice(player.SeatId, ChoiceKind.SelectAction, "phase.play.choose_action", options, PendingOperationKind.PlayAction);
    }

    private void ResolvePlayAction(int sourceSeatId, string optionId)
    {
        if (string.Equals(optionId, "action:end_play", StringComparison.Ordinal))
        {
            ChangePhase(GamePhase.Discard);
            return;
        }

        if (optionId.StartsWith("action:recast:", StringComparison.Ordinal))
        {
            string cardId = optionId["action:recast:".Length..];
            DiscardFromHand(sourceSeatId, cardId);
            DrawCards(State.Players[sourceSeatId], 1);
            return;
        }

        if (optionId.StartsWith("action:skill:", StringComparison.Ordinal))
        {
            string skillId = optionId["action:skill:".Length..];
            BeginSkillAction(sourceSeatId, skillId);
            return;
        }

        if (string.Equals(optionId, "action:serpent_spear", StringComparison.Ordinal))
        {
            RequestSerpentSpearPlayCost(sourceSeatId);
            return;
        }

        if (!optionId.StartsWith("action:use:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported play option: {optionId}");
        }

        string cardInstanceId = optionId["action:use:".Length..];
        BeginCardUse(sourceSeatId, cardInstanceId);
    }

    private void BeginCardUse(int sourceSeatId, string cardInstanceId)
    {
        PlayerState source = State.Players[sourceSeatId];
        if (!source.Hand.Contains(cardInstanceId))
        {
            throw new InvalidOperationException("Selected card is not in the acting player's hand.");
        }

        CardDefinition definition = _content.Cards[State.Cards[cardInstanceId].DefinitionId];
        switch (definition.EffectId)
        {
            case "card.peach":
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                Heal(sourceSeatId, sourceSeatId, 1);
                FinishUsedCard(cardInstanceId);
                break;
            case "card.wine":
                if (source.WineUsesThisTurn > 0)
                {
                    throw new InvalidOperationException("Wine can only be used once per turn.");
                }
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                source.WineUsesThisTurn++;
                source.Marks["wine_damage"] = 1;
                CommitMutation();
                FinishUsedCard(cardInstanceId);
                break;
            case "card.ex_nihilo":
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                Emit(RuleEventKind.CardUsed, sourceSeatId, new[] { sourceSeatId }, new CardMoveRuleEventPayload(
                    cardInstanceId, CardZone.Hand, CardZone.Processing, sourceSeatId));
                BeginNullificationChain(new PendingOperation
                {
                    SourceSeatId = sourceSeatId,
                    TargetSeatId = sourceSeatId,
                    CardInstanceId = cardInstanceId
                }, new[] { sourceSeatId });
                break;
            case "card.peach_garden":
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                BeginSequentialTargetEffect(
                    sourceSeatId,
                    cardInstanceId,
                    "card.peach_garden",
                    AlivePlayersInSeatOrder(sourceSeatId).Select(player => player.SeatId));
                break;
            case "card.harvest":
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                BeginHarvest(sourceSeatId, cardInstanceId);
                break;
            case "card.barbarians":
            case "card.arrow_barrage":
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                BeginMassResponse(sourceSeatId, cardInstanceId, definition.EffectId);
                break;
            case "card.equipment":
                Equip(sourceSeatId, cardInstanceId, definition);
                break;
            case "card.slash":
                if (HasEquippedCard(source, "vermilion_fan"))
                {
                    SetChoice(
                        sourceSeatId,
                        ChoiceKind.SelectControl,
                        "equipment.vermilion_fan.choose_nature",
                        new[]
                        {
                            new ChoiceOption("nature:physical", ChoiceOptionKind.Control, "damage.physical", AiValue: 0),
                            new ChoiceOption("nature:fire", ChoiceOptionKind.Control, "damage.fire", AiValue: 1)
                        },
                        PendingOperationKind.SelectSlashNature,
                        operation: new PendingOperation
                        {
                            Kind = PendingOperationKind.SelectSlashNature,
                            SourceSeatId = sourceSeatId,
                            CardInstanceId = cardInstanceId
                        });
                    break;
                }
                RequestCardTargets(sourceSeatId, cardInstanceId, definition);
                break;
            case "card.iron_chain":
            case "card.fire_slash":
            case "card.thunder_slash":
            case "card.duel":
            case "card.dismantle":
            case "card.snatch":
            case "card.fire_attack":
            case "card.borrow_sword":
            case "card.indulgence":
            case "card.supply_shortage":
                RequestCardTargets(sourceSeatId, cardInstanceId, definition);
                break;
            case "card.lightning":
                MoveHandCardToJudgement(sourceSeatId, sourceSeatId, cardInstanceId);
                break;
            default:
                MoveHandCardToProcessing(sourceSeatId, cardInstanceId);
                FinishUsedCard(cardInstanceId);
                break;
        }
    }

    private void ResolveSlashNature(PendingOperation operation, string optionId)
    {
        operation.DamageNatureOverride = string.Equals(optionId, "nature:fire", StringComparison.Ordinal)
            ? DamageNature.Fire
            : DamageNature.Physical;
        CardDefinition definition = _content.Cards[State.Cards[operation.CardInstanceId].DefinitionId];
        RequestCardTargets(operation.SourceSeatId, operation.CardInstanceId, definition, operation.DamageNatureOverride);
    }

    private void RequestCardTargets(
        int sourceSeatId,
        string cardInstanceId,
        CardDefinition definition,
        DamageNature? natureOverride = null)
    {
        List<ChoiceOption> targets = State.Players.Values
            .Where(player => player.IsAlive
                && (definition.EffectId is "card.iron_chain" or "card.fire_attack" || player.SeatId != sourceSeatId)
                && CanCardTarget(sourceSeatId, player, definition))
            .OrderBy(player => EffectiveDistance(sourceSeatId, player.SeatId))
            .ThenBy(player => player.SeatId)
            .Select(player => new ChoiceOption(
                $"seat:{player.SeatId}",
                ChoiceOptionKind.Player,
                "target.player",
                player.SeatId.ToString(),
                AiValue: -_mode.GetAttitude(State, sourceSeatId, player.SeatId)))
            .ToList();
        bool isSlash = definition.EffectId is "card.slash" or "card.fire_slash" or "card.thunder_slash";
        bool fangtianMultiTarget = isSlash
            && State.Players[sourceSeatId].Hand.Count == 1
            && HasEquippedCard(State.Players[sourceSeatId], "fangtian_halberd");
        int maximum = definition.EffectId == "card.iron_chain"
            ? Math.Min(2, targets.Count)
            : fangtianMultiTarget
                ? Math.Min(3, targets.Count)
                : 1;
        SetChoice(
            sourceSeatId,
            ChoiceKind.SelectTarget,
            "card.choose_target",
            targets,
            PendingOperationKind.SelectCardTarget,
            minimum: 1,
            maximum: maximum,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SelectCardTarget,
                SourceSeatId = sourceSeatId,
                CardInstanceId = cardInstanceId,
                DamageNatureOverride = natureOverride
            });
    }

    private void ResolveCardTarget(PendingOperation operation, IReadOnlyList<int> targets)
    {
        CardState card = State.Cards[operation.CardInstanceId];
        CardDefinition definition = _content.Cards[card.DefinitionId];
        CardZone fromZone = card.Zone;
        if (fromZone == CardZone.Hand)
        {
            MoveHandCardToProcessing(operation.SourceSeatId, operation.CardInstanceId);
        }
        else if (fromZone != CardZone.Processing)
        {
            throw new InvalidOperationException("Used card is not in hand or processing area.");
        }
        Emit(RuleEventKind.CardUsed, operation.SourceSeatId, targets, new CardMoveRuleEventPayload(
            operation.CardInstanceId,
            fromZone,
            CardZone.Processing,
            operation.SourceSeatId));

        if (IsNullifiable(definition))
        {
            if (definition.EffectId == "card.iron_chain")
            {
                BeginSequentialTargetEffect(
                    operation.SourceSeatId,
                    operation.CardInstanceId,
                    definition.EffectId,
                    targets,
                    emitCardUsed: false);
                return;
            }
            BeginNullificationChain(operation, targets);
            return;
        }

        switch (definition.EffectId)
        {
            case "card.slash":
            case "card.fire_slash":
            case "card.thunder_slash":
                if (State.Players[operation.SourceSeatId].SlashUsesThisTurn > 0
                    && !HasEquippedCard(State.Players[operation.SourceSeatId], "crossbow"))
                {
                    throw new InvalidOperationException("Slash can only be used once in the play phase without a modifier.");
                }
                State.Players[operation.SourceSeatId].SlashUsesThisTurn++;
                int slashDamage = 1 + State.Players[operation.SourceSeatId].Marks.GetValueOrDefault("wine_damage");
                State.Players[operation.SourceSeatId].Marks.Remove("wine_damage");
                CommitMutation();
                DamageNature slashNature = operation.DamageNatureOverride ?? definition.EffectId switch
                {
                    "card.fire_slash" => DamageNature.Fire,
                    "card.thunder_slash" => DamageNature.Thunder,
                    _ => DamageNature.Physical
                };
                BeginSequentialTargetEffect(
                    operation.SourceSeatId,
                    operation.CardInstanceId,
                    definition.EffectId,
                    targets,
                    emitCardUsed: false,
                    requiresNullification: false,
                    damageNature: slashNature,
                    damageAmount: slashDamage);
                break;
            case "card.duel":
                RequestDuelSlash(operation.SourceSeatId, targets[0], operation.SourceSeatId, operation.CardInstanceId);
                break;
            case "card.iron_chain":
                foreach (int target in targets)
                {
                    State.Players[target].IsChained = !State.Players[target].IsChained;
                }
                CommitMutation();
                FinishUsedCard(operation.CardInstanceId);
                break;
            case "card.indulgence":
            case "card.supply_shortage":
                MoveProcessingCardToJudgement(targets[0], operation.CardInstanceId);
                break;
            case "card.dismantle":
                RequestTargetCardSelection(operation.SourceSeatId, targets[0], operation.CardInstanceId, steal: false);
                break;
            case "card.snatch":
                RequestTargetCardSelection(operation.SourceSeatId, targets[0], operation.CardInstanceId, steal: true);
                break;
            case "card.fire_attack":
                BeginFireAttack(operation.SourceSeatId, targets[0], operation.CardInstanceId);
                break;
            case "card.borrow_sword":
                BeginBorrowSword(operation.SourceSeatId, targets[0], operation.CardInstanceId);
                break;
            default:
                FinishUsedCard(operation.CardInstanceId);
                break;
        }
    }

    private bool IsNullifiable(CardDefinition definition) =>
        definition.Category is CardCategory.Trick or CardCategory.DelayedTrick
        && definition.EffectId != "card.nullification";

    private void BeginNullificationChain(PendingOperation original, IReadOnlyList<int> targets)
    {
        if (targets.Count != 1)
        {
            throw new InvalidOperationException("Nullification chains resolve exactly one target at a time.");
        }
        PendingOperation operation = new()
        {
            Kind = PendingOperationKind.NullificationResponse,
            SourceSeatId = original.SourceSeatId,
            TargetSeatId = targets.FirstOrDefault(),
            CardInstanceId = original.CardInstanceId,
            DamageNatureOverride = original.DamageNatureOverride,
            Continuation = original.Continuation
        };
        foreach (int target in targets)
        {
            operation.EffectTargets.Enqueue(target);
        }
        FillNullificationResponders(operation, original.SourceSeatId);
        ContinueNullificationChain(operation);
    }

    private void FillNullificationResponders(PendingOperation operation, int startingSeatId)
    {
        operation.RemainingTargets.Clear();
        foreach (PlayerState player in AlivePlayersInSeatOrder(startingSeatId).Where(player => player.Hand.Any(cardId =>
            _content.Cards[State.Cards[cardId].DefinitionId].EffectId == "card.nullification")))
        {
            operation.RemainingTargets.Enqueue(player.SeatId);
        }
    }

    private void ContinueNullificationChain(PendingOperation operation)
    {
        if (operation.RemainingTargets.Count == 0)
        {
            if (operation.OtherSeatId % 2 == 1)
            {
                Emit(RuleEventKind.CardCancelled, operation.SourceSeatId, operation.EffectTargets, new TextRuleEventPayload("card.nullified"));
                if (operation.Continuation is not null)
                {
                    ContinueSequentialTargetEffect(operation.Continuation);
                }
                else
                {
                    FinishUsedCard(operation.CardInstanceId);
                }
            }
            else
            {
                ResolveCardAfterNullification(operation);
            }
            return;
        }
        int responderSeatId = operation.RemainingTargets.Dequeue();
        PlayerState responder = State.Players[responderSeatId];
        List<ChoiceOption> options = responder.Hand
            .Where(cardId => _content.Cards[State.Cards[cardId].DefinitionId].EffectId == "card.nullification")
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "card.nullification", cardId, AiValue: operation.OtherSeatId % 2 == 0 ? 4 : -4))
            .ToList();
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: 0));
        SetChoice(
            responderSeatId,
            ChoiceKind.UseOrRespond,
            "response.nullification",
            options,
            PendingOperationKind.NullificationResponse,
            operation: operation);
    }

    private void ResolveNullificationResponse(PendingOperation operation, string optionId)
    {
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            string cardId = ParseCardOption(optionId);
            int responderSeatId = State.Cards[cardId].OwnerSeatId;
            DiscardFromHand(responderSeatId, cardId);
            operation.OtherSeatId++;
            Emit(RuleEventKind.CardUsed, responderSeatId, operation.EffectTargets, new TextRuleEventPayload("card.nullification"));
            FillNullificationResponders(operation, responderSeatId);
        }
        ContinueNullificationChain(operation);
    }

    private void ResolveCardAfterNullification(PendingOperation operation)
    {
        int[] targets = operation.EffectTargets.ToArray();
        if (operation.Continuation is not null)
        {
            ResolveSequentialTargetEffect(operation.Continuation, targets[0]);
            return;
        }
        string effectId = _content.Cards[State.Cards[operation.CardInstanceId].DefinitionId].EffectId;
        switch (effectId)
        {
            case "card.duel":
                RequestDuelSlash(operation.SourceSeatId, targets[0], operation.SourceSeatId, operation.CardInstanceId);
                break;
            case "card.iron_chain":
                foreach (int target in targets)
                {
                    State.Players[target].IsChained = !State.Players[target].IsChained;
                }
                CommitMutation();
                FinishUsedCard(operation.CardInstanceId);
                break;
            case "card.indulgence":
            case "card.supply_shortage":
                MoveProcessingCardToJudgement(targets[0], operation.CardInstanceId);
                break;
            case "card.dismantle":
                RequestTargetCardSelection(operation.SourceSeatId, targets[0], operation.CardInstanceId, steal: false);
                break;
            case "card.snatch":
                RequestTargetCardSelection(operation.SourceSeatId, targets[0], operation.CardInstanceId, steal: true);
                break;
            case "card.fire_attack":
                BeginFireAttack(operation.SourceSeatId, targets[0], operation.CardInstanceId);
                break;
            case "card.borrow_sword":
                BeginBorrowSword(operation.SourceSeatId, targets[0], operation.CardInstanceId);
                break;
            case "card.ex_nihilo":
                DrawCards(State.Players[operation.SourceSeatId], 2);
                FinishUsedCard(operation.CardInstanceId);
                break;
            default:
                FinishUsedCard(operation.CardInstanceId);
                break;
        }
    }

    private void BeginSequentialTargetEffect(
        int sourceSeatId,
        string cardInstanceId,
        string effectId,
        IEnumerable<int> targetSeatIds,
        bool emitCardUsed = true,
        bool requiresNullification = true,
        DamageNature? damageNature = null,
        int damageAmount = 0)
    {
        PendingOperation sequence = new()
        {
            Kind = PendingOperationKind.SequentialTargetEffect,
            SourceSeatId = sourceSeatId,
            CardInstanceId = cardInstanceId,
            EffectId = effectId,
            RequiresNullification = requiresNullification,
            DamageNatureOverride = damageNature,
            DamageAmount = damageAmount
        };
        foreach (int targetSeatId in targetSeatIds.Distinct())
        {
            sequence.RemainingTargets.Enqueue(targetSeatId);
        }
        if (emitCardUsed)
        {
            Emit(RuleEventKind.CardUsed, sourceSeatId, sequence.RemainingTargets, new CardMoveRuleEventPayload(
                cardInstanceId,
                CardZone.Hand,
                CardZone.Processing,
                sourceSeatId));
        }
        ContinueSequentialTargetEffect(sequence);
    }

    private void ContinueSequentialTargetEffect(PendingOperation sequence)
    {
        while (sequence.RemainingTargets.Count > 0)
        {
            int targetSeatId = sequence.RemainingTargets.Dequeue();
            if (!State.Players.TryGetValue(targetSeatId, out PlayerState? target) || !target.IsAlive)
            {
                continue;
            }
            sequence.TargetSeatId = targetSeatId;
            if (!sequence.RequiresNullification)
            {
                ResolveSequentialTargetEffect(sequence, targetSeatId);
                return;
            }
            BeginNullificationChain(new PendingOperation
            {
                SourceSeatId = sequence.SourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = sequence.CardInstanceId,
                Continuation = sequence
            }, new[] { targetSeatId });
            return;
        }

        if (sequence.EffectId == "card.harvest")
        {
            foreach (string remaining in State.ProcessingArea
                .Where(cardId => cardId != sequence.CardInstanceId)
                .ToArray())
            {
                MoveProcessingCardToDiscard(remaining);
            }
        }
        FinishUsedCard(sequence.CardInstanceId);
    }

    private void ResolveSequentialTargetEffect(PendingOperation sequence, int targetSeatId)
    {
        switch (sequence.EffectId)
        {
            case "card.slash":
            case "card.fire_slash":
            case "card.thunder_slash":
                if (ShouldTriggerDoubleSwords(sequence.SourceSeatId, targetSeatId))
                {
                    RequestDoubleSwordsChoice(sequence, targetSeatId);
                }
                else
                {
                    RequestSequenceDodge(sequence, targetSeatId);
                }
                break;
            case "card.iron_chain":
                State.Players[targetSeatId].IsChained = !State.Players[targetSeatId].IsChained;
                CommitMutation();
                ContinueSequentialTargetEffect(sequence);
                break;
            case "card.peach_garden":
                Heal(sequence.SourceSeatId, targetSeatId, 1);
                ContinueSequentialTargetEffect(sequence);
                break;
            case "card.barbarians":
                if (HasEquippedCard(State.Players[targetSeatId], "vine_armor"))
                {
                    Emit(RuleEventKind.CardCancelled, targetSeatId, new[] { targetSeatId }, new TextRuleEventPayload("equipment.vine_armor.mass_immunity"));
                    ContinueSequentialTargetEffect(sequence);
                }
                else
                {
                    RequestMassResponse(sequence, targetSeatId, requiresSlash: true);
                }
                break;
            case "card.arrow_barrage":
                if (HasEquippedCard(State.Players[targetSeatId], "vine_armor"))
                {
                    Emit(RuleEventKind.CardCancelled, targetSeatId, new[] { targetSeatId }, new TextRuleEventPayload("equipment.vine_armor.mass_immunity"));
                    ContinueSequentialTargetEffect(sequence);
                }
                else
                {
                    RequestMassResponse(sequence, targetSeatId, requiresSlash: false);
                }
                break;
            case "card.harvest":
                RequestHarvestPick(sequence, targetSeatId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported sequential card effect: {sequence.EffectId}");
        }
    }

    private void BeginFireAttack(int sourceSeatId, int targetSeatId, string fireAttackCardId)
    {
        PlayerState target = State.Players[targetSeatId];
        if (target.Hand.Count == 0)
        {
            FinishUsedCard(fireAttackCardId);
            return;
        }
        List<ChoiceOption> options = target.Hand
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "card.fire_attack.reveal_card",
                cardId,
                AiValue: -CardUseValue(cardId)))
            .ToList();
        SetChoice(
            targetSeatId,
            ChoiceKind.SelectCard,
            "card.fire_attack.choose_reveal",
            options,
            PendingOperationKind.FireAttackReveal,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.FireAttackReveal,
                SourceSeatId = sourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = fireAttackCardId
            });
    }

    private void ResolveFireAttackReveal(PendingOperation operation, string revealedCardId)
    {
        PlayerState target = State.Players[operation.TargetSeatId];
        if (!target.Hand.Contains(revealedCardId))
        {
            throw new InvalidOperationException("The revealed Fire Attack card is no longer in the target hand.");
        }
        CardSuit suit = State.Cards[revealedCardId].Suit;
        Emit(RuleEventKind.CardRevealed, operation.TargetSeatId, new[] { operation.SourceSeatId }, new TextRuleEventPayload(
            "card.fire_attack.revealed",
            new Dictionary<string, string>
            {
                ["cardInstanceId"] = revealedCardId,
                ["suit"] = suit.ToString()
            }));

        List<ChoiceOption> options = State.Players[operation.SourceSeatId].Hand
            .Where(cardId => State.Cards[cardId].Suit == suit)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "card.fire_attack.discard_matching",
                cardId,
                AiValue: -CardUseValue(cardId)))
            .ToList();
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: 0));
        SetChoice(
            operation.SourceSeatId,
            ChoiceKind.Discard,
            "card.fire_attack.choose_discard",
            options,
            PendingOperationKind.FireAttackDiscard,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.FireAttackDiscard,
                SourceSeatId = operation.SourceSeatId,
                TargetSeatId = operation.TargetSeatId,
                CardInstanceId = operation.CardInstanceId,
                OtherCardInstanceId = revealedCardId
            });
    }

    private void ResolveFireAttackDiscard(PendingOperation operation, string optionId)
    {
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            string discardCardId = ParseCardOption(optionId);
            if (State.Cards[discardCardId].Suit != State.Cards[operation.OtherCardInstanceId].Suit)
            {
                throw new InvalidOperationException("Fire Attack requires discarding a card of the revealed suit.");
            }
            DiscardFromHand(operation.SourceSeatId, discardCardId);
            Damage(operation.SourceSeatId, operation.TargetSeatId, 1, DamageNature.Fire, operation.CardInstanceId);
        }
        FinishUsedCard(operation.CardInstanceId);
    }

    private void BeginBorrowSword(int sourceSeatId, int weaponHolderSeatId, string borrowSwordCardId)
    {
        PlayerState weaponHolder = State.Players[weaponHolderSeatId];
        List<ChoiceOption> victims = State.Players.Values
            .Where(player => player.IsAlive
                && player.SeatId != weaponHolderSeatId
                && EffectiveDistance(weaponHolderSeatId, player.SeatId) <= AttackRange(weaponHolder))
            .OrderBy(player => player.SeatId)
            .Select(player => new ChoiceOption(
                $"seat:{player.SeatId}",
                ChoiceOptionKind.Player,
                "card.borrow_sword.victim",
                player.SeatId.ToString(),
                AiValue: -_mode.GetAttitude(State, sourceSeatId, player.SeatId)))
            .ToList();
        if (victims.Count == 0)
        {
            TransferBorrowedWeapon(sourceSeatId, weaponHolderSeatId);
            FinishUsedCard(borrowSwordCardId);
            return;
        }
        SetChoice(
            sourceSeatId,
            ChoiceKind.SelectTarget,
            "card.borrow_sword.choose_victim",
            victims,
            PendingOperationKind.BorrowSwordVictim,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.BorrowSwordVictim,
                SourceSeatId = sourceSeatId,
                TargetSeatId = weaponHolderSeatId,
                CardInstanceId = borrowSwordCardId
            });
    }

    private void ResolveBorrowSwordVictim(PendingOperation operation, int victimSeatId)
    {
        PlayerState holder = State.Players[operation.TargetSeatId];
        if (!State.Players[victimSeatId].IsAlive
            || victimSeatId == holder.SeatId
            || EffectiveDistance(holder.SeatId, victimSeatId) > AttackRange(holder))
        {
            throw new InvalidOperationException("Borrow Sword victim is no longer a legal Slash target.");
        }
        List<ChoiceOption> options = holder.Hand
            .Where(cardId => IsSlash(State.Cards[cardId]))
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "card.borrow_sword.use_slash",
                cardId,
                AiValue: 4))
            .ToList();
        if (HasEquippedCard(holder, "serpent_spear") && holder.Hand.Count >= 2)
        {
            options.Add(new ChoiceOption("equipment:serpent_spear", ChoiceOptionKind.Skill, "equipment.serpent_spear.virtual_slash", AiValue: 3));
        }
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: -2));
        SetChoice(
            holder.SeatId,
            ChoiceKind.UseOrRespond,
            "card.borrow_sword.request_slash",
            options,
            PendingOperationKind.BorrowSwordSlash,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.BorrowSwordSlash,
                SourceSeatId = operation.SourceSeatId,
                TargetSeatId = holder.SeatId,
                OtherSeatId = victimSeatId,
                CardInstanceId = operation.CardInstanceId
            });
    }

    private void ResolveBorrowSwordSlash(PendingOperation operation, string optionId)
    {
        if (string.Equals(optionId, "equipment:serpent_spear", StringComparison.Ordinal))
        {
            RequestSerpentSpearResponseCost(operation, operation.TargetSeatId);
            return;
        }
        if (!optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            TransferBorrowedWeapon(operation.SourceSeatId, operation.TargetSeatId);
            FinishUsedCard(operation.CardInstanceId);
            return;
        }
        string slashCardId = ParseCardOption(optionId);
        if (!IsSlash(State.Cards[slashCardId]))
        {
            throw new InvalidOperationException("Borrow Sword response must be a Slash.");
        }
        MoveHandCardToProcessing(operation.TargetSeatId, slashCardId);
        Emit(RuleEventKind.CardUsed, operation.TargetSeatId, new[] { operation.OtherSeatId }, new CardMoveRuleEventPayload(
            slashCardId,
            CardZone.Hand,
            CardZone.Processing,
            operation.TargetSeatId));
        CardDefinition slash = _content.Cards[State.Cards[slashCardId].DefinitionId];
        DamageNature nature = slash.EffectId switch
        {
            "card.fire_slash" => DamageNature.Fire,
            "card.thunder_slash" => DamageNature.Thunder,
            _ => DamageNature.Physical
        };
        RequestDodge(
            operation.TargetSeatId,
            operation.OtherSeatId,
            slashCardId,
            nature,
            operation.CardInstanceId);
    }

    private void TransferBorrowedWeapon(int sourceSeatId, int weaponHolderSeatId)
    {
        PlayerState holder = State.Players[weaponHolderSeatId];
        if (!holder.Equipment.Remove(EquipmentSlot.Weapon, out string? weaponCardId))
        {
            return;
        }
        State.Players[sourceSeatId].Hand.Add(weaponCardId);
        MoveCard(weaponCardId, CardZone.Hand, sourceSeatId);
        CommitMutation();
        Emit(RuleEventKind.EquipmentChanged, sourceSeatId, new[] { weaponHolderSeatId }, new CardMoveRuleEventPayload(
            weaponCardId,
            CardZone.Equipment,
            CardZone.Hand,
            weaponHolderSeatId,
            sourceSeatId));
    }

    private bool ShouldTriggerDoubleSwords(int sourceSeatId, int targetSeatId)
    {
        if (!HasEquippedCard(State.Players[sourceSeatId], "double_swords"))
        {
            return false;
        }
        string sourceGender = _content.Generals[State.Players[sourceSeatId].GeneralId].Gender;
        string targetGender = _content.Generals[State.Players[targetSeatId].GeneralId].Gender;
        return !string.IsNullOrWhiteSpace(sourceGender)
            && !string.IsNullOrWhiteSpace(targetGender)
            && !string.Equals(sourceGender, targetGender, StringComparison.OrdinalIgnoreCase);
    }

    private void RequestSerpentSpearPlayCost(int sourceSeatId)
    {
        List<ChoiceOption> options = State.Players[sourceSeatId].Hand
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "equipment.serpent_spear.cost",
                cardId,
                AiValue: -CardUseValue(cardId)))
            .ToList();
        SetChoice(
            sourceSeatId,
            ChoiceKind.SelectCard,
            "equipment.serpent_spear.choose_two",
            options,
            PendingOperationKind.SerpentSpearPlayCost,
            minimum: 2,
            maximum: 2,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SerpentSpearPlayCost,
                SourceSeatId = sourceSeatId
            });
    }

    private void ResolveSerpentSpearPlayCost(PendingOperation operation, IReadOnlyList<string> optionIds)
    {
        foreach (string optionId in optionIds)
        {
            DiscardFromHand(operation.SourceSeatId, ParseCardOption(optionId));
        }
        string virtualSlashId = CreateVirtualSlash(operation.SourceSeatId);
        RequestCardTargets(operation.SourceSeatId, virtualSlashId, _content.Cards["slash"]);
    }

    private void RequestSerpentSpearResponseCost(PendingOperation original, int responderSeatId)
    {
        List<ChoiceOption> options = State.Players[responderSeatId].Hand
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "equipment.serpent_spear.cost",
                cardId,
                AiValue: -CardUseValue(cardId)))
            .ToList();
        SetChoice(
            responderSeatId,
            ChoiceKind.SelectCard,
            "equipment.serpent_spear.choose_two",
            options,
            PendingOperationKind.SerpentSpearResponseCost,
            minimum: 2,
            maximum: 2,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SerpentSpearResponseCost,
                ResumeKind = original.Kind,
                SourceSeatId = original.SourceSeatId,
                TargetSeatId = original.TargetSeatId,
                OtherSeatId = original.OtherSeatId,
                CardInstanceId = original.CardInstanceId,
                Continuation = original.Continuation
            });
    }

    private void ResolveSerpentSpearResponseCost(PendingOperation operation, IReadOnlyList<string> optionIds)
    {
        foreach (string optionId in optionIds)
        {
            DiscardFromHand(operation.TargetSeatId, ParseCardOption(optionId));
        }
        switch (operation.ResumeKind)
        {
            case PendingOperationKind.DuelResponse:
                RequestDuelSlash(operation.SourceSeatId, operation.OtherSeatId, operation.TargetSeatId, operation.CardInstanceId);
                break;
            case PendingOperationKind.RespondSlash:
                ContinueSequentialTargetEffect(operation.Continuation
                    ?? throw new InvalidOperationException("Serpent Spear mass response has no continuation."));
                break;
            case PendingOperationKind.BorrowSwordSlash:
                string virtualSlashId = CreateVirtualSlash(operation.TargetSeatId);
                Emit(RuleEventKind.CardUsed, operation.TargetSeatId, new[] { operation.OtherSeatId }, new TextRuleEventPayload("equipment.serpent_spear.virtual_slash"));
                RequestDodge(
                    operation.TargetSeatId,
                    operation.OtherSeatId,
                    virtualSlashId,
                    DamageNature.Physical,
                    operation.CardInstanceId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Serpent Spear response: {operation.ResumeKind}");
        }
    }

    private string CreateVirtualSlash(int sourceSeatId)
    {
        string instanceId = $"virtual-slash-{++_choiceSequence:D8}";
        State.Cards.Add(instanceId, new CardState
        {
            InstanceId = instanceId,
            DefinitionId = "slash",
            Suit = CardSuit.None,
            Rank = 0,
            Zone = CardZone.Processing,
            OwnerSeatId = sourceSeatId,
            IsFaceUp = true
        });
        State.ProcessingArea.Add(instanceId);
        CommitMutation();
        Emit(RuleEventKind.SkillTriggered, sourceSeatId, new[] { sourceSeatId }, new TextRuleEventPayload("equipment.serpent_spear"));
        return instanceId;
    }

    private void RequestDoubleSwordsChoice(PendingOperation sequence, int targetSeatId)
    {
        PlayerState target = State.Players[targetSeatId];
        List<ChoiceOption> options = target.Hand
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "equipment.double_swords.discard",
                cardId,
                AiValue: -CardUseValue(cardId)))
            .ToList();
        options.Add(new ChoiceOption("control:source_draw", ChoiceOptionKind.Control, "equipment.double_swords.source_draw", AiValue: -2));
        SetChoice(
            targetSeatId,
            ChoiceKind.SelectControl,
            "equipment.double_swords.choose",
            options,
            PendingOperationKind.DoubleSwordsChoice,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.DoubleSwordsChoice,
                SourceSeatId = sequence.SourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = sequence.CardInstanceId,
                DamageNatureOverride = sequence.DamageNatureOverride,
                DamageAmount = sequence.DamageAmount,
                Continuation = sequence
            });
    }

    private void ResolveDoubleSwordsChoice(PendingOperation operation, string optionId)
    {
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            DiscardFromHand(operation.TargetSeatId, ParseCardOption(optionId));
        }
        else
        {
            DrawCards(State.Players[operation.SourceSeatId], 1);
        }
        RequestDodge(
            operation.SourceSeatId,
            operation.TargetSeatId,
            operation.CardInstanceId,
            operation.DamageNatureOverride,
            continuation: operation.Continuation,
            damageAmount: operation.DamageAmount);
    }

    private void RequestSequenceDodge(PendingOperation sequence, int targetSeatId) => RequestDodge(
        sequence.SourceSeatId,
        targetSeatId,
        sequence.CardInstanceId,
        sequence.DamageNatureOverride,
        continuation: sequence,
        damageAmount: sequence.DamageAmount);

    private bool TryRequestIceSword(PendingOperation slashOperation)
    {
        PlayerState source = State.Players[slashOperation.SourceSeatId];
        PlayerState target = State.Players[slashOperation.TargetSeatId];
        if (!HasEquippedCard(source, "ice_sword"))
        {
            return false;
        }
        List<ChoiceOption> cards = LegalOwnedCards(target)
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                "equipment.ice_sword.discard_target_card",
                cardId,
                AiValue: 2))
            .ToList();
        if (cards.Count == 0)
        {
            return false;
        }
        SetChoice(
            source.SeatId,
            ChoiceKind.SelectCard,
            "equipment.ice_sword.choose_cards",
            cards,
            PendingOperationKind.IceSwordCards,
            minimum: 1,
            maximum: Math.Min(2, cards.Count),
            allowCancel: true,
            operation: CopySlashOperation(slashOperation, PendingOperationKind.IceSwordCards));
        return true;
    }

    private void ResolveIceSwordCards(PendingOperation operation, IReadOnlyList<string> optionIds)
    {
        foreach (string optionId in optionIds)
        {
            DiscardOwnedCard(operation.TargetSeatId, ParseCardOption(optionId));
        }
        Emit(RuleEventKind.SkillTriggered, operation.SourceSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload("equipment.ice_sword"));
        CompleteAvoidedSlash(operation);
    }

    private bool TryRequestPostDodgeWeapon(PendingOperation slashOperation)
    {
        PlayerState source = State.Players[slashOperation.SourceSeatId];
        if (HasEquippedCard(source, "stone_axe"))
        {
            List<ChoiceOption> costs = LegalOwnedCards(source)
                .Where(cardId => cardId != slashOperation.CardInstanceId)
                .Select(cardId => new ChoiceOption(
                    $"card:{cardId}",
                    ChoiceOptionKind.Card,
                    "equipment.stone_axe.cost",
                    cardId,
                    AiValue: -CardUseValue(cardId)))
                .ToList();
            if (costs.Count >= 2)
            {
                SetChoice(
                    source.SeatId,
                    ChoiceKind.Discard,
                    "equipment.stone_axe.choose_cost",
                    costs,
                    PendingOperationKind.StoneAxeCost,
                    minimum: 2,
                    maximum: 2,
                    allowCancel: true,
                    operation: CopySlashOperation(slashOperation, PendingOperationKind.StoneAxeCost));
                return true;
            }
        }
        if (HasEquippedCard(source, "green_dragon_blade"))
        {
            List<ChoiceOption> options = source.Hand
                .Where(cardId => IsSlash(State.Cards[cardId]))
                .OrderBy(cardId => cardId, StringComparer.Ordinal)
                .Select(cardId => new ChoiceOption(
                    $"card:{cardId}",
                    ChoiceOptionKind.Card,
                    "equipment.green_dragon_blade.use_slash",
                    cardId,
                    AiValue: 3))
                .ToList();
            if (options.Count > 0)
            {
                options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: 0));
                SetChoice(
                    source.SeatId,
                    ChoiceKind.UseOrRespond,
                    "equipment.green_dragon_blade.follow_up",
                    options,
                    PendingOperationKind.GreenDragonFollowUp,
                    operation: CopySlashOperation(slashOperation, PendingOperationKind.GreenDragonFollowUp));
                return true;
            }
        }
        return false;
    }

    private void ResolveStoneAxeCost(PendingOperation operation, IReadOnlyList<string> optionIds)
    {
        foreach (string optionId in optionIds)
        {
            DiscardOwnedCard(operation.SourceSeatId, ParseCardOption(optionId));
        }
        Emit(RuleEventKind.SkillTriggered, operation.SourceSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload("equipment.stone_axe"));
        ApplySlashDamage(operation);
        if (operation.Continuation is null)
        {
            CompleteAvoidedSlash(operation);
        }
    }

    private void ResolveGreenDragonFollowUp(PendingOperation operation, string optionId)
    {
        if (!optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            CompleteAvoidedSlash(operation);
            return;
        }
        string slashCardId = ParseCardOption(optionId);
        MoveHandCardToProcessing(operation.SourceSeatId, slashCardId);
        Emit(RuleEventKind.CardUsed, operation.SourceSeatId, new[] { operation.TargetSeatId }, new CardMoveRuleEventPayload(
            slashCardId,
            CardZone.Hand,
            CardZone.Processing,
            operation.SourceSeatId));
        CardDefinition slash = _content.Cards[State.Cards[slashCardId].DefinitionId];
        DamageNature nature = slash.EffectId switch
        {
            "card.fire_slash" => DamageNature.Fire,
            "card.thunder_slash" => DamageNature.Thunder,
            _ => DamageNature.Physical
        };
        RequestDodge(
            operation.SourceSeatId,
            operation.TargetSeatId,
            slashCardId,
            nature,
            operation.Continuation is null ? operation.CardInstanceId : string.Empty,
            operation.Continuation,
            1);
    }

    private void RequestKylinMountChoice(PendingOperation operation)
    {
        PlayerState target = State.Players[operation.TargetSeatId];
        List<ChoiceOption> options = target.Equipment
            .Where(entry => entry.Key is EquipmentSlot.OffensiveMount or EquipmentSlot.DefensiveMount)
            .OrderBy(entry => entry.Key)
            .Select(entry => new ChoiceOption(
                $"card:{entry.Value}",
                ChoiceOptionKind.Card,
                "equipment.kylin_bow.discard_mount",
                entry.Value,
                AiValue: 3))
            .ToList();
        if (options.Count == 0)
        {
            if (operation.Continuation is not null)
            {
                ResumeContinuation(operation.Continuation);
            }
            return;
        }
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: 0));
        SetChoice(
            operation.SourceSeatId,
            ChoiceKind.SelectCard,
            "equipment.kylin_bow.choose_mount",
            options,
            PendingOperationKind.KylinMountChoice,
            operation: operation);
    }

    private void ResolveKylinMountChoice(PendingOperation operation, string optionId)
    {
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            DiscardOwnedCard(operation.TargetSeatId, ParseCardOption(optionId));
            Emit(RuleEventKind.SkillTriggered, operation.SourceSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload("equipment.kylin_bow"));
        }
        if (operation.Continuation is not null)
        {
            ResumeContinuation(operation.Continuation);
        }
    }

    private PendingOperation CopySlashOperation(PendingOperation source, PendingOperationKind kind) => new()
    {
        Kind = kind,
        SourceSeatId = source.SourceSeatId,
        TargetSeatId = source.TargetSeatId,
        CardInstanceId = source.CardInstanceId,
        OtherCardInstanceId = source.OtherCardInstanceId,
        DamageNatureOverride = source.DamageNatureOverride,
        DamageAmount = source.DamageAmount,
        Continuation = source.Continuation
    };

    private IEnumerable<string> LegalOwnedCards(PlayerState player) => player.Hand
        .Concat(player.Equipment.Values)
        .Concat(player.JudgementArea)
        .OrderBy(cardId => cardId, StringComparer.Ordinal);

    private void DiscardOwnedCard(int ownerSeatId, string cardInstanceId)
    {
        PlayerState owner = State.Players[ownerSeatId];
        if (owner.Hand.Contains(cardInstanceId))
        {
            DiscardFromHand(ownerSeatId, cardInstanceId);
            return;
        }
        bool removed = owner.JudgementArea.Remove(cardInstanceId);
        EquipmentSlot? slot = owner.Equipment
            .Where(entry => entry.Value == cardInstanceId)
            .Select(entry => (EquipmentSlot?)entry.Key)
            .FirstOrDefault();
        if (slot is not null)
        {
            removed |= owner.Equipment.Remove(slot.Value);
        }
        if (!removed)
        {
            throw new InvalidOperationException("The selected owned card is no longer available.");
        }
        State.DiscardPile.Add(cardInstanceId);
        MoveCard(cardInstanceId, CardZone.DiscardPile, 0);
        CommitMutation();
        Emit(RuleEventKind.CardsDiscarded, ownerSeatId, new[] { ownerSeatId }, new CardMoveRuleEventPayload(
            cardInstanceId,
            slot is null ? CardZone.Judgement : CardZone.Equipment,
            CardZone.DiscardPile,
            ownerSeatId));
        if (slot is not null)
        {
            HandleEquipmentLeft(owner, cardInstanceId);
        }
    }

    private void CompleteAvoidedSlash(PendingOperation operation)
    {
        if (operation.Continuation is not null)
        {
            if (!string.Equals(operation.CardInstanceId, operation.Continuation.CardInstanceId, StringComparison.Ordinal))
            {
                FinishUsedCard(operation.CardInstanceId);
            }
            ContinueSequentialTargetEffect(operation.Continuation);
            return;
        }
        FinishUsedCard(operation.CardInstanceId);
        if (!string.IsNullOrWhiteSpace(operation.OtherCardInstanceId))
        {
            FinishUsedCard(operation.OtherCardInstanceId);
        }
    }

    private void RequestDodge(
        int sourceSeatId,
        int targetSeatId,
        string cardInstanceId,
        DamageNature? natureOverride = null,
        string followUpCardId = "",
        PendingOperation? continuation = null,
        int damageAmount = 0)
    {
        PlayerState target = State.Players[targetSeatId];
        bool armorIgnored = SlashIgnoresArmor(sourceSeatId, cardInstanceId);
        CardState sourceCard = State.Cards[cardInstanceId];
        string sourceEffectId = _content.Cards[sourceCard.DefinitionId].EffectId;
        bool normalSlash = sourceEffectId == "card.slash" && natureOverride.GetValueOrDefault(DamageNature.Physical) == DamageNature.Physical;
        bool blackSlash = IsSlash(sourceCard) && sourceCard.Suit is CardSuit.Spade or CardSuit.Club;
        if (!armorIgnored
            && ((normalSlash && HasEquippedCard(target, "vine_armor"))
                || (blackSlash && HasEquippedCard(target, "renwang_shield"))))
        {
            Emit(RuleEventKind.CardCancelled, targetSeatId, new[] { targetSeatId }, new TextRuleEventPayload(
                HasEquippedCard(target, "vine_armor") ? "equipment.vine_armor.slash_immunity" : "equipment.renwang_shield"));
            CompleteAvoidedSlash(new PendingOperation
            {
                SourceSeatId = sourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = cardInstanceId,
                OtherCardInstanceId = followUpCardId,
                DamageNatureOverride = natureOverride,
                Continuation = continuation,
                DamageAmount = damageAmount
            });
            return;
        }
        List<ChoiceOption> options = target.Hand
            .Where(cardId => _content.Cards[State.Cards[cardId].DefinitionId].EffectId == "card.dodge")
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "card.dodge", cardId, AiValue: 10))
            .ToList();
        if (HasSkill(target, "auto_dodge")
            && target.Marks.GetValueOrDefault("skill:auto_dodge:round") != State.RoundNumber)
        {
            options.Add(new ChoiceOption("skill:auto_dodge", ChoiceOptionKind.Skill, "skill.auto_dodge", AiValue: 12));
        }
        if (!armorIgnored && HasEquippedCard(target, "eight_diagram"))
        {
            options.Add(new ChoiceOption("equipment:eight_diagram", ChoiceOptionKind.Skill, "equipment.eight_diagram", AiValue: 9));
        }
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: -10));
        SetChoice(
            targetSeatId,
            ChoiceKind.UseOrRespond,
            "response.dodge",
            options,
            PendingOperationKind.RespondDodge,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.RespondDodge,
                SourceSeatId = sourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = cardInstanceId,
                OtherCardInstanceId = followUpCardId,
                DamageNatureOverride = natureOverride,
                Continuation = continuation,
                DamageAmount = damageAmount
            });
    }

    private void ResolveDodgeResponse(PendingOperation operation, string optionId)
    {
        bool avoidedDamage = false;
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            DiscardFromHand(operation.TargetSeatId, ParseCardOption(optionId));
            avoidedDamage = true;
        }
        else if (string.Equals(optionId, "skill:auto_dodge", StringComparison.Ordinal))
        {
            State.Players[operation.TargetSeatId].Marks["skill:auto_dodge:round"] = State.RoundNumber;
            CommitMutation();
            Emit(RuleEventKind.SkillTriggered, operation.TargetSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload("skill.auto_dodge"));
            avoidedDamage = true;
        }
        else if (string.Equals(optionId, "equipment:eight_diagram", StringComparison.Ordinal))
        {
            CardState judgement = DrawTopCardToProcessing();
            bool red = judgement.Suit is CardSuit.Heart or CardSuit.Diamond;
            Emit(RuleEventKind.Judgement, operation.TargetSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload(
                red ? "equipment.eight_diagram.success" : "equipment.eight_diagram.failure",
                new Dictionary<string, string> { ["cardInstanceId"] = judgement.InstanceId }));
            MoveProcessingCardToDiscard(judgement.InstanceId);
            avoidedDamage = red;
            if (!red)
            {
                if (TryRequestIceSword(operation))
                {
                    return;
                }
                ApplySlashDamage(operation);
            }
        }
        else
        {
            if (TryRequestIceSword(operation))
            {
                return;
            }
            ApplySlashDamage(operation);
        }
        if (avoidedDamage)
        {
            if (TryRequestPostDodgeWeapon(operation))
            {
                return;
            }
            CompleteAvoidedSlash(operation);
            return;
        }
        if (operation.Continuation is not null)
        {
            if (!string.Equals(operation.CardInstanceId, operation.Continuation.CardInstanceId, StringComparison.Ordinal))
            {
                FinishUsedCard(operation.CardInstanceId);
            }
            return;
        }
        CompleteAvoidedSlash(operation);
    }

    private void ApplySlashDamage(PendingOperation operation)
    {
        CardDefinition slash = _content.Cards[State.Cards[operation.CardInstanceId].DefinitionId];
        DamageNature nature = operation.DamageNatureOverride ?? slash.EffectId switch
        {
            "card.fire_slash" => DamageNature.Fire,
            "card.thunder_slash" => DamageNature.Thunder,
            _ => DamageNature.Physical
        };
        int amount = operation.DamageAmount > 0
            ? operation.DamageAmount
            : 1 + State.Players[operation.SourceSeatId].Marks.GetValueOrDefault("wine_damage");
        if (operation.DamageAmount == 0)
        {
            State.Players[operation.SourceSeatId].Marks.Remove("wine_damage");
        }
        if (HasEquippedCard(State.Players[operation.SourceSeatId], "guding_blade")
            && State.Players[operation.TargetSeatId].Hand.Count == 0)
        {
            amount++;
        }
        PendingOperation? damageContinuation = operation.Continuation;
        if (HasEquippedCard(State.Players[operation.SourceSeatId], "kylin_bow")
            && State.Players[operation.TargetSeatId].Equipment.Keys.Any(slot => slot is EquipmentSlot.OffensiveMount or EquipmentSlot.DefensiveMount))
        {
            damageContinuation = new PendingOperation
            {
                Kind = PendingOperationKind.KylinMountChoice,
                SourceSeatId = operation.SourceSeatId,
                TargetSeatId = operation.TargetSeatId,
                CardInstanceId = operation.CardInstanceId,
                Continuation = operation.Continuation
            };
        }
        Damage(
            operation.SourceSeatId,
            operation.TargetSeatId,
            amount,
            nature,
            operation.CardInstanceId,
            damageContinuation);
    }

    private void RequestDuelSlash(int originalSourceSeatId, int responderSeatId, int otherSeatId, string cardInstanceId)
    {
        PlayerState responder = State.Players[responderSeatId];
        List<ChoiceOption> options = responder.Hand
            .Where(cardId => IsSlash(State.Cards[cardId]))
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "card.slash", cardId, AiValue: 8))
            .ToList();
        if (HasEquippedCard(responder, "serpent_spear") && responder.Hand.Count >= 2)
        {
            options.Add(new ChoiceOption("equipment:serpent_spear", ChoiceOptionKind.Skill, "equipment.serpent_spear.virtual_slash", AiValue: 7));
        }
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: -8));
        SetChoice(
            responderSeatId,
            ChoiceKind.UseOrRespond,
            "response.duel_slash",
            options,
            PendingOperationKind.DuelResponse,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.DuelResponse,
                SourceSeatId = originalSourceSeatId,
                TargetSeatId = responderSeatId,
                OtherSeatId = otherSeatId,
                CardInstanceId = cardInstanceId
            });
    }

    private void ResolveSlashResponse(PendingOperation operation, string optionId)
    {
        if (string.Equals(optionId, "equipment:serpent_spear", StringComparison.Ordinal))
        {
            RequestSerpentSpearResponseCost(operation, operation.TargetSeatId);
            return;
        }
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            DiscardFromHand(operation.TargetSeatId, ParseCardOption(optionId));
            if (operation.Kind == PendingOperationKind.DuelResponse)
            {
                RequestDuelSlash(operation.SourceSeatId, operation.OtherSeatId, operation.TargetSeatId, operation.CardInstanceId);
            }
            else
            {
                ContinueSequentialTargetEffect(operation.Continuation
                    ?? throw new InvalidOperationException("Mass response has no continuation."));
            }
            return;
        }

        Damage(
            operation.OtherSeatId,
            operation.TargetSeatId,
            1,
            DamageNature.Physical,
            operation.CardInstanceId,
            operation.Kind == PendingOperationKind.DuelResponse ? null : operation.Continuation);
        if (operation.Kind == PendingOperationKind.DuelResponse)
        {
            FinishUsedCard(operation.CardInstanceId);
        }
    }

    private void BeginMassResponse(int sourceSeatId, string cardInstanceId, string effectId)
    {
        BeginSequentialTargetEffect(
            sourceSeatId,
            cardInstanceId,
            effectId,
            AlivePlayersInSeatOrder(sourceSeatId).Where(player => player.SeatId != sourceSeatId).Select(player => player.SeatId));
    }

    private void RequestMassResponse(PendingOperation sequence, int targetSeatId, bool requiresSlash)
    {
        PlayerState target = State.Players[targetSeatId];
        List<ChoiceOption> options = target.Hand
            .Where(cardId => requiresSlash ? IsSlash(State.Cards[cardId]) : _content.Cards[State.Cards[cardId].DefinitionId].EffectId == "card.dodge")
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, requiresSlash ? "card.slash" : "card.dodge", cardId, AiValue: 8))
            .ToList();
        if (requiresSlash && HasEquippedCard(target, "serpent_spear") && target.Hand.Count >= 2)
        {
            options.Add(new ChoiceOption("equipment:serpent_spear", ChoiceOptionKind.Skill, "equipment.serpent_spear.virtual_slash", AiValue: 7));
        }
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: -8));
        SetChoice(
            target.SeatId,
            ChoiceKind.UseOrRespond,
            requiresSlash ? "response.slash" : "response.dodge",
            options,
            requiresSlash ? PendingOperationKind.RespondSlash : PendingOperationKind.RespondDodge,
            operation: new PendingOperation
            {
                Kind = requiresSlash ? PendingOperationKind.RespondSlash : PendingOperationKind.RespondDodge,
                SourceSeatId = sequence.SourceSeatId,
                OtherSeatId = sequence.SourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = sequence.CardInstanceId,
                Continuation = sequence
            });
    }

    private void RequestTargetCardSelection(int sourceSeatId, int targetSeatId, string usedCardId, bool steal)
    {
        PlayerState target = State.Players[targetSeatId];
        List<ChoiceOption> options = new();
        options.AddRange(target.Hand.OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "card.hidden", cardId, AiValue: 1)));
        options.AddRange(target.Equipment.Values.OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Concat(target.JudgementArea.OrderBy(cardId => cardId, StringComparer.Ordinal))
            .Select(cardId => new ChoiceOption(
                $"card:{cardId}",
                ChoiceOptionKind.Card,
                _content.Cards[State.Cards[cardId].DefinitionId].DisplayName,
                cardId,
                AiValue: 2)));
        if (options.Count == 0)
        {
            FinishUsedCard(usedCardId);
            return;
        }
        SetChoice(
            sourceSeatId,
            ChoiceKind.SelectCard,
            steal ? "card.snatch.choose" : "card.dismantle.choose",
            options,
            PendingOperationKind.SelectTargetCard,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SelectTargetCard,
                SourceSeatId = sourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = usedCardId,
                OtherSeatId = steal ? 1 : 0
            });
    }

    private void ResolveTargetCardSelection(PendingOperation operation, string selectedCardId)
    {
        PlayerState target = State.Players[operation.TargetSeatId];
        CardZone fromZone = State.Cards[selectedCardId].Zone;
        bool removed = target.Hand.Remove(selectedCardId) || target.JudgementArea.Remove(selectedCardId);
        EquipmentSlot? equipmentSlot = target.Equipment
            .Where(entry => entry.Value == selectedCardId)
            .Select(entry => (EquipmentSlot?)entry.Key)
            .FirstOrDefault();
        if (equipmentSlot is not null)
        {
            removed |= target.Equipment.Remove(equipmentSlot.Value);
        }
        if (!removed)
        {
            throw new InvalidOperationException("Selected target card is no longer in a legal zone.");
        }

        if (operation.OtherSeatId == 1)
        {
            PlayerState source = State.Players[operation.SourceSeatId];
            source.Hand.Add(selectedCardId);
            MoveCard(selectedCardId, CardZone.Hand, source.SeatId);
        }
        else
        {
            State.DiscardPile.Add(selectedCardId);
            MoveCard(selectedCardId, CardZone.DiscardPile, 0);
        }
        CommitMutation();
        Emit(RuleEventKind.CardsDiscarded, operation.SourceSeatId, new[] { operation.TargetSeatId }, new CardMoveRuleEventPayload(
            selectedCardId,
            fromZone,
            operation.OtherSeatId == 1 ? CardZone.Hand : CardZone.DiscardPile,
            operation.TargetSeatId,
            operation.OtherSeatId == 1 ? operation.SourceSeatId : 0));
        if (equipmentSlot is not null)
        {
            HandleEquipmentLeft(target, selectedCardId);
        }
        FinishUsedCard(operation.CardInstanceId);
    }

    private void BeginHarvest(int sourceSeatId, string harvestCardId)
    {
        int[] targetSeatIds = AlivePlayersInSeatOrder(sourceSeatId).Select(player => player.SeatId).ToArray();
        foreach (int _ in targetSeatIds)
        {
            if (State.DrawPile.Count == 0)
            {
                ReshuffleDiscardPile();
            }
            if (State.DrawPile.Count > 0)
            {
                string revealed = State.DrawPile[^1];
                State.DrawPile.RemoveAt(State.DrawPile.Count - 1);
                MoveCard(revealed, CardZone.Processing, 0);
                State.ProcessingArea.Add(revealed);
            }
        }
        CommitMutation();
        BeginSequentialTargetEffect(sourceSeatId, harvestCardId, "card.harvest", targetSeatIds);
    }

    private void RequestHarvestPick(PendingOperation sequence, int targetSeatId)
    {
        List<string> pool = State.ProcessingArea
            .Where(cardId => cardId != sequence.CardInstanceId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToList();
        if (pool.Count == 0)
        {
            ContinueSequentialTargetEffect(sequence);
            return;
        }
        List<ChoiceOption> options = pool.Select(cardId => new ChoiceOption(
            $"card:{cardId}",
            ChoiceOptionKind.Card,
            _content.Cards[State.Cards[cardId].DefinitionId].DisplayName,
            cardId,
            AiValue: CardUseValue(cardId))).ToList();
        SetChoice(
            targetSeatId,
            ChoiceKind.SelectCard,
            "card.harvest.choose",
            options,
            PendingOperationKind.HarvestPick,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.HarvestPick,
                SourceSeatId = sequence.SourceSeatId,
                TargetSeatId = targetSeatId,
                CardInstanceId = sequence.CardInstanceId,
                Continuation = sequence
            });
    }

    private void ResolveHarvestPick(PendingOperation operation, string cardId)
    {
        if (!State.ProcessingArea.Remove(cardId))
        {
            throw new InvalidOperationException("Selected Harvest card is unavailable.");
        }
        PlayerState chooser = State.Players[operation.TargetSeatId];
        chooser.Hand.Add(cardId);
        MoveCard(cardId, CardZone.Hand, chooser.SeatId);
        CommitMutation();
        ContinueSequentialTargetEffect(operation.Continuation
            ?? throw new InvalidOperationException("Harvest choice has no continuation."));
    }

    private void HandleCancelledChoice(PendingOperation operation)
    {
        if (operation.Kind == PendingOperationKind.PlayAction)
        {
            ChangePhase(GamePhase.Discard);
            return;
        }
        if (operation.Kind == PendingOperationKind.StoneAxeCost)
        {
            CompleteAvoidedSlash(operation);
            return;
        }
        if (operation.Kind == PendingOperationKind.IceSwordCards)
        {
            ApplySlashDamage(operation);
            if (operation.Continuation is null)
            {
                CompleteAvoidedSlash(operation);
            }
            return;
        }
        throw new InvalidOperationException("The cancelled choice has no cancellation handler.");
    }

    private void BeginSkillAction(int sourceSeatId, string skillId)
    {
        SkillDefinitionV2 skill = _content.Skills[skillId];
        PlayerState source = State.Players[sourceSeatId];
        if ((skill.Tags & SkillTags.Active) == 0 || !CanUseActiveSkill(source, skill))
        {
            throw new InvalidOperationException("Selected skill is not currently usable.");
        }

        if (skillId == "limit_break")
        {
            source.Marks["skill:limit_break:used"] = 1;
            source.Marks["limit_break_damage"] = 2;
            DrawCards(source, 2);
            CommitMutation();
            Emit(RuleEventKind.SkillTriggered, sourceSeatId, new[] { sourceSeatId }, new TextRuleEventPayload("skill.limit_break"));
            return;
        }

        List<ChoiceOption> cards = source.Hand
            .Where(cardId => skillId != "blade_conversion" || _content.Cards[State.Cards[cardId].DefinitionId].Category != CardCategory.Basic)
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "skill.cost.card", cardId, AiValue: -1))
            .ToList();
        if (cards.Count == 0)
        {
            Emit(RuleEventKind.SkillTriggered, sourceSeatId, new[] { sourceSeatId }, new TextRuleEventPayload("skill.no_cost"));
            return;
        }

        SetChoice(
            sourceSeatId,
            ChoiceKind.SelectCard,
            "skill.choose_cost",
            cards,
            PendingOperationKind.SkillCost,
            allowCancel: (skill.Tags & SkillTags.Forced) == 0,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SkillCost,
                SourceSeatId = sourceSeatId,
                SkillId = skillId
            });
    }

    private void ResolveSkillAction(PendingOperation operation, ChoiceResult result)
    {
        string cardId = ParseCardOption(result.SelectedOptionIds.Single());
        if (operation.SkillId == "active_draw")
        {
            DiscardFromHand(operation.SourceSeatId, cardId);
            DrawCards(State.Players[operation.SourceSeatId], 2);
            MarkActiveSkillUsed(State.Players[operation.SourceSeatId], operation.SkillId);
            EmitSkillTriggered(operation);
            return;
        }

        List<ChoiceOption> targets = State.Players.Values
            .Where(player => player.IsAlive && player.SeatId != operation.SourceSeatId)
            .Where(player => operation.SkillId != "ally_supply" || _mode.GetAttitude(State, operation.SourceSeatId, player.SeatId) > 0)
            .Where(player => operation.SkillId != "blade_conversion"
                || EffectiveDistance(operation.SourceSeatId, player.SeatId) <= AttackRange(State.Players[operation.SourceSeatId]))
            .OrderBy(player => player.SeatId)
            .Select(player => new ChoiceOption(
                $"seat:{player.SeatId}",
                ChoiceOptionKind.Player,
                "target.player",
                player.SeatId.ToString(),
                AiValue: operation.SkillId == "ally_supply"
                    ? _mode.GetAttitude(State, operation.SourceSeatId, player.SeatId)
                    : -_mode.GetAttitude(State, operation.SourceSeatId, player.SeatId)))
            .ToList();
        SetChoice(
            operation.SourceSeatId,
            ChoiceKind.SelectTarget,
            "skill.choose_target",
            targets,
            PendingOperationKind.SkillTarget,
            operation: new PendingOperation
            {
                Kind = PendingOperationKind.SkillTarget,
                SourceSeatId = operation.SourceSeatId,
                SkillId = operation.SkillId,
                CardInstanceId = cardId
            });
    }

    private void ResolveSkillTarget(PendingOperation operation, int targetSeatId)
    {
        PlayerState source = State.Players[operation.SourceSeatId];
        switch (operation.SkillId)
        {
            case "discard_strike":
                DiscardFromHand(source.SeatId, operation.CardInstanceId);
                MarkActiveSkillUsed(source, operation.SkillId);
                EmitSkillTriggered(operation, targetSeatId);
                Damage(source.SeatId, targetSeatId, 1, DamageNature.Physical, string.Empty);
                break;
            case "ally_supply":
                if (!source.Hand.Remove(operation.CardInstanceId))
                {
                    throw new InvalidOperationException("The ally supply card is no longer in hand.");
                }
                PlayerState target = State.Players[targetSeatId];
                target.Hand.Add(operation.CardInstanceId);
                MoveCard(operation.CardInstanceId, CardZone.Hand, targetSeatId);
                MarkActiveSkillUsed(source, operation.SkillId);
                CommitMutation();
                DrawCards(source, 1);
                DrawCards(target, 1);
                EmitSkillTriggered(operation, targetSeatId);
                break;
            case "blade_conversion":
                if (source.SlashUsesThisTurn > 0 && !HasEquippedCard(source, "crossbow"))
                {
                    throw new InvalidOperationException("The converted Slash exceeds the turn usage limit.");
                }
                source.SlashUsesThisTurn++;
                MoveHandCardToProcessing(source.SeatId, operation.CardInstanceId);
                EmitSkillTriggered(operation, targetSeatId);
                RequestDodge(source.SeatId, targetSeatId, operation.CardInstanceId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported active skill target: {operation.SkillId}");
        }
    }

    private void EmitSkillTriggered(PendingOperation operation, int targetSeatId = 0) =>
        Emit(RuleEventKind.SkillTriggered, operation.SourceSeatId, targetSeatId > 0 ? new[] { targetSeatId } : new[] { operation.SourceSeatId }, new TextRuleEventPayload("skill.triggered", new Dictionary<string, string>
        {
            ["skillId"] = operation.SkillId
        }));

    private void RequestDiscardIfNeeded(PlayerState player)
    {
        int required = Math.Max(0, player.Hand.Count - Math.Max(0, player.Health));
        if (required == 0)
        {
            return;
        }
        List<ChoiceOption> options = player.Hand
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "phase.discard.card", cardId, AiValue: -CardUseValue(cardId)))
            .ToList();
        SetChoice(
            player.SeatId,
            ChoiceKind.Discard,
            "phase.discard.choose",
            options,
            PendingOperationKind.DiscardToLimit,
            minimum: required,
            maximum: required);
    }

    private void ResolveJudgementArea(PlayerState player)
    {
        string? delayedCardId = player.JudgementArea.FirstOrDefault();
        if (delayedCardId is null)
        {
            return;
        }
        player.JudgementArea.Remove(delayedCardId);
        State.ProcessingArea.Add(delayedCardId);
        MoveCard(delayedCardId, CardZone.Processing, 0);
        CardState judgementCard = DrawTopCardToProcessing();
        PendingOperation operation = new()
        {
            Kind = PendingOperationKind.JudgementReplacement,
            SourceSeatId = player.SeatId,
            TargetSeatId = player.SeatId,
            CardInstanceId = delayedCardId,
            OtherCardInstanceId = judgementCard.InstanceId
        };
        foreach (PlayerState observer in AlivePlayersInSeatOrder(player.SeatId).Where(candidate =>
            HasSkill(candidate, "mirror_judgement")
            && candidate.Hand.Count > 0
            && candidate.Marks.GetValueOrDefault("skill:mirror_judgement:used") == 0))
        {
            operation.RemainingTargets.Enqueue(observer.SeatId);
        }
        ContinueJudgementReplacement(operation);
    }

    private void ContinueJudgementReplacement(PendingOperation operation)
    {
        if (operation.RemainingTargets.Count == 0)
        {
            FinalizeJudgement(operation);
            return;
        }
        int responderSeatId = operation.RemainingTargets.Dequeue();
        PlayerState responder = State.Players[responderSeatId];
        List<ChoiceOption> options = responder.Hand
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "judgement.replace_card", cardId, AiValue: 1))
            .ToList();
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: 0));
        SetChoice(
            responderSeatId,
            ChoiceKind.SelectCard,
            "judgement.replace",
            options,
            PendingOperationKind.JudgementReplacement,
            operation: operation);
    }

    private void ResolveJudgementReplacement(PendingOperation operation, string optionId)
    {
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            string replacementId = ParseCardOption(optionId);
            int responderSeatId = State.Cards[replacementId].OwnerSeatId;
            PlayerState responder = State.Players[responderSeatId];
            if (!responder.Hand.Remove(replacementId))
            {
                throw new InvalidOperationException("Judgement replacement card is no longer available.");
            }
            MoveProcessingCardToDiscard(operation.OtherCardInstanceId);
            State.ProcessingArea.Add(replacementId);
            MoveCard(replacementId, CardZone.Processing, 0);
            operation.OtherCardInstanceId = replacementId;
            responder.Marks["skill:mirror_judgement:used"] = 1;
            CommitMutation();
            Emit(RuleEventKind.SkillTriggered, responderSeatId, new[] { operation.TargetSeatId }, new TextRuleEventPayload("skill.mirror_judgement"));
            operation.RemainingTargets.Clear();
        }
        ContinueJudgementReplacement(operation);
    }

    private void FinalizeJudgement(PendingOperation operation)
    {
        PlayerState player = State.Players[operation.TargetSeatId];
        CardDefinition delayed = _content.Cards[State.Cards[operation.CardInstanceId].DefinitionId];
        CardState judgementCard = State.Cards[operation.OtherCardInstanceId];
        bool hit = delayed.EffectId switch
        {
            "card.indulgence" => judgementCard.Suit != CardSuit.Heart,
            "card.supply_shortage" => judgementCard.Suit != CardSuit.Club,
            "card.lightning" => judgementCard.Suit == CardSuit.Spade && judgementCard.Rank is >= 2 and <= 9,
            _ => false
        };
        Emit(RuleEventKind.Judgement, player.SeatId, new[] { player.SeatId }, new ValueRuleEventPayload(hit ? 1 : 0, delayed.CardId));
        foreach (PlayerState observer in State.Players.Values.Where(candidate => candidate.IsAlive
            && HasSkill(candidate, "judgement_draw")
            && candidate.Marks.GetValueOrDefault("skill:judgement_draw:used") == 0))
        {
            observer.Marks["skill:judgement_draw:used"] = 1;
            DrawCards(observer, 1);
        }
        MoveProcessingCardToDiscard(judgementCard.InstanceId);
        if (hit && delayed.EffectId == "card.indulgence")
        {
            player.TemporaryFlags.Add("skip_play");
        }
        else if (hit && delayed.EffectId == "card.supply_shortage")
        {
            player.TemporaryFlags.Add("skip_draw");
        }
        else if (hit && delayed.EffectId == "card.lightning")
        {
            Damage(0, player.SeatId, 3, DamageNature.Thunder, operation.CardInstanceId);
        }

        if (!hit && delayed.EffectId == "card.lightning")
        {
            PlayerState next = AlivePlayersInSeatOrder(player.SeatId).Skip(1).FirstOrDefault() ?? player;
            State.ProcessingArea.Remove(operation.CardInstanceId);
            next.JudgementArea.Add(operation.CardInstanceId);
            MoveCard(operation.CardInstanceId, CardZone.Judgement, next.SeatId);
            CommitMutation();
        }
        else
        {
            MoveProcessingCardToDiscard(operation.CardInstanceId);
        }
    }

    private void ChangePhase(GamePhase phase)
    {
        PlayerState current = State.Players[State.CurrentSeatId];
        if (phase == GamePhase.Draw && current.TemporaryFlags.Remove("skip_draw"))
        {
            State.Phase = GamePhase.Play;
            CommitMutation();
            Emit(RuleEventKind.PhaseSkipped, current.SeatId, new[] { current.SeatId }, new PhaseRuleEventPayload(GamePhase.Draw, true));
            EmitPhaseChanged(GamePhase.Play);
            return;
        }
        if (phase == GamePhase.Play && current.TemporaryFlags.Remove("skip_play"))
        {
            State.Phase = GamePhase.Discard;
            CommitMutation();
            Emit(RuleEventKind.PhaseSkipped, current.SeatId, new[] { current.SeatId }, new PhaseRuleEventPayload(GamePhase.Play, true));
            EmitPhaseChanged(GamePhase.Discard);
            return;
        }

        State.Phase = phase;
        CommitMutation();
        EmitPhaseChanged(phase);
    }

    private void AdvanceTurn()
    {
        int endingSeat = State.CurrentSeatId;
        Emit(RuleEventKind.TurnEnded, endingSeat, new[] { endingSeat }, EmptyRuleEventPayload.Instance);
        PlayerState endingPlayer = State.Players[endingSeat];
        endingPlayer.SlashUsesThisTurn = 0;
        endingPlayer.WineUsesThisTurn = 0;
        endingPlayer.Marks.Remove("wine_damage");
        foreach (string markId in endingPlayer.Marks.Keys
            .Where(markId => markId.StartsWith("skill:", StringComparison.Ordinal)
                && !string.Equals(markId, "skill:limit_break:used", StringComparison.Ordinal))
            .ToArray())
        {
            endingPlayer.Marks.Remove(markId);
        }
        endingPlayer.TemporaryFlags.Clear();

        VictoryResult? victory = _mode.CheckVictory(State);
        if (victory is not null)
        {
            CompleteMatch(victory);
            return;
        }

        do
        {
            State.CurrentSeatIndex = (State.CurrentSeatIndex + 1) % State.TurnOrder.Count;
            if (State.CurrentSeatIndex == 0)
            {
                State.RoundNumber++;
            }
        }
        while (!State.Players[State.CurrentSeatId].IsAlive);

        State.Phase = GamePhase.Preparation;
        CommitMutation();
        Emit(RuleEventKind.TurnStarted, State.CurrentSeatId, new[] { State.CurrentSeatId }, new ValueRuleEventPayload(State.RoundNumber));
        EmitPhaseChanged(GamePhase.Preparation);
    }

    private void Damage(
        int sourceSeatId,
        int targetSeatId,
        int amount,
        DamageNature nature,
        string sourceCardId,
        PendingOperation? continuation = null)
    {
        if (amount <= 0 || !State.Players[targetSeatId].IsAlive)
        {
            return;
        }
        if (sourceSeatId > 0 && State.Players.TryGetValue(sourceSeatId, out PlayerState? source))
        {
            if (HasSkill(source, "first_damage_boost")
                && source.Marks.GetValueOrDefault("skill:first_damage_boost:used") == 0)
            {
                amount++;
                source.Marks["skill:first_damage_boost:used"] = 1;
            }
            int limitedBonus = source.Marks.GetValueOrDefault("limit_break_damage");
            if (limitedBonus > 0)
            {
                amount += limitedBonus;
                source.Marks.Remove("limit_break_damage");
            }
        }
        PlayerState target = State.Players[targetSeatId];
        int baseDamageAmount = amount;
        amount = AdjustDamageForArmor(sourceSeatId, target, amount, nature, sourceCardId);
        target.Health -= amount;
        List<PlayerState> damagedPlayers = new() { target };
        CommitMutation();
        Emit(RuleEventKind.Damage, sourceSeatId, new[] { targetSeatId }, new DamageRuleEventPayload(amount, nature, sourceCardId));

        if (nature != DamageNature.Physical && target.IsChained)
        {
            target.IsChained = false;
            foreach (PlayerState chained in AlivePlayersInSeatOrder(targetSeatId).Where(player => player.SeatId != targetSeatId && player.IsChained).ToArray())
            {
                chained.IsChained = false;
                int chainedAmount = AdjustDamageForArmor(sourceSeatId, chained, baseDamageAmount, nature, sourceCardId);
                chained.Health -= chainedAmount;
                damagedPlayers.Add(chained);
                Emit(RuleEventKind.Damage, sourceSeatId, new[] { chained.SeatId }, new DamageRuleEventPayload(chainedAmount, nature, sourceCardId, true));
            }
            CommitMutation();
        }
        CheckBossPhaseTransition();
        foreach (PlayerState damaged in damagedPlayers)
        {
            if (damaged.IsAlive && HasSkill(damaged, "pain_draw"))
            {
                DrawCards(damaged, amount);
                Emit(RuleEventKind.SkillTriggered, damaged.SeatId, new[] { damaged.SeatId }, new TextRuleEventPayload("skill.pain_draw"));
            }
        }
        if (nature != DamageNature.Physical)
        {
            PlayerState? resonator = State.Players.Values
                .Where(player => player.IsAlive
                    && HasSkill(player, "elemental_resonance")
                    && player.Marks.GetValueOrDefault("skill:elemental_resonance:used") == 0)
                .OrderBy(player => SeatDistance(targetSeatId, player.SeatId))
                .ThenBy(player => player.SeatId)
                .FirstOrDefault();
            if (resonator is not null)
            {
                target.IsChained = !target.IsChained;
                resonator.Marks["skill:elemental_resonance:used"] = 1;
                CommitMutation();
                Emit(RuleEventKind.SkillTriggered, resonator.SeatId, new[] { targetSeatId }, new TextRuleEventPayload("skill.elemental_resonance"));
            }
        }
        BeginDyingSequence(sourceSeatId, damagedPlayers.Where(player => player.Health <= 0), continuation);
    }

    private int AdjustDamageForArmor(
        int sourceSeatId,
        PlayerState target,
        int amount,
        DamageNature nature,
        string sourceCardId)
    {
        bool ignored = sourceSeatId > 0
            && !string.IsNullOrWhiteSpace(sourceCardId)
            && State.Cards.ContainsKey(sourceCardId)
            && SlashIgnoresArmor(sourceSeatId, sourceCardId);
        if (ignored)
        {
            return amount;
        }
        if (nature == DamageNature.Fire && HasEquippedCard(target, "vine_armor"))
        {
            amount++;
        }
        if (amount > 1 && HasEquippedCard(target, "silver_lion"))
        {
            amount = 1;
        }
        return amount;
    }

    private void BeginDyingSequence(
        int sourceSeatId,
        IEnumerable<PlayerState> dyingPlayers,
        PendingOperation? continuation = null)
    {
        PendingOperation operation = new()
        {
            Kind = PendingOperationKind.DyingRescue,
            SourceSeatId = sourceSeatId,
            Continuation = continuation
        };
        foreach (PlayerState player in dyingPlayers.Where(player => player.IsAlive).DistinctBy(player => player.SeatId))
        {
            operation.RemainingDyingTargets.Enqueue(player.SeatId);
        }

        ContinueDyingSequence(operation);
    }

    private void ContinueDyingSequence(PendingOperation operation)
    {
        if (State.Status == MatchStatus.Completed)
        {
            return;
        }

        if (operation.TargetSeatId > 0)
        {
            PlayerState current = State.Players[operation.TargetSeatId];
            if (current.Health <= 0 && current.IsAlive && operation.RemainingTargets.Count > 0)
            {
                RequestDyingRescueChoice(operation, operation.RemainingTargets.Dequeue());
                return;
            }
            if (current.Health <= 0 && current.IsAlive)
            {
                DefeatPlayer(operation.SourceSeatId, current);
            }
            operation.TargetSeatId = 0;
            operation.RemainingTargets.Clear();
        }

        while (operation.RemainingDyingTargets.Count > 0)
        {
            int targetSeatId = operation.RemainingDyingTargets.Dequeue();
            PlayerState target = State.Players[targetSeatId];
            if (!target.IsAlive || target.Health > 0)
            {
                continue;
            }
            operation.TargetSeatId = targetSeatId;
            Emit(RuleEventKind.Dying, operation.SourceSeatId, new[] { targetSeatId }, new ValueRuleEventPayload(target.Health));
            foreach (PlayerState responder in AlivePlayersInSeatOrder(targetSeatId))
            {
                operation.RemainingTargets.Enqueue(responder.SeatId);
            }
            if (operation.RemainingTargets.Count > 0)
            {
                RequestDyingRescueChoice(operation, operation.RemainingTargets.Dequeue());
                return;
            }
        }

        if (State.Status == MatchStatus.Running && operation.Continuation is not null)
        {
            PendingOperation continuation = operation.Continuation;
            operation.Continuation = null;
            ResumeContinuation(continuation);
        }
    }

    private void ResumeContinuation(PendingOperation continuation)
    {
        switch (continuation.Kind)
        {
            case PendingOperationKind.SequentialTargetEffect:
                ContinueSequentialTargetEffect(continuation);
                break;
            case PendingOperationKind.KylinMountChoice:
                RequestKylinMountChoice(continuation);
                break;
            default:
                throw new InvalidOperationException($"Unsupported continuation: {continuation.Kind}");
        }
    }

    private void RequestDyingRescueChoice(PendingOperation operation, int responderSeatId)
    {
        PlayerState responder = State.Players[responderSeatId];
        List<ChoiceOption> options = responder.Hand
            .Where(cardId =>
            {
                string effectId = _content.Cards[State.Cards[cardId].DefinitionId].EffectId;
                return effectId == "card.peach" || (responderSeatId == operation.TargetSeatId && effectId == "card.wine");
            })
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .Select(cardId => new ChoiceOption($"card:{cardId}", ChoiceOptionKind.Card, "dying.rescue_card", cardId, AiValue: 20))
            .ToList();
        options.Add(new ChoiceOption("control:decline", ChoiceOptionKind.Control, "response.decline", AiValue: -20));
        SetChoice(
            responderSeatId,
            ChoiceKind.UseOrRespond,
            "dying.request_rescue",
            options,
            PendingOperationKind.DyingRescue,
            operation: operation);
    }

    private void ResolveDyingRescue(PendingOperation operation, string optionId)
    {
        int responderSeatId = State.PendingChoice?.ActingSeatId ?? 0;
        // AcceptChoice clears PendingChoice before dispatch; infer the responder
        // from the selected card owner for a rescue, or continue the seat queue.
        if (optionId.StartsWith("card:", StringComparison.Ordinal))
        {
            string cardId = ParseCardOption(optionId);
            responderSeatId = State.Cards[cardId].OwnerSeatId;
            DiscardFromHand(responderSeatId, cardId);
            PlayerState target = State.Players[operation.TargetSeatId];
            target.Health++;
            CommitMutation();
            Emit(RuleEventKind.Heal, responderSeatId, new[] { target.SeatId }, new ValueRuleEventPayload(1, "dying.rescue"));
            if (target.Health <= 0)
            {
                RequestDyingRescueChoice(operation, responderSeatId);
                return;
            }
        }
        ContinueDyingSequence(operation);
    }

    private void DefeatPlayer(int sourceSeatId, PlayerState target)
    {

        target.IsAlive = false;
        foreach (string cardId in target.Hand.ToArray())
        {
            DiscardFromHand(target.SeatId, cardId);
        }
        foreach (string cardId in target.Equipment.Values.ToArray())
        {
            MoveCard(cardId, CardZone.DiscardPile, 0);
            State.DiscardPile.Add(cardId);
        }
        target.Equipment.Clear();
        CommitMutation();
        Emit(RuleEventKind.Defeated, sourceSeatId, new[] { target.SeatId }, EmptyRuleEventPayload.Instance);
        ApplyIdentityDefeatReward(sourceSeatId, target);
        VictoryResult? victory = _mode.CheckVictory(State);
        if (victory is not null)
        {
            CompleteMatch(victory);
        }
    }

    private void ApplyIdentityDefeatReward(int sourceSeatId, PlayerState defeated)
    {
        if (State.ModeId != BuiltInModeIds.Identity
            || sourceSeatId <= 0
            || !State.Players.TryGetValue(sourceSeatId, out PlayerState? killer)
            || !killer.IsAlive)
        {
            return;
        }
        if (defeated.Role == IdentityRole.Rebel)
        {
            DrawCards(killer, 3);
            return;
        }
        if (killer.Role == IdentityRole.Lord && defeated.Role == IdentityRole.Loyalist)
        {
            foreach (string cardId in killer.Hand.ToArray())
            {
                DiscardFromHand(killer.SeatId, cardId);
            }
            foreach (string cardId in killer.Equipment.Values.ToArray())
            {
                killer.Equipment.Remove(_content.Cards[State.Cards[cardId].DefinitionId].EquipmentSlot!.Value);
                MoveCard(cardId, CardZone.DiscardPile, 0);
                State.DiscardPile.Add(cardId);
                HandleEquipmentLeft(killer, cardId);
            }
            CommitMutation();
        }
    }

    private void CheckBossPhaseTransition()
    {
        if (State.ModeId != BuiltInModeIds.Boss
            || string.IsNullOrWhiteSpace(_config.BossDefinitionId)
            || !_content.Bosses.TryGetValue(_config.BossDefinitionId, out BossDefinition? definition))
        {
            return;
        }
        PlayerState boss = State.Players.Values.Single(player => player.Role == IdentityRole.Boss);
        if (!boss.IsAlive
            || boss.Marks.GetValueOrDefault("boss_phase", 1) >= 2
            || boss.Health > Math.Max(1, (int)Math.Ceiling(boss.MaxHealth * definition.PhaseTwoHealthRatio)))
        {
            return;
        }
        boss.Marks["boss_phase"] = 2;
        foreach (string skillId in definition.PhaseTwoSkillIds.Where(skillId => !boss.SkillIds.Contains(skillId)))
        {
            boss.SkillIds.Add(skillId);
        }
        CommitMutation();
        Emit(RuleEventKind.SkillTriggered, boss.SeatId, new[] { boss.SeatId }, new TextRuleEventPayload("boss.phase_two"));
    }

    private void Heal(int sourceSeatId, int targetSeatId, int amount)
    {
        PlayerState target = State.Players[targetSeatId];
        int actual = Math.Min(amount, target.MaxHealth - target.Health);
        if (actual <= 0)
        {
            return;
        }
        target.Health += actual;
        CommitMutation();
        Emit(RuleEventKind.Heal, sourceSeatId, new[] { targetSeatId }, new ValueRuleEventPayload(actual));
    }

    private void CompleteMatch(VictoryResult victory)
    {
        State.Status = MatchStatus.Completed;
        State.Phase = GamePhase.Finished;
        State.WinnerSeatIds.Clear();
        State.WinnerSeatIds.AddRange(victory.WinnerSeatIds);
        State.ResultMessage = victory.ResultKey;
        State.PendingChoice = null;
        _pendingOperation = null;
        CommitMutation();
        Emit(RuleEventKind.MatchEnded, 0, victory.WinnerSeatIds, new TextRuleEventPayload(victory.ResultKey));
    }

    private void SetChoice(
        int actingSeatId,
        ChoiceKind kind,
        string promptKey,
        IReadOnlyList<ChoiceOption> options,
        PendingOperationKind operationKind,
        int minimum = 1,
        int maximum = 1,
        bool allowCancel = false,
        PendingOperation? operation = null)
    {
        CommitMutation();
        ChoiceRequest choice = new(
            $"choice-{++_choiceSequence:D8}",
            State.Revision,
            actingSeatId,
            kind,
            promptKey,
            options,
            minimum,
            maximum,
            allowCancel,
            IsPrivate: true);
        State.PendingChoice = choice;
        _pendingOperation = operation ?? new PendingOperation { Kind = operationKind, SourceSeatId = actingSeatId };
        Emit(RuleEventKind.ChoiceRequested, actingSeatId, new[] { actingSeatId }, new TextRuleEventPayload(promptKey));
    }

    private void DrawCards(PlayerState player, int count)
    {
        int actual = 0;
        for (int index = 0; index < count; index++)
        {
            if (State.DrawPile.Count == 0)
            {
                ReshuffleDiscardPile();
            }
            if (State.DrawPile.Count == 0)
            {
                break;
            }
            string cardId = State.DrawPile[^1];
            State.DrawPile.RemoveAt(State.DrawPile.Count - 1);
            State.Cards[cardId].Zone = CardZone.Hand;
            State.Cards[cardId].OwnerSeatId = player.SeatId;
            player.Hand.Add(cardId);
            actual++;
        }
        if (actual > 0)
        {
            CommitMutation();
            Emit(RuleEventKind.CardsDrawn, player.SeatId, new[] { player.SeatId }, new ValueRuleEventPayload(actual));
        }
    }

    private CardState DrawTopCardToProcessing()
    {
        if (State.DrawPile.Count == 0)
        {
            ReshuffleDiscardPile();
        }
        if (State.DrawPile.Count == 0)
        {
            throw new InvalidOperationException("No card is available for judgement.");
        }
        string cardId = State.DrawPile[^1];
        State.DrawPile.RemoveAt(State.DrawPile.Count - 1);
        MoveCard(cardId, CardZone.Processing, 0);
        State.ProcessingArea.Add(cardId);
        CommitMutation();
        return State.Cards[cardId];
    }

    private void ReshuffleDiscardPile()
    {
        if (State.DiscardPile.Count == 0)
        {
            return;
        }
        List<string> cards = State.DiscardPile.ToList();
        State.DiscardPile.Clear();
        foreach (string cardId in cards)
        {
            State.Cards[cardId].Zone = CardZone.DrawPile;
        }
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int other = (int)(ConsumeRandom() % (uint)(index + 1));
            (cards[index], cards[other]) = (cards[other], cards[index]);
        }
        State.DrawPile.AddRange(cards);
        CommitMutation();
    }

    private void MoveHandCardToProcessing(int sourceSeatId, string cardId)
    {
        PlayerState source = State.Players[sourceSeatId];
        if (!source.Hand.Remove(cardId))
        {
            throw new InvalidOperationException("Card is not in source hand.");
        }
        MoveCard(cardId, CardZone.Processing, 0);
        State.ProcessingArea.Add(cardId);
        CommitMutation();
    }

    private void MoveHandCardToJudgement(int sourceSeatId, int targetSeatId, string cardId)
    {
        PlayerState source = State.Players[sourceSeatId];
        if (!source.Hand.Remove(cardId))
        {
            throw new InvalidOperationException("Card is not in source hand.");
        }
        State.Players[targetSeatId].JudgementArea.Add(cardId);
        MoveCard(cardId, CardZone.Judgement, targetSeatId);
        CommitMutation();
    }

    private void MoveProcessingCardToJudgement(int targetSeatId, string cardId)
    {
        State.ProcessingArea.Remove(cardId);
        State.Players[targetSeatId].JudgementArea.Add(cardId);
        MoveCard(cardId, CardZone.Judgement, targetSeatId);
        CommitMutation();
    }

    private void MoveProcessingCardToDiscard(string cardId)
    {
        State.ProcessingArea.Remove(cardId);
        State.DiscardPile.Add(cardId);
        MoveCard(cardId, CardZone.DiscardPile, 0);
        CommitMutation();
    }

    private void FinishUsedCard(string cardId)
    {
        if (cardId.StartsWith("virtual-slash-", StringComparison.Ordinal))
        {
            State.ProcessingArea.Remove(cardId);
            State.Cards.Remove(cardId);
            CommitMutation();
            return;
        }
        if (State.Cards[cardId].Zone == CardZone.Processing)
        {
            MoveProcessingCardToDiscard(cardId);
        }
    }

    private void DiscardFromHand(int seatId, string cardId)
    {
        PlayerState player = State.Players[seatId];
        if (!player.Hand.Remove(cardId))
        {
            throw new InvalidOperationException("Discarded card is not in hand.");
        }
        State.DiscardPile.Add(cardId);
        MoveCard(cardId, CardZone.DiscardPile, 0);
        CommitMutation();
        Emit(RuleEventKind.CardsDiscarded, seatId, new[] { seatId }, new CardMoveRuleEventPayload(cardId, CardZone.Hand, CardZone.DiscardPile, seatId));
        if (player.IsAlive
            && State.CurrentSeatId != seatId
            && HasSkill(player, "off_turn_loss_draw")
            && player.Marks.GetValueOrDefault("skill:off_turn_loss_draw:used") == 0)
        {
            player.Marks["skill:off_turn_loss_draw:used"] = 1;
            DrawCards(player, 1);
            Emit(RuleEventKind.SkillTriggered, seatId, new[] { seatId }, new TextRuleEventPayload("skill.off_turn_loss_draw"));
        }
    }

    private void DiscardFirstAvailableCard(int targetSeatId)
    {
        PlayerState target = State.Players[targetSeatId];
        string? cardId = target.Hand.OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault();
        if (cardId is not null)
        {
            DiscardFromHand(targetSeatId, cardId);
            return;
        }
        if (target.Equipment.Count > 0)
        {
            KeyValuePair<EquipmentSlot, string> equipment = target.Equipment.First();
            target.Equipment.Remove(equipment.Key);
            State.DiscardPile.Add(equipment.Value);
            MoveCard(equipment.Value, CardZone.DiscardPile, 0);
            CommitMutation();
            HandleEquipmentLeft(target, equipment.Value);
        }
    }

    private void StealFirstAvailableCard(int sourceSeatId, int targetSeatId)
    {
        PlayerState source = State.Players[sourceSeatId];
        PlayerState target = State.Players[targetSeatId];
        string? cardId = target.Hand.OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault();
        if (cardId is not null)
        {
            target.Hand.Remove(cardId);
            source.Hand.Add(cardId);
            MoveCard(cardId, CardZone.Hand, sourceSeatId);
            CommitMutation();
        }
    }

    private void Equip(int sourceSeatId, string cardId, CardDefinition definition)
    {
        if (definition.EquipmentSlot is null)
        {
            throw new InvalidOperationException("Equipment card has no slot.");
        }
        PlayerState source = State.Players[sourceSeatId];
        if (!source.Hand.Remove(cardId))
        {
            throw new InvalidOperationException("Equipment card is not in hand.");
        }
        if (source.Equipment.Remove(definition.EquipmentSlot.Value, out string? replaced))
        {
            State.DiscardPile.Add(replaced);
            MoveCard(replaced, CardZone.DiscardPile, 0);
            HandleEquipmentLeft(source, replaced);
        }
        source.Equipment[definition.EquipmentSlot.Value] = cardId;
        MoveCard(cardId, CardZone.Equipment, sourceSeatId);
        CommitMutation();
        Emit(RuleEventKind.EquipmentChanged, sourceSeatId, new[] { sourceSeatId }, new CardMoveRuleEventPayload(cardId, CardZone.Hand, CardZone.Equipment, sourceSeatId, sourceSeatId));
    }

    private void HandleEquipmentLeft(PlayerState owner, string cardInstanceId)
    {
        if (owner.IsAlive
            && owner.Health < owner.MaxHealth
            && string.Equals(State.Cards[cardInstanceId].DefinitionId, "silver_lion", StringComparison.Ordinal))
        {
            Heal(owner.SeatId, owner.SeatId, 1);
            Emit(RuleEventKind.SkillTriggered, owner.SeatId, new[] { owner.SeatId }, new TextRuleEventPayload("equipment.silver_lion.left"));
        }
    }

    private void MoveCard(string cardId, CardZone zone, int ownerSeatId)
    {
        CardState card = State.Cards[cardId];
        card.Zone = zone;
        card.OwnerSeatId = ownerSeatId;
        card.IsFaceUp = zone is CardZone.Equipment or CardZone.Judgement or CardZone.Processing or CardZone.DiscardPile;
    }

    private void EmitPhaseChanged(GamePhase phase) =>
        Emit(RuleEventKind.PhaseChanged, State.CurrentSeatId, new[] { State.CurrentSeatId }, new PhaseRuleEventPayload(phase));

    private void Emit(RuleEventKind kind, int sourceSeatId, IEnumerable<int> targetSeatIds, RuleEventPayload payload)
    {
        RuleEvent ruleEvent = new(
            $"event-{++_eventSequence:D8}",
            _eventSequence,
            string.Empty,
            kind,
            RuleEventStage.Created,
            sourceSeatId,
            targetSeatIds.ToArray(),
            payload);
        _resolutionStack.Enqueue(new ResolutionFrame(ruleEvent));
        _resolutionStack.ResetStepBudget();
        while (_resolutionStack.HasWork)
        {
            ResolutionStep step = _resolutionStack.Advance();
            if (step.Progress == ResolutionProgress.Faulted)
            {
                throw new InvalidOperationException(step.Error);
            }
            if (step.Event is not null)
            {
                _stepEvents.Add(step.Event);
                Journal.Append(JournalEntryKind.RuleEvent, State.ComputeCanonicalHash(), step.Event);
            }
        }
    }

    private ulong ConsumeRandom()
    {
        ulong value = _random.NextUInt64();
        State.RandomState = _random.State;
        Journal.Append(JournalEntryKind.RandomConsumed, State.ComputeCanonicalHash(), randomValue: value);
        return value;
    }

    private void CommitMutation()
    {
        State.RandomState = _random.State;
        State.Revision++;
        if (State.PendingChoice is ChoiceRequest pending && pending.StateRevision != State.Revision)
        {
            State.PendingChoice = pending with { StateRevision = State.Revision };
        }
    }

    private EngineStepResult BuildResult(EngineProgress progress, string errorKey = "") => new(
        progress,
        State.Revision,
        _stepEvents.ToArray(),
        State.PendingChoice,
        State.ComputeCanonicalHash(),
        errorKey);

    private IEnumerable<PlayerState> AlivePlayersInSeatOrder(int startingSeatId)
    {
        int start = State.TurnOrder.IndexOf(startingSeatId);
        if (start < 0)
        {
            start = 0;
        }
        for (int offset = 0; offset < State.TurnOrder.Count; offset++)
        {
            PlayerState player = State.Players[State.TurnOrder[(start + offset) % State.TurnOrder.Count]];
            if (player.IsAlive)
            {
                yield return player;
            }
        }
    }

    private int SeatDistance(int sourceSeatId, int targetSeatId)
    {
        List<int> alive = State.TurnOrder.Where(seat => State.Players[seat].IsAlive).ToList();
        int source = alive.IndexOf(sourceSeatId);
        int target = alive.IndexOf(targetSeatId);
        int clockwise = (target - source + alive.Count) % alive.Count;
        int counterClockwise = (source - target + alive.Count) % alive.Count;
        return Math.Min(clockwise, counterClockwise);
    }

    private int EffectiveDistance(int sourceSeatId, int targetSeatId)
    {
        int distance = SeatDistance(sourceSeatId, targetSeatId);
        PlayerState source = State.Players[sourceSeatId];
        PlayerState target = State.Players[targetSeatId];
        if (source.Equipment.Values.Any(cardId => State.Cards[cardId].DefinitionId is "chitu" or "dawan" or "zixing"))
        {
            distance--;
        }
        if (target.Equipment.Values.Any(cardId => State.Cards[cardId].DefinitionId is "jueying" or "dilu" or "zhuahuangfeidian" or "hualiu"))
        {
            distance++;
        }
        return Math.Max(1, distance);
    }

    private int AttackRange(PlayerState player)
    {
        if (!player.Equipment.TryGetValue(EquipmentSlot.Weapon, out string? weaponId))
        {
            return 1;
        }
        return Math.Max(1, _content.Cards[State.Cards[weaponId].DefinitionId].AttackRange);
    }

    private bool CanCardTarget(int sourceSeatId, PlayerState target, CardDefinition definition)
    {
        PlayerState source = State.Players[sourceSeatId];
        int distance = EffectiveDistance(sourceSeatId, target.SeatId);
        bool hasCards = target.Hand.Count + target.Equipment.Count + target.JudgementArea.Count > 0;
        return definition.EffectId switch
        {
            "card.slash" or "card.fire_slash" or "card.thunder_slash" => distance <= AttackRange(source),
            "card.snatch" => distance <= 1 && hasCards,
            "card.dismantle" => hasCards,
            "card.fire_attack" => target.Hand.Count > 0,
            "card.borrow_sword" => target.Equipment.ContainsKey(EquipmentSlot.Weapon)
                && State.Players.Values.Any(victim => victim.IsAlive
                    && victim.SeatId != target.SeatId
                    && EffectiveDistance(target.SeatId, victim.SeatId) <= AttackRange(target)),
            "card.supply_shortage" => distance <= 1 && !HasDelayedTrick(target, definition.EffectId),
            "card.indulgence" => !HasDelayedTrick(target, definition.EffectId),
            "card.iron_chain" => true,
            _ => true
        };
    }

    private bool HasDelayedTrick(PlayerState player, string effectId) => player.JudgementArea
        .Any(cardId => string.Equals(_content.Cards[State.Cards[cardId].DefinitionId].EffectId, effectId, StringComparison.Ordinal));

    private bool CanUseCard(PlayerState source, CardDefinition definition)
    {
        if (definition.EffectId is "card.dodge" or "card.nullification")
        {
            return false;
        }
        if (definition.EffectId == "card.peach")
        {
            return source.Health < source.MaxHealth;
        }
        if (definition.EffectId == "card.wine")
        {
            return source.WineUsesThisTurn == 0;
        }
        if (definition.EffectId is "card.slash" or "card.fire_slash" or "card.thunder_slash")
        {
            if (source.SlashUsesThisTurn > 0 && !HasEquippedCard(source, "crossbow"))
            {
                return false;
            }
        }
        if (definition.EffectId == "card.lightning")
        {
            return !HasDelayedTrick(source, definition.EffectId);
        }
        if (definition.EffectId is "card.peach_garden" or "card.harvest")
        {
            return State.Players.Values.Any(player => player.IsAlive);
        }
        if (definition.EffectId is "card.barbarians" or "card.arrow_barrage")
        {
            return State.Players.Values.Any(player => player.IsAlive && player.SeatId != source.SeatId);
        }
        if (definition.EffectId is "card.slash" or "card.fire_slash" or "card.thunder_slash"
            or "card.duel" or "card.dismantle" or "card.snatch" or "card.fire_attack"
            or "card.borrow_sword" or "card.indulgence" or "card.supply_shortage" or "card.iron_chain")
        {
            return State.Players.Values.Any(target => target.IsAlive
                && (definition.EffectId is "card.iron_chain" or "card.fire_attack" || target.SeatId != source.SeatId)
                && CanCardTarget(source.SeatId, target, definition));
        }
        return true;
    }

    private bool IsSlash(CardState card) => _content.Cards[card.DefinitionId].EffectId is "card.slash" or "card.fire_slash" or "card.thunder_slash";

    private bool HasEquippedCard(PlayerState player, string definitionId) => player.Equipment.Values
        .Any(instanceId => string.Equals(State.Cards[instanceId].DefinitionId, definitionId, StringComparison.Ordinal));

    private bool SlashIgnoresArmor(int sourceSeatId, string cardInstanceId) =>
        IsSlash(State.Cards[cardInstanceId])
        && HasEquippedCard(State.Players[sourceSeatId], "qinggang_sword");

    private bool HasSkill(PlayerState player, string skillId) => player.SkillIds.Contains(skillId);

    private bool CanUseActiveSkill(PlayerState player, SkillDefinitionV2 skill)
    {
        if (player.Marks.GetValueOrDefault($"skill:{skill.SkillId}:used") > 0)
        {
            return false;
        }
        if (skill.SkillId == "limit_break")
        {
            return player.Marks.GetValueOrDefault("skill:limit_break:used") == 0;
        }
        if (skill.SkillId == "blade_conversion")
        {
            return (player.SlashUsesThisTurn == 0 || HasEquippedCard(player, "crossbow"))
                && player.Hand.Any(cardId => _content.Cards[State.Cards[cardId].DefinitionId].Category != CardCategory.Basic)
                && State.Players.Values.Any(target => target.IsAlive
                    && target.SeatId != player.SeatId
                    && EffectiveDistance(player.SeatId, target.SeatId) <= AttackRange(player));
        }
        if (skill.SkillId == "ally_supply")
        {
            return player.Hand.Count > 0 && State.Players.Values.Any(target => target.IsAlive
                && target.SeatId != player.SeatId
                && _mode.GetAttitude(State, player.SeatId, target.SeatId) > 0);
        }
        if (skill.SkillId == "discard_strike")
        {
            return player.Hand.Count > 0 && State.Players.Values.Any(target => target.IsAlive && target.SeatId != player.SeatId);
        }
        return player.Hand.Count > 0;
    }

    private void MarkActiveSkillUsed(PlayerState player, string skillId)
    {
        SkillDefinitionV2 skill = _content.Skills[skillId];
        if (skill.UsageScope != SkillUsageScope.Unlimited)
        {
            player.Marks[$"skill:{skillId}:used"] = 1;
            CommitMutation();
        }
    }

    private double CardUseValue(string cardId) => _content.Cards[State.Cards[cardId].DefinitionId].AiValues?.GetValueOrDefault("keep") ?? 0;

    private static int ParseSeatOption(string optionId) => optionId.StartsWith("seat:", StringComparison.Ordinal)
        && int.TryParse(optionId["seat:".Length..], out int seat)
            ? seat
            : throw new InvalidOperationException($"Invalid seat option: {optionId}");

    private static string ParseCardOption(string optionId) => optionId.StartsWith("card:", StringComparison.Ordinal)
        ? optionId["card:".Length..]
        : throw new InvalidOperationException($"Invalid card option: {optionId}");
}
