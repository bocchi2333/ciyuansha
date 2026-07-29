using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.Networking;
using Godot;
using CoreGameView = CiyuanSha.GameCore.Domain.GameView;
using CoreViewerContext = CiyuanSha.GameCore.Domain.ViewerContext;
using CoreViewerRole = CiyuanSha.GameCore.Domain.ViewerRole;

namespace CiyuanSha.Gameplay.Core;

/// <summary>
/// Godot scene, network and presentation adapter. Protocol-V2 rules are owned
/// exclusively by <see cref="GameCoreRuntime"/>.
/// </summary>
public partial class GameManager : Node
{
	public static GameManager? Instance { get; private set; }

	[Export]
	public NodePath ActionManagerPath { get; set; } = new NodePath();

	[Export]
	public int OpeningHandSize { get; set; } = 4;

	[Export]
	public int DrawCardsPerTurn { get; set; } = 2;

	[Export]
	public bool EnableFactionVictoryRules { get; set; }

	public ActionManager? ActionManager => _actionManager;

	/// <summary>The host-owned deterministic rules runtime used by protocol V2.</summary>
	public GameCoreRuntime? CoreRuntime { get; private set; }

	/// <summary>True while all rule state comes from GameCore views.</summary>
	public bool IsCoreMatchActive { get; private set; }

	public TurnPhase CurrentPhase { get; private set; } = TurnPhase.TurnStart;

	public int CurrentTurnPeerId { get; private set; } = 1;

	public int WinnerPeerId { get; private set; }

	public string ResultMessage { get; private set; } = string.Empty;

	public bool IsMatchRunning { get; private set; }

	public bool IsAwaitingPlayerInput => CurrentPhase == TurnPhase.PlayPhase && _isWaitingForPlayInput;

	public bool IsAwaitingDiscardInput => CurrentPhase == TurnPhase.DiscardPhase && _isWaitingForDiscardInput;

	public bool HasPendingResponseWindow { get; private set; }

	public int PendingResponsePeerId { get; private set; }

	public string PendingResponsePrompt { get; private set; } = string.Empty;

	public ResponseWindowKind PendingResponseKind { get; private set; }

	public int PendingHarvestPeerId { get; private set; }

	public string PendingHarvestPrompt { get; private set; } = string.Empty;

	public bool HasPendingHarvestSelection => PendingHarvestPeerId > 0 && _harvestPool.Count > 0;

	public IReadOnlyList<CardInstance> HarvestPool => _harvestPool;

	public int PendingTargetCardSelectionPeerId { get; private set; }

	public string PendingTargetCardSelectionPrompt { get; private set; } = string.Empty;

	public bool HasPendingTargetCardSelection => PendingTargetCardSelectionPeerId > 0 && _targetCardSelectionPool.Count > 0;

	public IReadOnlyList<CardInstance> TargetCardSelectionPool => _targetCardSelectionPool;

	public int PendingHandCardSelectionPeerId { get; private set; }

	public string PendingHandCardSelectionPrompt { get; private set; } = string.Empty;

	public bool HasPendingHandCardSelection => PendingHandCardSelectionPeerId > 0 && _handCardSelectionPool.Count > 0;

	public IReadOnlyList<CardInstance> HandCardSelectionPool => _handCardSelectionPool;

	public bool CanDeclineHandCardSelection { get; private set; }

	public int SlashUsesThisTurn { get; private set; }

	public int WineUsesThisTurn { get; private set; }

	public int DrawPileCount => _drawPile.Count;

	public int DiscardPileCount => _discardPile.Count;

	public IReadOnlyList<NetworkBattleLogEntry> BattleLog => _battleLog;

	public CardInstance? CurrentResolvedResponseCard => _currentResolvedResponseCard?.Clone();

	public void PushCardToDrawPileTopForTest(CardInstance card)
	{
		if (card is null)
		{
			return;
		}

		_drawPile.Add(card.Clone());
		RequestStateSync();
	}

	public event Action<TurnPhase>? OnPhaseChanged;

	public event Action? OnPlayPhaseInputRequested;

	public event Action? OnDiscardPhaseInputRequested;

	public event Action<int>? OnTurnOwnerChanged;

	public event Action<bool>? OnMatchRunningChanged;

	public event Action<int, string>? OnMatchEnded;

	public event Action? OnStateSyncRequested;

	public event Action? OnBattleLogChanged;

	public event Action? OnStateChanged;

	public event Action? OnResponseWindowChanged;

	public event Action<CardUseContext, CardUseStage, Node?>? OnCardUseStageChanged;

	public event Action<RuleEventContext>? OnRuleEvent;

	public event Action<NetworkPresentationEvent>? OnPresentationEvent;

	public event Action<EngineStepResult>? OnCoreEngineAdvanced;

	private ActionManager? _actionManager;
	private readonly Dictionary<int, PlayerCharacter> _peerCharacters = new();
	private readonly List<int> _turnOrder = new();
	private readonly List<NetworkBattleLogEntry> _battleLog = new();
	private readonly List<NetworkPresentationEvent> _presentationEvents = new();
	private readonly List<CardInstance> _drawPile = new();
	private readonly List<CardInstance> _discardPile = new();
	private readonly List<CardInstance> _harvestPool = new();
	private readonly List<CardInstance> _targetCardSelectionPool = new();
	private readonly List<CardInstance> _handCardSelectionPool = new();
	private readonly Queue<int> _pendingHarvestPeerOrder = new();
	private readonly HashSet<int> _skipDrawPhasePeerIds = new();
	private readonly HashSet<int> _skipPlayPhasePeerIds = new();
	private readonly Random _random = new();
	private bool _isWaitingForPlayInput;
	private bool _isWaitingForDiscardInput;
	private bool _hasQueuedPlayPhaseAction;
	private int _currentTurnIndex;
	private int _nextBattleLogSequence = 1;
	private int _nextPresentationEventSequence = 1;
	private int _lastAppliedPresentationSequence;
	private bool _presentationSnapshotInitialized;
	private int _nextCardInstanceSequence = 1;
	private DamageAction? _pendingResponseDamageAction;
	private DamageTargetingEventArgs? _pendingDamageTargetingArgs;
	private CardType _pendingRequiredResponseCardType = CardType.None;
	private Action<PlayerCharacter>? _pendingRequiredResponseSuccess;
	private Action<PlayerCharacter>? _pendingRequiredResponseDecline;
	private CardInstance? _currentResolvedResponseCard;
	private Action<PlayerCharacter>? _pendingNullificationSuccess;
	private Action? _pendingNullificationDecline;
	private string _pendingNullificationSubjectName = string.Empty;
	private readonly List<int> _pendingNullificationResponseOrder = new();
	private int _pendingNullificationResponseIndex;
	private PlayerCharacter? _pendingTargetCardSelectionSource;
	private PlayerCharacter? _pendingTargetCardSelectionTarget;
	private Action<PlayerCharacter, PlayerCharacter, CardInstance, bool>? _pendingTargetCardSelectionResolved;
	private PlayerCharacter? _pendingHandCardSelectionCharacter;
	private Action<PlayerCharacter, CardInstance>? _pendingHandCardSelectionResolved;
	private Action<PlayerCharacter>? _pendingHandCardSelectionDeclined;
	private bool _pendingHandCardSelectionConsumesCard = true;
	private PlayerCharacter? _pendingDyingCharacter;
	private Node? _pendingDyingSource;
	private readonly List<int> _pendingDyingResponseOrder = new();
	private int _pendingDyingResponseIndex;

	public override void _Ready()
	{
		if (Instance is not null && Instance != this)
		{
			GD.PushWarning("Duplicate GameManager detected. The newer instance will be freed.");
			QueueFree();
			return;
		}

		Instance = this;
		_actionManager = ResolveActionManager();
		try
		{
			CoreRuntime = GameCoreRuntime.LoadDefault();
		}
		catch (Exception exception)
		{
			GD.PushError($"GameCore content initialization failed: {exception.Message}");
			CoreRuntime = null;
		}
	}

	public EngineStepResult BeginCoreMatch(MatchConfig config)
	{
		if (CoreRuntime is null)
		{
			throw new InvalidOperationException("GameCore runtime is unavailable.");
		}

		EngineStepResult result = CoreRuntime.Start(config);
		IsCoreMatchActive = true;
		ApplyCoreViewToPresentation(BuildLocalCoreView());
		OnCoreEngineAdvanced?.Invoke(result);
		RequestStateSync();
		return result;
	}

	public EngineStepResult SubmitCoreChoice(int seatId, string reconnectToken, ChoiceResult result)
	{
		if (CoreRuntime is null)
		{
			throw new InvalidOperationException("GameCore runtime is unavailable.");
		}

		EngineStepResult step = CoreRuntime.SubmitChoice(seatId, reconnectToken, result);
		if (step.Progress != EngineProgress.Rejected)
		{
			ApplyCoreViewToPresentation(BuildLocalCoreView());
		}
		OnCoreEngineAdvanced?.Invoke(step);
		if (step.Progress != EngineProgress.Rejected)
		{
			RequestStateSync();
		}
		return step;
	}

	public void ApplyCoreViewSnapshot(CoreGameView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		IsCoreMatchActive = true;
		ApplyCoreViewToPresentation(view);
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _Process(double delta)
	{
		if (IsCoreMatchActive || !ShouldProcessGameFlow() || !IsMatchRunning)
		{
			return;
		}

		if (_actionManager is null)
		{
			_actionManager = ResolveActionManager();
			if (_actionManager is null)
			{
				return;
			}
		}

		switch (CurrentPhase)
		{
			case TurnPhase.TurnStart:
			case TurnPhase.JudgementPhase:
				if (!_actionManager.HasPendingActions)
				{
					AdvanceToNextPhase();
				}
				break;

			case TurnPhase.DrawPhase:
				if (!_actionManager.HasPendingActions)
				{
					AdvanceToNextPhase();
				}
				break;

			case TurnPhase.PlayPhase:
				HandlePlayPhase();
				break;

			case TurnPhase.DiscardPhase:
				if (!_isWaitingForDiscardInput && !_actionManager.HasPendingActions)
				{
					AdvanceToNextPhase();
				}
				break;

			case TurnPhase.EndPhase:
				if (!_actionManager.HasPendingActions)
				{
					AdvanceToNextPhase();
				}
				break;
		}
	}

	public void StartTurn()
	{
		if (!IsMatchRunning)
		{
			return;
		}

		EnterPhase(TurnPhase.TurnStart);
	}

	public void BeginMatch(IEnumerable<int> turnOrder, int startingPeerId)
	{
		_turnOrder.Clear();

		foreach (int peerId in turnOrder.Distinct())
		{
			if (peerId > 0)
			{
				_turnOrder.Add(peerId);
			}
		}

		if (_turnOrder.Count == 0)
		{
			_turnOrder.Add(Math.Max(1, startingPeerId));
		}

		IsMatchRunning = true;
		WinnerPeerId = 0;
		ResultMessage = string.Empty;
		SlashUsesThisTurn = 0;
		WineUsesThisTurn = 0;
		_battleLog.Clear();
		_nextBattleLogSequence = 1;
		_presentationEvents.Clear();
		_nextPresentationEventSequence = 1;
		_lastAppliedPresentationSequence = 0;
		_presentationSnapshotInitialized = false;
		_skipDrawPhasePeerIds.Clear();
		_skipPlayPhasePeerIds.Clear();

		InitializeDecksForMatch();
		ResetCharactersForMatch();
		AssignDefaultFactions();
		DealOpeningHands();
		ResetPendingDyingState();
		ClearResponseWindow();
		ClearHarvestSelection(discardRemainingCards: false);
		ClearTargetCardSelection();
		ClearHandCardSelection();

		SetCurrentTurnPeerId(startingPeerId <= 0 ? _turnOrder[0] : startingPeerId);
		AddBattleLog("Match started.");
		EmitRuleEvent(RuleEventType.MatchStarted, actingPeerId: CurrentTurnPeerId, message: "Match started.");
		OnMatchRunningChanged?.Invoke(true);
		StartTurn();
		RequestStateSync();
	}

	/// <summary>
	/// Starts only the scene-side representation of a V2 match. It intentionally
	/// does not create decks, deal cards, assign roles, or advance phases.
	/// </summary>
	public void BeginCorePresentationMatch(IEnumerable<int> turnOrder, int startingPeerId)
	{
		_turnOrder.Clear();
		_turnOrder.AddRange(turnOrder.Where(peerId => peerId > 0).Distinct());
		CurrentTurnPeerId = Math.Max(1, startingPeerId);
		_currentTurnIndex = Math.Max(0, _turnOrder.IndexOf(CurrentTurnPeerId));
		IsCoreMatchActive = true;
		IsMatchRunning = CoreRuntime?.Engine?.State.Status == CiyuanSha.GameCore.Domain.MatchStatus.Running
			|| LanMultiplayerManager.Instance?.LastMatchStateSnapshot?.CoreView?.Status == CiyuanSha.GameCore.Domain.MatchStatus.Running;
		WinnerPeerId = 0;
		ResultMessage = string.Empty;
		ResetLegacyWaitingState();
		ApplyCoreViewToPresentation(BuildLocalCoreView());
		OnMatchRunningChanged?.Invoke(IsMatchRunning);
		OnStateChanged?.Invoke();
		RequestStateSync();
	}

	private void AssignDefaultFactions()
	{
		foreach (PlayerCharacter character in _peerCharacters.Values)
		{
			if (character.Faction == PlayerFaction.None)
			{
				character.SetFaction(PlayerFaction.Neutral);
			}
		}
	}

	public void EndMatch(int winnerPeerId = 0, string resultMessage = "")
	{
		IsCoreMatchActive = false;
		IsMatchRunning = false;
		_isWaitingForPlayInput = false;
		_isWaitingForDiscardInput = false;
		_hasQueuedPlayPhaseAction = false;
		ResetPendingDyingState();
		ClearResponseWindow();
		ClearHarvestSelection(discardRemainingCards: false);
		ClearTargetCardSelection();
		ClearHandCardSelection();
		_skipDrawPhasePeerIds.Clear();
		_skipPlayPhasePeerIds.Clear();
		WinnerPeerId = Math.Max(0, winnerPeerId);
		ResultMessage = resultMessage ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(ResultMessage))
		{
			AddBattleLog(ResultMessage);
		}

		EmitRuleEvent(RuleEventType.MatchEnded, actingPeerId: WinnerPeerId, message: ResultMessage);

		_turnOrder.Clear();
		_currentTurnIndex = 0;
		CurrentTurnPeerId = 1;
		SlashUsesThisTurn = 0;
		WineUsesThisTurn = 0;
		OnMatchRunningChanged?.Invoke(false);
		OnMatchEnded?.Invoke(WinnerPeerId, ResultMessage);
		RequestStateSync();
	}

	public void SetCurrentTurnPeerId(int peerId)
	{
		CurrentTurnPeerId = Math.Max(1, peerId);
		_currentTurnIndex = Math.Max(0, _turnOrder.IndexOf(CurrentTurnPeerId));
		OnTurnOwnerChanged?.Invoke(CurrentTurnPeerId);
	}

	public void RegisterPeerCharacter(int peerId, PlayerCharacter character)
	{
		if (character is null)
		{
			GD.PushWarning("Tried to register a null PlayerCharacter.");
			return;
		}

		if (_peerCharacters.TryGetValue(peerId, out PlayerCharacter? existingCharacter))
		{
			existingCharacter.OnStatsChanged -= HandleCharacterStatsChanged;
			existingCharacter.OnDefeated -= HandleCharacterDefeated;
			existingCharacter.OnHandCardRemoved -= HandleHandCardRemoved;
		}

		character.OwnerPeerId = peerId;
		_peerCharacters[peerId] = character;
		character.OnStatsChanged += HandleCharacterStatsChanged;
		character.OnDefeated += HandleCharacterDefeated;
		character.OnHandCardRemoved += HandleHandCardRemoved;
		if (IsCoreMatchActive)
		{
			ApplyCoreViewToPresentation(BuildLocalCoreView());
		}
		RequestStateSync();
	}

	public bool TrySetCharacterFaction(int peerId, PlayerFaction faction)
	{
		if (!_peerCharacters.TryGetValue(peerId, out PlayerCharacter? character))
		{
			return false;
		}

		character.SetFaction(faction);
		RequestStateSync();
		return true;
	}

	public bool CanActivateSkill(int actingPeerId, string skillId, IEnumerable<int>? targetPeerIds, string cardInstanceId, out string reason)
	{
		reason = string.Empty;
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			reason = $"No source PlayerCharacter registered for peer {actingPeerId}.";
			return false;
		}

		CharacterSkill? skill = sourceCharacter.Skills.FirstOrDefault(skill => string.Equals(skill.SkillId, skillId, StringComparison.OrdinalIgnoreCase));
		if (skill is null)
		{
			reason = $"{sourceCharacter.CharacterName} does not have skill {skillId}.";
			return false;
		}

		NetworkPlayCommand command = new()
		{
			SkillId = skillId ?? string.Empty,
			CardInstanceId = cardInstanceId ?? string.Empty,
			TargetPeerIds = targetPeerIds?.Where(peerId => peerId > 0).Distinct().ToList() ?? new List<int>()
		};
		SkillActivationContext activationContext = new(sourceCharacter, ResolveSkillActivationTargets(command), command.CardInstanceId);
		if (!ValidateSkillActivationShape(skill, activationContext, out reason))
		{
			return false;
		}

		return skill.CanActivate(this, activationContext, out reason);
	}

	public void AssignIdentityFactionsByTurnOrder(bool shuffleNonLordFactions = true)
	{
		if (_turnOrder.Count == 0)
		{
			return;
		}

		List<PlayerFaction> factions = BuildIdentityFactionLayout(_turnOrder.Count);
		if (shuffleNonLordFactions && factions.Count > 2)
		{
			ShuffleList(factions, startIndex: 1);
		}

		for (int index = 0; index < _turnOrder.Count; index++)
		{
			if (_peerCharacters.TryGetValue(_turnOrder[index], out PlayerCharacter? character))
			{
				character.SetFaction(factions[Math.Min(index, factions.Count - 1)]);
			}
		}

		RequestStateSync();
	}

	public void UnregisterPeerCharacter(int peerId)
	{
		if (_peerCharacters.Remove(peerId, out PlayerCharacter? character))
		{
			character.OnStatsChanged -= HandleCharacterStatsChanged;
			character.OnDefeated -= HandleCharacterDefeated;
			character.OnHandCardRemoved -= HandleHandCardRemoved;
			RequestStateSync();
		}
	}

	public bool TryHandleNetworkPlayCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (command is null)
		{
			return false;
		}

		if (command.CommandType is NetworkPlayCommandType.RespondDodge
			or NetworkPlayCommandType.RespondPeach
			or NetworkPlayCommandType.RespondSlash
			or NetworkPlayCommandType.RespondNullification
			or NetworkPlayCommandType.DeclineResponse)
		{
			return TryHandleResponseCommand(actingPeerId, command);
		}

		if (HasPendingHarvestSelection)
		{
			if (actingPeerId != PendingHarvestPeerId)
			{
				GD.PushWarning($"Rejected Harvest choice from peer {actingPeerId} because peer {PendingHarvestPeerId} is choosing.");
				return false;
			}

			return command.CommandType == NetworkPlayCommandType.ChooseHarvestCard
				? TryChooseHarvestCardCommand(actingPeerId, command)
				: RejectUnsupportedCommand(command.CommandType);
		}

		if (HasPendingTargetCardSelection)
		{
			if (actingPeerId != PendingTargetCardSelectionPeerId)
			{
				GD.PushWarning($"Rejected target-card choice from peer {actingPeerId} because peer {PendingTargetCardSelectionPeerId} is choosing.");
				return false;
			}

			return command.CommandType == NetworkPlayCommandType.ChooseTargetCard
				? TryChooseTargetCardCommand(actingPeerId, command)
				: RejectUnsupportedCommand(command.CommandType);
		}

		if (HasPendingHandCardSelection)
		{
			if (actingPeerId != PendingHandCardSelectionPeerId)
			{
				GD.PushWarning($"Rejected hand-card choice from peer {actingPeerId} because peer {PendingHandCardSelectionPeerId} is choosing.");
				return false;
			}

			return command.CommandType switch
			{
				NetworkPlayCommandType.ChooseHandCard => TryChooseHandCardCommand(actingPeerId, command),
				NetworkPlayCommandType.DeclineHandCardSelection => TryDeclineHandCardSelectionCommand(actingPeerId),
				_ => RejectUnsupportedCommand(command.CommandType)
			};
		}

		if (CurrentPhase == TurnPhase.DiscardPhase)
		{
			if (actingPeerId != CurrentTurnPeerId)
			{
				GD.PushWarning($"Rejected discard command from peer {actingPeerId} because it is currently peer {CurrentTurnPeerId}'s turn.");
				return false;
			}

			return command.CommandType == NetworkPlayCommandType.DiscardCard
				? TryDiscardCardCommand(actingPeerId, command)
				: RejectUnsupportedCommand(command.CommandType);
		}

		if (!IsMatchRunning)
		{
			GD.PushWarning("Rejected network play command because no match is running.");
			return false;
		}

		if (CurrentPhase != TurnPhase.PlayPhase)
		{
			GD.PushWarning("Rejected network play command because the game is not in PlayPhase.");
			return false;
		}

		if (actingPeerId != CurrentTurnPeerId)
		{
			GD.PushWarning($"Rejected network play command from peer {actingPeerId} because it is currently peer {CurrentTurnPeerId}'s turn.");
			return false;
		}

		return command.CommandType switch
		{
			NetworkPlayCommandType.BasicAttack => TrySubmitBasicAttackCommand(actingPeerId, command),
			NetworkPlayCommandType.EndPlayPhase => EndPlayPhaseAndReturnTrue(),
			NetworkPlayCommandType.UsePeach => TryUsePeachCommand(actingPeerId, command),
			NetworkPlayCommandType.UseEquipment => TryUseEquipmentCommand(actingPeerId, command),
			NetworkPlayCommandType.UseCard => TryUseGenericCardCommand(actingPeerId, command),
			NetworkPlayCommandType.RecastCard => TryRecastCardCommand(actingPeerId, command),
			NetworkPlayCommandType.ActivateSkill => TryActivateSkillCommand(actingPeerId, command),
			_ => RejectUnsupportedCommand(command.CommandType)
		};
	}

	public bool SubmitPlayPhaseAction(GameAction action)
	{
		if (action is null)
		{
			GD.PushWarning("Tried to submit a null play phase action.");
			return false;
		}

		if (_actionManager is null)
		{
			GD.PushWarning("ActionManager is not ready, so the play phase action was rejected.");
			return false;
		}

		if (CurrentPhase != TurnPhase.PlayPhase)
		{
			GD.PushWarning("Play phase action was submitted outside of PlayPhase.");
			return false;
		}

		_actionManager.AddToBottom(action);
		_isWaitingForPlayInput = false;
		_hasQueuedPlayPhaseAction = true;
		RequestStateSync();
		return true;
	}

	public int GetDistanceBetween(int sourcePeerId, int targetPeerId)
	{
		if (sourcePeerId <= 0 || targetPeerId <= 0 || sourcePeerId == targetPeerId)
		{
			return 0;
		}

		List<int> aliveTurnOrder = GetAliveTurnOrder();
		if (aliveTurnOrder.Count <= 1)
		{
			return 0;
		}

		int sourceIndex = aliveTurnOrder.IndexOf(sourcePeerId);
		int targetIndex = aliveTurnOrder.IndexOf(targetPeerId);
		if (sourceIndex < 0 || targetIndex < 0)
		{
			return int.MaxValue;
		}

		int clockwise = (targetIndex - sourceIndex + aliveTurnOrder.Count) % aliveTurnOrder.Count;
		int counterClockwise = (sourceIndex - targetIndex + aliveTurnOrder.Count) % aliveTurnOrder.Count;
		int seatDistance = Math.Min(clockwise, counterClockwise);
		int attackAdjustment = _peerCharacters.TryGetValue(sourcePeerId, out PlayerCharacter? source)
			? source.AttackDistanceModifier
			: 0;
		int defenseAdjustment = _peerCharacters.TryGetValue(targetPeerId, out PlayerCharacter? target)
			? target.DefenseDistanceModifier
			: 0;
		return Math.Max(1, seatDistance - attackAdjustment + defenseAdjustment);
	}

	public IReadOnlyList<PlayerCharacter> GetAliveCharacters()
	{
		return _peerCharacters.Values
			.Where(character => character.IsAlive)
			.OrderBy(character => GetTurnOrderDistance(CurrentTurnPeerId, character.OwnerPeerId))
			.ToList();
	}

	public bool CanUseSlashOnTarget(PlayerCharacter? source, PlayerCharacter? target, out string reason)
	{
		reason = string.Empty;

		if (source is null || target is null)
		{
			reason = "Source and target are required.";
			return false;
		}

		if (!IsMatchRunning)
		{
			reason = "No match is currently running.";
			return false;
		}

		if (!source.IsAlive)
		{
			reason = $"{source.CharacterName} is defeated.";
			return false;
		}

		if (!target.IsAlive)
		{
			reason = $"{target.CharacterName} is already defeated.";
			return false;
		}

		if (source == target || source.OwnerPeerId == target.OwnerPeerId)
		{
			reason = "Slash must target another character.";
			return false;
		}

		if (CurrentPhase != TurnPhase.PlayPhase)
		{
			reason = "Slash can only be used during PlayPhase.";
			return false;
		}

		if (source.OwnerPeerId != CurrentTurnPeerId)
		{
			reason = "It is not this character's turn.";
			return false;
		}

		int distance = GetDistanceBetween(source.OwnerPeerId, target.OwnerPeerId);
		int attackRange = source.EffectiveAttackRange;
		if (distance == int.MaxValue)
		{
			reason = "Target is not part of the active turn order.";
			return false;
		}

		if (distance > attackRange)
		{
			reason = $"Target is out of range. Distance {distance}, range {attackRange}.";
			return false;
		}

		if (source.OwnerPeerId == CurrentTurnPeerId && SlashUsesThisTurn >= 1 && !source.CanUseUnlimitedSlash)
		{
			reason = "You have already used Slash this turn.";
			return false;
		}

		return true;
	}

	public bool CanUseForcedSlashOnTarget(PlayerCharacter? source, PlayerCharacter? target, out string reason)
	{
		reason = string.Empty;

		if (source is null || target is null)
		{
			reason = "Source and target are required.";
			return false;
		}

		if (!IsMatchRunning)
		{
			reason = "No match is currently running.";
			return false;
		}

		if (!source.IsAlive)
		{
			reason = $"{source.CharacterName} is defeated.";
			return false;
		}

		if (!target.IsAlive)
		{
			reason = $"{target.CharacterName} is already defeated.";
			return false;
		}

		if (source == target || source.OwnerPeerId == target.OwnerPeerId)
		{
			reason = "Slash must target another character.";
			return false;
		}

		int distance = GetDistanceBetween(source.OwnerPeerId, target.OwnerPeerId);
		if (distance == int.MaxValue)
		{
			reason = "Target is not part of the active turn order.";
			return false;
		}

		int attackRange = source.EffectiveAttackRange;
		if (distance > attackRange)
		{
			reason = $"Target is out of range. Distance {distance}, range {attackRange}.";
			return false;
		}

		return true;
	}

	public bool HasPendingResponseForAction(DamageAction action)
	{
		return action is not null
			&& HasPendingResponseWindow
			&& PendingResponseKind == ResponseWindowKind.Dodge
			&& _pendingResponseDamageAction == action
			&& _pendingDamageTargetingArgs is not null;
	}

	public bool HasPendingDyingResolution(PlayerCharacter? targetCharacter)
	{
		return targetCharacter is not null
			&& _pendingDyingCharacter == targetCharacter
			&& targetCharacter.IsDying;
	}

	public bool TryBeginManualDamageResponse(DamageAction action, PlayerCharacter targetCharacter, DamageTargetingEventArgs args)
	{
		if (action is null || targetCharacter is null || args is null)
		{
			return false;
		}

		if (VineArmorMakesDamageIneffective(args.DamageInfo, targetCharacter))
		{
			args.CancelDamage();
			AddBattleLog($"{targetCharacter.CharacterName}'s Vine Armor makes Slash ineffective.");
			EmitRuleEvent(
				RuleEventType.DamageApplied,
				args.DamageInfo.Source,
				targetCharacter,
				damageInfo: new DamageInfo(args.DamageInfo.Source, targetCharacter, 0, args.DamageInfo.Type, args.DamageInfo.AllowsDodgeResponse, args.DamageInfo.IsChainTransfer, args.DamageInfo.CauseCardType),
				value: 0,
				message: $"{targetCharacter.CharacterName}'s Vine Armor makes Slash ineffective.");
			return false;
		}

		if (TryJudgeEightDiagramDodge(targetCharacter))
		{
			args.RespondWithDodge(new DodgeAction(targetCharacter));
			return false;
		}

		if (targetCharacter.FindFirstHandCardOfType(CardType.Dodge) is null)
		{
			return false;
		}

		_pendingResponseDamageAction = action;
		_pendingDamageTargetingArgs = args;
		BeginResponseWindow(targetCharacter.OwnerPeerId, $"Play Dodge for {targetCharacter.CharacterName} or pass.", ResponseWindowKind.Dodge);
		return true;
	}

	private static bool VineArmorMakesDamageIneffective(DamageInfo damageInfo, PlayerCharacter targetCharacter)
	{
		if (!targetCharacter.HasVineArmor
			|| damageInfo.CauseCardType != CardType.Slash
			|| damageInfo.Type != DamageType.Physical)
		{
			return false;
		}

		return damageInfo.Source is not PlayerCharacter sourceCharacter || !sourceCharacter.IgnoresTargetArmor;
	}

	public bool ResolveDyingCharacter(PlayerCharacter? targetCharacter, Node? damageSource = null)
	{
		if (targetCharacter is null || !targetCharacter.IsDying)
		{
			return false;
		}

		AddBattleLog($"{targetCharacter.CharacterName} enters dying state.");
		EmitRuleEvent(RuleEventType.DyingEntered, damageSource, targetCharacter, message: $"{targetCharacter.CharacterName} enters dying state.");

		_pendingDyingCharacter = targetCharacter;
		_pendingDyingSource = damageSource;
		ResetPendingDyingResponseOrder(CurrentTurnPeerId);
		return AdvancePendingDyingResponse();
	}

	private void ResetPendingDyingResponseOrder(int startPeerId)
	{
		_pendingDyingResponseOrder.Clear();
		_pendingDyingResponseOrder.AddRange(GetDyingResponseOrder(startPeerId).Select(character => character.OwnerPeerId));
		_pendingDyingResponseIndex = 0;
	}

	private bool AdvancePendingDyingResponse()
	{
		if (_pendingDyingCharacter is null)
		{
			return false;
		}

		if (!_pendingDyingCharacter.IsDying)
		{
			FinishPendingDyingResolution(true);
			return false;
		}

		while (_pendingDyingResponseIndex < _pendingDyingResponseOrder.Count)
		{
			int responderPeerId = _pendingDyingResponseOrder[_pendingDyingResponseIndex++];
			if (!_peerCharacters.TryGetValue(responderPeerId, out PlayerCharacter? rescuer) || rescuer.IsDefeated)
			{
				continue;
			}

			bool canUseWineForSelf = rescuer.OwnerPeerId == _pendingDyingCharacter.OwnerPeerId
				&& rescuer.FindFirstHandCardOfType(CardType.Wine) is not null;
			if (rescuer.FindFirstHandCardOfType(CardType.Peach) is null && !canUseWineForSelf)
			{
				continue;
			}

			string prompt = rescuer.OwnerPeerId == _pendingDyingCharacter.OwnerPeerId
				? $"Play Peach or Wine for {_pendingDyingCharacter.CharacterName} or pass."
				: $"Play Peach to save {_pendingDyingCharacter.CharacterName} or pass.";
			BeginResponseWindow(responderPeerId, prompt, ResponseWindowKind.Peach);
			return true;
		}

		FinishPendingDyingResolution(false);
		return false;
	}

	private void FinishPendingDyingResolution(bool survived)
	{
		PlayerCharacter? targetCharacter = _pendingDyingCharacter;
		Node? damageSource = _pendingDyingSource;

		ResetPendingDyingState();
		ClearResponseWindow();

		if (survived || targetCharacter is null)
		{
			EmitRuleEvent(RuleEventType.DyingResolved, targetCharacter, targetCharacter, value: targetCharacter?.CurrentHealth ?? 0, message: "Dying state resolved.");
			RequestStateSync();
			return;
		}

		IReadOnlyList<CardInstance> defeatedCards = targetCharacter.RemoveAllCardsAndEquipment();
		foreach (CardInstance defeatedCard in defeatedCards)
		{
			DiscardCardToPile(defeatedCard);
		}

		if (defeatedCards.Count > 0)
		{
			AddBattleLog($"{targetCharacter.CharacterName} discards {defeatedCards.Count} card(s) on defeat.");
			EmitCardsLost(targetCharacter, targetCharacter, value: defeatedCards.Count, message: $"{targetCharacter.CharacterName} loses all remaining cards on defeat.");
			EmitRuleEvent(RuleEventType.CardsDiscarded, targetCharacter, targetCharacter, value: defeatedCards.Count, message: $"{targetCharacter.CharacterName}'s remaining cards are discarded.");
			foreach (CardInstance defeatedEquipment in defeatedCards.Where(card => CardRules.IsEquipment(card.CardType)))
			{
				NotifyEquipmentLost(targetCharacter, targetCharacter, defeatedEquipment);
			}
		}

		string sourceName = damageSource?.Name ?? "UnknownSource";
		ApplyIdentityDefeatReward(targetCharacter, damageSource as PlayerCharacter);
		targetCharacter.MarkDefeated();
		AddBattleLog($"{targetCharacter.CharacterName} is defeated by {sourceName}.");
		EmitRuleEvent(RuleEventType.CharacterDefeated, damageSource, targetCharacter, value: defeatedCards.Count, message: $"{targetCharacter.CharacterName} is defeated by {sourceName}.");
		RequestStateSync();
	}

	private void ApplyIdentityDefeatReward(PlayerCharacter defeatedCharacter, PlayerCharacter? killer)
	{
		if (!EnableFactionVictoryRules
			|| defeatedCharacter is null
			|| killer is null
			|| killer == defeatedCharacter
			|| killer.IsDefeated)
		{
			return;
		}

		if (defeatedCharacter.Faction == PlayerFaction.Rebel)
		{
			int drawnCount = DrawCardsForEffect(
				killer,
				3,
				$"{killer.CharacterName} draws {{count}} card(s) for defeating a Rebel.",
				$"{killer.CharacterName} draws cards for defeating a Rebel.");
			if (drawnCount > 0)
			{
				AddBattleLog($"{killer.CharacterName} gains the Rebel defeat reward.");
			}

			return;
		}

		if (killer.Faction != PlayerFaction.Lord || defeatedCharacter.Faction != PlayerFaction.Loyalist)
		{
			return;
		}

		IReadOnlyList<CardInstance> penaltyCards = killer.RemoveAllCardsAndEquipment();
		foreach (CardInstance penaltyCard in penaltyCards)
		{
			DiscardCardToPile(penaltyCard);
		}

		if (penaltyCards.Count == 0)
		{
			AddBattleLog($"{killer.CharacterName} has no cards to lose for defeating a Loyalist.");
			return;
		}

		AddBattleLog($"{killer.CharacterName} loses all cards for defeating a Loyalist.");
		EmitCardsLost(killer, killer, value: penaltyCards.Count, message: $"{killer.CharacterName} loses all cards for defeating a Loyalist.");
		EmitRuleEvent(RuleEventType.CardsDiscarded, killer, killer, value: penaltyCards.Count, message: $"{killer.CharacterName} loses all cards for defeating a Loyalist.");
		foreach (CardInstance equipmentCard in penaltyCards.Where(card => CardRules.IsEquipment(card.CardType)))
		{
			NotifyEquipmentLost(killer, killer, equipmentCard);
		}
	}

	private void ResetPendingDyingState()
	{
		_pendingDyingCharacter = null;
		_pendingDyingSource = null;
		_pendingDyingResponseOrder.Clear();
		_pendingDyingResponseIndex = 0;
	}

	private CoreGameView? BuildLocalCoreView()
	{
		if (LanMultiplayerManager.Instance is not LanMultiplayerManager network)
		{
			return CoreRuntime?.Engine is null ? null : CoreRuntime.BuildView(CoreViewerContext.Spectator);
		}
		if (network.JoinAsSpectator)
		{
			return CoreRuntime?.Engine is null
				? network.LastMatchStateSnapshot?.CoreView
				: CoreRuntime.BuildView(CoreViewerContext.Spectator);
		}
		int localSeatId = ResolveSeatId(network.LocalPeerId);
		return CoreRuntime?.Engine is null
			? network.LastMatchStateSnapshot?.CoreView
			: CoreRuntime.BuildView(CoreViewerContext.ForPlayer(Math.Max(1, localSeatId)));
	}

	private void ApplyCoreViewToPresentation(CoreGameView? view)
	{
		if (view is null)
		{
			return;
		}

		IsMatchRunning = view.Status == CiyuanSha.GameCore.Domain.MatchStatus.Running;
		ResultMessage = view.ResultMessage;
		WinnerPeerId = view.WinnerSeatIds.Count == 1 ? ResolvePeerId(view.WinnerSeatIds[0]) : 0;
		CurrentTurnPeerId = ResolvePeerId(view.CurrentSeatId);
		CurrentPhase = ToPresentationPhase(view.Phase);
		SetPublicPileCounts(view.DrawPileCount, view.DiscardPileCount);
		_turnOrder.Clear();
		_turnOrder.AddRange(view.Players.OrderBy(player => player.SeatId).Select(player => ResolvePeerId(player.SeatId)));
		_currentTurnIndex = Math.Max(0, _turnOrder.IndexOf(CurrentTurnPeerId));
		ResetLegacyWaitingState();

		int viewerPeerId = LanMultiplayerManager.Instance?.JoinAsSpectator == true
			? 0
			: LanMultiplayerManager.Instance?.LocalPeerId ?? -1;
		foreach (NetworkCharacterState state in BuildCoreCharacterStates(view, viewerPeerId))
		{
			if (_peerCharacters.TryGetValue(state.PeerId, out PlayerCharacter? character))
			{
				character.ApplyNetworkState(state);
			}
		}

		OnMatchRunningChanged?.Invoke(IsMatchRunning);
		OnTurnOwnerChanged?.Invoke(CurrentTurnPeerId);
		OnPhaseChanged?.Invoke(CurrentPhase);
		OnStateChanged?.Invoke();
	}

	private void ApplyCoreViewToSnapshot(MatchStateSnapshot snapshot, CoreGameView view, int viewerPeerId)
	{
		snapshot.CoreView = view;
		snapshot.MatchId = view.MatchId;
		snapshot.ModeId = view.ModeId;
		snapshot.StateRevision = view.Revision;
		snapshot.StateHash = view.StateHash;
		snapshot.ActiveChoice = view.PendingChoice;
		snapshot.ContentPacks = view.ContentPacks.ToList();
		snapshot.IsMatchRunning = view.Status == CiyuanSha.GameCore.Domain.MatchStatus.Running;
		snapshot.WinnerPeerId = view.WinnerSeatIds.Count == 1 ? ResolvePeerId(view.WinnerSeatIds[0]) : 0;
		snapshot.ResultMessage = view.ResultMessage;
		snapshot.CurrentTurnPeerId = ResolvePeerId(view.CurrentSeatId);
		snapshot.CurrentTurnIndex = Math.Max(0, view.Players.OrderBy(player => player.SeatId).ToList().FindIndex(player => player.SeatId == view.CurrentSeatId));
		snapshot.CurrentPhase = ToPresentationPhase(view.Phase).ToString();
		snapshot.WaitingPeerId = view.PendingChoice is null ? 0 : ResolvePeerId(view.PendingChoice.ActingSeatId);
		snapshot.WaitingKind = view.PendingChoice?.Kind.ToString() ?? string.Empty;
		snapshot.WaitingPrompt = view.PendingChoice?.PromptKey ?? string.Empty;
		snapshot.IsAwaitingPlayInput = false;
		snapshot.IsAwaitingDiscardInput = false;
		snapshot.HasPendingResponseWindow = false;
		snapshot.PendingResponsePeerId = 0;
		snapshot.PendingHarvestPeerId = 0;
		snapshot.PendingTargetCardSelectionPeerId = 0;
		snapshot.PendingHandCardSelectionPeerId = 0;
		snapshot.HarvestPool.Clear();
		snapshot.TargetCardSelectionPool.Clear();
		snapshot.HandCardSelectionPool.Clear();
		snapshot.SlashUsesThisTurn = 0;
		snapshot.WineUsesThisTurn = 0;
		snapshot.DrawPileCount = view.DrawPileCount;
		snapshot.DiscardPileCount = view.DiscardPileCount;
		snapshot.TurnOrder = view.Players.OrderBy(player => player.SeatId).Select(player => ResolvePeerId(player.SeatId)).ToList();
		snapshot.Characters = BuildCoreCharacterStates(view, viewerPeerId);
		snapshot.BattleLog = BuildCorePublicBattleLog();
		snapshot.PresentationEvents.Clear();
	}

	private List<NetworkCharacterState> BuildCoreCharacterStates(CoreGameView view, int viewerPeerId)
	{
		Dictionary<string, CiyuanSha.GameCore.Domain.CardView> visibleCards = view.PublicCards
			.Concat(view.PrivateHandCards)
			.GroupBy(card => card.InstanceId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		List<NetworkCharacterState> states = new();
		foreach (CiyuanSha.GameCore.Domain.PlayerView player in view.Players.OrderBy(player => player.SeatId))
		{
			GeneralDefinitionV2? general = CoreRuntime?.Content.Generals.GetValueOrDefault(player.GeneralId);
			Dictionary<CiyuanSha.GameCore.Domain.EquipmentSlot, string> equipmentNames = new();
			foreach ((CiyuanSha.GameCore.Domain.EquipmentSlot slot, string cardId) in player.Equipment)
			{
				equipmentNames[slot] = ResolveVisibleCardName(cardId, visibleCards);
			}
			List<NetworkHandCardState> hand = view.PrivateHandCards
				.Where(card => card.OwnerSeatId == player.SeatId)
				.OrderBy(card => card.InstanceId, StringComparer.Ordinal)
				.Select(ToNetworkCoreCard)
				.ToList();
			List<NetworkHandCardState> delayed = player.JudgementArea
				.Where(visibleCards.ContainsKey)
				.Select(cardId => ToNetworkCoreCard(visibleCards[cardId]))
				.ToList();
			states.Add(new NetworkCharacterState
			{
				PeerId = ResolvePeerId(player.SeatId),
				CharacterId = player.GeneralId,
				CharacterName = player.DisplayName,
				Faction = player.IsRoleVisible ? player.Role.ToString() : "Hidden",
				Gender = general?.Gender ?? "Unknown",
				MaxHealth = player.MaxHealth,
				CurrentHealth = player.Health,
				IsDefeated = !player.IsAlive,
				IsChained = player.IsChained,
				EffectiveAttackRange = ResolveAttackRange(player, visibleCards),
				HandCardCount = player.HandCount,
				GeneralCardFileName = general?.Artwork ?? string.Empty,
				EquippedWeaponName = equipmentNames.GetValueOrDefault(CiyuanSha.GameCore.Domain.EquipmentSlot.Weapon, string.Empty),
				EquippedArmorName = equipmentNames.GetValueOrDefault(CiyuanSha.GameCore.Domain.EquipmentSlot.Armor, string.Empty),
				EquippedOffensiveHorseName = equipmentNames.GetValueOrDefault(CiyuanSha.GameCore.Domain.EquipmentSlot.OffensiveMount, string.Empty),
				EquippedDefensiveHorseName = equipmentNames.GetValueOrDefault(CiyuanSha.GameCore.Domain.EquipmentSlot.DefensiveMount, string.Empty),
				EquippedTreasureName = equipmentNames.GetValueOrDefault(CiyuanSha.GameCore.Domain.EquipmentSlot.Treasure, string.Empty),
				DelayedTricks = delayed,
				Skills = player.SkillIds.Select(ToNetworkCoreSkill).ToList(),
				HandCards = hand
			});
		}
		return states;
	}

	private NetworkHandCardState ToNetworkCoreCard(CiyuanSha.GameCore.Domain.CardView card)
	{
		CardDefinition definition = CoreRuntime?.Content.Cards[card.DefinitionId]
			?? throw new InvalidOperationException($"Unknown visible card definition: {card.DefinitionId}");
		return new NetworkHandCardState
		{
			InstanceId = card.InstanceId,
			CardType = ToPresentationCardType(definition),
			DisplayName = definition.DisplayName,
			Description = definition.Description,
			Suit = card.Suit.ToString(),
			Rank = card.Rank,
			EquipmentSlot = ToPresentationEquipmentSlot(definition.EquipmentSlot),
			EquipmentEffect = ToPresentationEquipmentEffect(definition.CardId),
			AttackRangeModifier = definition.AttackRange,
			IsDelayedTrick = definition.Category == CiyuanSha.GameCore.Domain.CardCategory.DelayedTrick
		};
	}

	private NetworkSkillState ToNetworkCoreSkill(string skillId)
	{
		CiyuanSha.GameCore.Skills.SkillDefinitionV2 skill = CoreRuntime?.Content.Skills[skillId]
			?? throw new InvalidOperationException($"Unknown skill definition: {skillId}");
		return new NetworkSkillState
		{
			SkillId = skill.SkillId,
			DisplayName = skill.DisplayName,
			Description = skill.Description,
			UsageScope = skill.UsageScope.ToString(),
			TriggerPriority = skill.Triggers.OrderByDescending(trigger => trigger.Priority).FirstOrDefault()?.Priority.ToString() ?? "0",
			TriggerTimings = string.Join(",", skill.Triggers.Select(trigger => $"{trigger.EventKind}.{trigger.EventStage}")),
			IsActiveSkill = (skill.Tags & CiyuanSha.GameCore.Skills.SkillTags.Active) != 0,
			TargetingMode = "ChoiceRequest",
			MinTargetCount = 0,
			MaxTargetCount = 0,
			RequiresHandCardCost = skill.EffectId.Contains("cost", StringComparison.OrdinalIgnoreCase)
		};
	}

	private List<NetworkBattleLogEntry> BuildCorePublicBattleLog()
	{
		if (CoreRuntime?.Engine is null)
		{
			return new List<NetworkBattleLogEntry>();
		}
		return CoreRuntime.Engine.Journal.Entries
			.Where(entry => entry.Kind == CiyuanSha.GameCore.Replay.JournalEntryKind.RuleEvent
				&& entry.RuleEvent is { Stage: CiyuanSha.GameCore.Events.RuleEventStage.Completed or CiyuanSha.GameCore.Events.RuleEventStage.Cancelled })
			.Select(entry => new NetworkBattleLogEntry
			{
				Sequence = entry.Sequence > int.MaxValue ? int.MaxValue : (int)entry.Sequence,
				Message = DescribePublicRuleEvent(entry.RuleEvent!)
			})
			.TakeLast(256)
			.ToList();
	}

	private string ResolveVisibleCardName(string cardId, IReadOnlyDictionary<string, CiyuanSha.GameCore.Domain.CardView> visibleCards)
	{
		return visibleCards.TryGetValue(cardId, out CiyuanSha.GameCore.Domain.CardView? card)
			&& CoreRuntime?.Content.Cards.TryGetValue(card.DefinitionId, out CardDefinition? definition) == true
				? definition.DisplayName
				: string.Empty;
	}

	private int ResolveAttackRange(CiyuanSha.GameCore.Domain.PlayerView player, IReadOnlyDictionary<string, CiyuanSha.GameCore.Domain.CardView> visibleCards)
	{
		if (!player.Equipment.TryGetValue(CiyuanSha.GameCore.Domain.EquipmentSlot.Weapon, out string? weaponId)
			|| !visibleCards.TryGetValue(weaponId, out CiyuanSha.GameCore.Domain.CardView? weapon)
			|| CoreRuntime?.Content.Cards.TryGetValue(weapon.DefinitionId, out CardDefinition? definition) != true)
		{
			return 1;
		}
		return Math.Max(1, definition!.AttackRange);
	}

	private int ResolveSeatId(int peerId)
	{
		if (peerId == 0)
		{
			return 0;
		}
		return LanMultiplayerManager.Instance?.Players.TryGetValue(peerId, out LanPlayerInfo? player) == true
			? player.SeatId
			: peerId;
	}

	private int ResolvePeerId(int seatId)
	{
		LanPlayerInfo? player = LanMultiplayerManager.Instance?.Players.Values.FirstOrDefault(item => item.SeatId == seatId && !item.IsSpectator);
		return player?.PeerId ?? Math.Max(1, seatId);
	}

	private void ResetLegacyWaitingState()
	{
		_isWaitingForPlayInput = false;
		_isWaitingForDiscardInput = false;
		_hasQueuedPlayPhaseAction = false;
		HasPendingResponseWindow = false;
		PendingResponsePeerId = 0;
		PendingResponsePrompt = string.Empty;
		PendingResponseKind = ResponseWindowKind.None;
		PendingHarvestPeerId = 0;
		PendingHarvestPrompt = string.Empty;
		PendingTargetCardSelectionPeerId = 0;
		PendingTargetCardSelectionPrompt = string.Empty;
		PendingHandCardSelectionPeerId = 0;
		PendingHandCardSelectionPrompt = string.Empty;
		CanDeclineHandCardSelection = false;
		_harvestPool.Clear();
		_targetCardSelectionPool.Clear();
		_handCardSelectionPool.Clear();
	}

	private static TurnPhase ToPresentationPhase(CiyuanSha.GameCore.Domain.GamePhase phase) => phase switch
	{
		CiyuanSha.GameCore.Domain.GamePhase.Preparation => TurnPhase.TurnStart,
		CiyuanSha.GameCore.Domain.GamePhase.Judgement => TurnPhase.JudgementPhase,
		CiyuanSha.GameCore.Domain.GamePhase.Draw => TurnPhase.DrawPhase,
		CiyuanSha.GameCore.Domain.GamePhase.Play => TurnPhase.PlayPhase,
		CiyuanSha.GameCore.Domain.GamePhase.Discard => TurnPhase.DiscardPhase,
		CiyuanSha.GameCore.Domain.GamePhase.End => TurnPhase.EndPhase,
		_ => TurnPhase.TurnStart
	};

	private static string ToPresentationCardType(CardDefinition definition) => definition.EffectId switch
	{
		"card.slash" => CardType.Slash.ToString(),
		"card.fire_slash" => CardType.FireSlash.ToString(),
		"card.thunder_slash" => CardType.ThunderSlash.ToString(),
		"card.dodge" => CardType.Dodge.ToString(),
		"card.peach" => CardType.Peach.ToString(),
		"card.wine" => CardType.Wine.ToString(),
		"card.dismantle" => CardType.Dismantle.ToString(),
		"card.snatch" => CardType.Snatch.ToString(),
		"card.duel" => CardType.Duel.ToString(),
		"card.ex_nihilo" => CardType.ExNihilo.ToString(),
		"card.nullification" => CardType.Nullification.ToString(),
		"card.barbarians" => CardType.Barbarians.ToString(),
		"card.arrow_barrage" => CardType.ArrowBarrage.ToString(),
		"card.peach_garden" => CardType.PeachGarden.ToString(),
		"card.harvest" => CardType.Harvest.ToString(),
		"card.indulgence" => CardType.Indulgence.ToString(),
		"card.supply_shortage" => CardType.SupplyShortage.ToString(),
		"card.lightning" => CardType.Lightning.ToString(),
		"card.iron_chain" => CardType.IronChain.ToString(),
		"card.fire_attack" => CardType.FireAttack.ToString(),
		"card.borrow_sword" => CardType.BorrowSword.ToString(),
		"card.equipment" when definition.EquipmentSlot == CiyuanSha.GameCore.Domain.EquipmentSlot.Weapon => CardType.Weapon.ToString(),
		"card.equipment" when definition.EquipmentSlot == CiyuanSha.GameCore.Domain.EquipmentSlot.Armor => CardType.Armor.ToString(),
		"card.equipment" when definition.EquipmentSlot == CiyuanSha.GameCore.Domain.EquipmentSlot.OffensiveMount => CardType.OffensiveHorse.ToString(),
		"card.equipment" when definition.EquipmentSlot == CiyuanSha.GameCore.Domain.EquipmentSlot.DefensiveMount => CardType.DefensiveHorse.ToString(),
		"card.equipment" when definition.EquipmentSlot == CiyuanSha.GameCore.Domain.EquipmentSlot.Treasure => CardType.Treasure.ToString(),
		_ => CardType.None.ToString()
	};

	private static string ToPresentationEquipmentSlot(CiyuanSha.GameCore.Domain.EquipmentSlot? slot) => slot switch
	{
		CiyuanSha.GameCore.Domain.EquipmentSlot.Weapon => EquipmentSlotType.Weapon.ToString(),
		CiyuanSha.GameCore.Domain.EquipmentSlot.Armor => EquipmentSlotType.Armor.ToString(),
		CiyuanSha.GameCore.Domain.EquipmentSlot.OffensiveMount => EquipmentSlotType.OffensiveHorse.ToString(),
		CiyuanSha.GameCore.Domain.EquipmentSlot.DefensiveMount => EquipmentSlotType.DefensiveHorse.ToString(),
		CiyuanSha.GameCore.Domain.EquipmentSlot.Treasure => EquipmentSlotType.Treasure.ToString(),
		_ => EquipmentSlotType.None.ToString()
	};

	private static string ToPresentationEquipmentEffect(string cardId) => cardId switch
	{
		"crossbow" => EquipmentEffectType.Crossbow.ToString(),
		"qinggang_sword" => EquipmentEffectType.QinggangSword.ToString(),
		"fangtian_halberd" => EquipmentEffectType.FangtianHalberd.ToString(),
		"stone_axe" => EquipmentEffectType.StoneAxe.ToString(),
		"kylin_bow" => EquipmentEffectType.KylinBow.ToString(),
		"green_dragon_blade" => EquipmentEffectType.GreenDragonBlade.ToString(),
		"double_swords" => EquipmentEffectType.DoubleSwords.ToString(),
		"serpent_spear" => EquipmentEffectType.SerpentSpear.ToString(),
		"ice_sword" => EquipmentEffectType.IceSword.ToString(),
		"guding_blade" => EquipmentEffectType.GudingBlade.ToString(),
		"vermilion_fan" => EquipmentEffectType.VermilionFan.ToString(),
		"eight_diagram" => EquipmentEffectType.EightDiagram.ToString(),
		"vine_armor" => EquipmentEffectType.VineArmor.ToString(),
		"renwang_shield" => EquipmentEffectType.RenwangShield.ToString(),
		"silver_lion" => EquipmentEffectType.SilverLion.ToString(),
		"imperial_seal" => EquipmentEffectType.ImperialSeal.ToString(),
		_ => EquipmentEffectType.None.ToString()
	};

	private static string DescribePublicRuleEvent(CiyuanSha.GameCore.Events.RuleEvent ruleEvent)
	{
		if (ruleEvent.Payload is CiyuanSha.GameCore.Events.TextRuleEventPayload text)
		{
			return text.MessageKey;
		}
		return ruleEvent.Kind switch
		{
			CiyuanSha.GameCore.Events.RuleEventKind.PhaseChanged when ruleEvent.Payload is CiyuanSha.GameCore.Events.PhaseRuleEventPayload phase => $"Phase: {phase.Phase}",
			CiyuanSha.GameCore.Events.RuleEventKind.Damage when ruleEvent.Payload is CiyuanSha.GameCore.Events.DamageRuleEventPayload damage => $"Seat {ruleEvent.SourceSeatId} deals {damage.Amount} {damage.Nature} damage.",
			_ => ruleEvent.Kind.ToString()
		};
	}

	public MatchStateSnapshot BuildMatchStateSnapshot(int viewerPeerId = -1)
	{
		GameCoreRuntime? core = CoreRuntime;
		CoreViewerRole viewerRole = viewerPeerId == 0 ? CoreViewerRole.Spectator : CoreViewerRole.Player;
		int viewerSeatId = ResolveSeatId(viewerPeerId);
		CoreGameView? coreView = core?.Engine is null
			? null
			: core.BuildView(viewerRole == CoreViewerRole.Spectator
				? CoreViewerContext.Spectator
				: CoreViewerContext.ForPlayer(Math.Max(1, viewerSeatId)));
		MatchStateSnapshot snapshot = new()
		{
			ProtocolVersion = CiyuanSha.GameCore.Networking.ProtocolV2.Version,
			EngineApiVersion = CiyuanSha.GameCore.Networking.ProtocolV2.EngineApiVersion,
			MatchId = coreView?.MatchId ?? string.Empty,
			ModeId = coreView?.ModeId ?? LanMultiplayerManager.Instance?.SelectedModeId ?? BuiltInModeIds.Duel,
			StateRevision = coreView?.Revision ?? 0,
			JournalCursor = core?.Engine?.Journal.Cursor ?? 0,
			StateHash = coreView?.StateHash ?? string.Empty,
			ViewerRole = viewerRole,
			ActiveChoice = coreView?.PendingChoice,
			CoreView = coreView,
			ContentPacks = core?.ContentPacks.ToList() ?? new List<CiyuanSha.GameCore.Content.ContentPackReference>(),
			IsMatchRunning = IsMatchRunning,
			WinnerPeerId = WinnerPeerId,
			ResultMessage = ResultMessage,
			EnableFactionVictoryRules = EnableFactionVictoryRules,
			CurrentTurnPeerId = CurrentTurnPeerId,
			CurrentTurnIndex = _currentTurnIndex,
			CurrentPhase = CurrentPhase.ToString(),
			WaitingPeerId = GetWaitingPeerId(),
			WaitingKind = GetWaitingKind(),
			WaitingPrompt = GetWaitingPrompt(),
			LobbyPlayers = LanMultiplayerManager.Instance?.Players.Values.OrderBy(player => player.PeerId).Select(CloneLobbyPlayerInfo).ToList() ?? new List<LanPlayerInfo>(),
			IsAwaitingPlayInput = _isWaitingForPlayInput,
			IsAwaitingDiscardInput = _isWaitingForDiscardInput,
			HasPendingResponseWindow = HasPendingResponseWindow,
			PendingResponsePeerId = PendingResponsePeerId,
			PendingResponsePrompt = PendingResponsePrompt,
			PendingResponseKind = PendingResponseKind.ToString(),
			PendingHarvestPeerId = PendingHarvestPeerId,
			PendingHarvestPrompt = PendingHarvestPrompt,
			HarvestPool = _harvestPool.Select(ToNetworkHandCardState).ToList(),
			PendingTargetCardSelectionPeerId = PendingTargetCardSelectionPeerId,
			PendingTargetCardSelectionPrompt = PendingTargetCardSelectionPrompt,
			TargetCardSelectionPool = _targetCardSelectionPool.Select(ToNetworkHandCardState).ToList(),
			PendingHandCardSelectionPeerId = PendingHandCardSelectionPeerId,
			PendingHandCardSelectionPrompt = PendingHandCardSelectionPrompt,
			HandCardSelectionPool = BuildHandCardSelectionSnapshot(viewerPeerId),
			CanDeclineHandCardSelection = CanDeclineHandCardSelection,
			SlashUsesThisTurn = SlashUsesThisTurn,
			WineUsesThisTurn = WineUsesThisTurn,
			DrawPileCount = DrawPileCount,
			DiscardPileCount = DiscardPileCount,
			TurnOrder = new List<int>(_turnOrder),
			Characters = _peerCharacters
				.OrderBy(entry => entry.Key)
				.Select(entry =>
				{
					NetworkCharacterState state = entry.Value.ToNetworkState(viewerPeerId < 0 || entry.Key == viewerPeerId);
					LanPlayerInfo? lobbyPlayer = LanMultiplayerManager.Instance?.Players.GetValueOrDefault(entry.Key);
					int seatId = lobbyPlayer?.SeatId ?? entry.Key;
					CiyuanSha.GameCore.Domain.PlayerView? playerView = coreView?.Players.FirstOrDefault(player => player.SeatId == seatId);
					if (viewerPeerId >= 0 && playerView is { IsRoleVisible: false })
					{
						state.Faction = "Hidden";
					}
					return state;
				})
				.ToList(),
			BattleLog = _battleLog
				.Select(entry => new NetworkBattleLogEntry
				{
					Sequence = entry.Sequence,
					Message = entry.Message
				})
				.ToList(),
			PresentationEvents = _presentationEvents
				.Select(ClonePresentationEvent)
				.ToList()
		};
		if (coreView is not null)
		{
			ApplyCoreViewToSnapshot(snapshot, coreView, viewerPeerId);
		}
		return snapshot;
	}

	public void ApplyMatchStateSnapshot(MatchStateSnapshot snapshot)
	{
		if (snapshot is null)
		{
			return;
		}

		bool wasMatchRunning = IsMatchRunning;
		IsCoreMatchActive = snapshot.CoreView is not null;
		IsMatchRunning = snapshot.IsMatchRunning;
		WinnerPeerId = Math.Max(0, snapshot.WinnerPeerId);
		ResultMessage = snapshot.ResultMessage ?? string.Empty;
		EnableFactionVictoryRules = snapshot.EnableFactionVictoryRules;
		_turnOrder.Clear();
		_turnOrder.AddRange(snapshot.TurnOrder.Where(peerId => peerId > 0));
		_currentTurnIndex = Math.Max(0, snapshot.CurrentTurnIndex);
		CurrentTurnPeerId = Math.Max(1, snapshot.CurrentTurnPeerId);
		_isWaitingForPlayInput = snapshot.IsAwaitingPlayInput;
		_isWaitingForDiscardInput = snapshot.IsAwaitingDiscardInput;
		HasPendingResponseWindow = snapshot.HasPendingResponseWindow;
		PendingResponsePeerId = snapshot.PendingResponsePeerId;
		PendingResponsePrompt = snapshot.PendingResponsePrompt ?? string.Empty;
		PendingHarvestPeerId = Math.Max(0, snapshot.PendingHarvestPeerId);
		PendingHarvestPrompt = snapshot.PendingHarvestPrompt ?? string.Empty;
		PendingTargetCardSelectionPeerId = Math.Max(0, snapshot.PendingTargetCardSelectionPeerId);
		PendingTargetCardSelectionPrompt = snapshot.PendingTargetCardSelectionPrompt ?? string.Empty;
		PendingHandCardSelectionPeerId = Math.Max(0, snapshot.PendingHandCardSelectionPeerId);
		PendingHandCardSelectionPrompt = snapshot.PendingHandCardSelectionPrompt ?? string.Empty;
		CanDeclineHandCardSelection = snapshot.CanDeclineHandCardSelection;
		PendingResponseKind = Enum.TryParse(snapshot.PendingResponseKind, true, out ResponseWindowKind pendingResponseKind)
			? pendingResponseKind
			: ResponseWindowKind.None;
		SlashUsesThisTurn = Math.Max(0, snapshot.SlashUsesThisTurn);
		WineUsesThisTurn = Math.Max(0, snapshot.WineUsesThisTurn);
		_hasQueuedPlayPhaseAction = false;

		SetPublicPileCounts(snapshot.DrawPileCount, snapshot.DiscardPileCount);
		_harvestPool.Clear();
		_harvestPool.AddRange((snapshot.HarvestPool ?? new List<NetworkHandCardState>()).Select(FromNetworkHandCardState));
		_pendingHarvestPeerOrder.Clear();
		_targetCardSelectionPool.Clear();
		_targetCardSelectionPool.AddRange((snapshot.TargetCardSelectionPool ?? new List<NetworkHandCardState>()).Select(FromNetworkHandCardState));
		_handCardSelectionPool.Clear();
		_handCardSelectionPool.AddRange((snapshot.HandCardSelectionPool ?? new List<NetworkHandCardState>()).Select(FromNetworkHandCardState));

		_battleLog.Clear();
		_battleLog.AddRange(snapshot.BattleLog.OrderBy(entry => entry.Sequence));
		_nextBattleLogSequence = (_battleLog.LastOrDefault()?.Sequence ?? 0) + 1;
		ApplyPresentationEventSnapshot(snapshot.PresentationEvents, wasMatchRunning);

		if (Enum.TryParse(snapshot.CurrentPhase, true, out TurnPhase phase))
		{
			CurrentPhase = phase;
		}

		foreach (NetworkCharacterState characterState in snapshot.Characters)
		{
			if (_peerCharacters.TryGetValue(characterState.PeerId, out PlayerCharacter? character))
			{
				character.ApplyNetworkState(characterState);
			}
		}

		OnMatchRunningChanged?.Invoke(IsMatchRunning);
		OnTurnOwnerChanged?.Invoke(CurrentTurnPeerId);
		OnPhaseChanged?.Invoke(CurrentPhase);
		OnBattleLogChanged?.Invoke();
		OnStateChanged?.Invoke();
		OnResponseWindowChanged?.Invoke();

		if (!IsMatchRunning && (WinnerPeerId > 0 || !string.IsNullOrWhiteSpace(ResultMessage)))
		{
			OnMatchEnded?.Invoke(WinnerPeerId, ResultMessage);
		}

		if (CurrentPhase == TurnPhase.PlayPhase && _isWaitingForPlayInput)
		{
			OnPlayPhaseInputRequested?.Invoke();
		}

		if (CurrentPhase == TurnPhase.DiscardPhase && _isWaitingForDiscardInput)
		{
			OnDiscardPhaseInputRequested?.Invoke();
		}

		if (HasPendingHarvestSelection)
		{
			OnStateChanged?.Invoke();
		}
	}

	private int GetWaitingPeerId()
	{
		if (HasPendingResponseWindow)
		{
			return PendingResponsePeerId;
		}

		if (HasPendingHarvestSelection)
		{
			return PendingHarvestPeerId;
		}

		if (HasPendingTargetCardSelection)
		{
			return PendingTargetCardSelectionPeerId;
		}

		if (HasPendingHandCardSelection)
		{
			return PendingHandCardSelectionPeerId;
		}

		if (IsAwaitingPlayerInput || IsAwaitingDiscardInput)
		{
			return CurrentTurnPeerId;
		}

		return 0;
	}

	private string GetWaitingKind()
	{
		if (HasPendingResponseWindow)
		{
			return PendingResponseKind.ToString();
		}

		if (HasPendingHarvestSelection)
		{
			return "Harvest";
		}

		if (HasPendingTargetCardSelection)
		{
			return "TargetCardSelection";
		}

		if (HasPendingHandCardSelection)
		{
			return CanDeclineHandCardSelection ? "HandCardSelectionOptional" : "HandCardSelection";
		}

		if (IsAwaitingDiscardInput)
		{
			return "DiscardPhase";
		}

		if (IsAwaitingPlayerInput)
		{
			return "PlayPhase";
		}

		return string.Empty;
	}

	private string GetWaitingPrompt()
	{
		if (HasPendingResponseWindow)
		{
			return PendingResponsePrompt;
		}

		if (HasPendingHarvestSelection)
		{
			return PendingHarvestPrompt;
		}

		if (HasPendingTargetCardSelection)
		{
			return PendingTargetCardSelectionPrompt;
		}

		if (HasPendingHandCardSelection)
		{
			return PendingHandCardSelectionPrompt;
		}

		if (IsAwaitingDiscardInput)
		{
			return "Discard down to hand limit.";
		}

		if (IsAwaitingPlayerInput)
		{
			return "Play a card, activate a skill, or end PlayPhase.";
		}

		return string.Empty;
	}

	private static LanPlayerInfo CloneLobbyPlayerInfo(LanPlayerInfo player)
	{
		return new LanPlayerInfo
		{
			PlayerId = player.PlayerId,
			PeerId = player.PeerId,
			SeatId = player.SeatId,
			TransportPeerId = player.TransportPeerId,
			PlayerName = player.PlayerName,
			CharacterId = player.CharacterId,
			IsReady = player.IsReady,
			IsHost = player.IsHost,
			IsConnected = player.IsConnected,
			IsBot = player.IsBot,
			IsSpectator = player.IsSpectator,
			IsBoss = player.IsBoss,
			BotDifficulty = player.BotDifficulty
		};
	}

	public void EndPlayPhase()
	{
		if (CurrentPhase != TurnPhase.PlayPhase)
		{
			return;
		}

		if (_actionManager is not null && _actionManager.HasPendingActions)
		{
			return;
		}

		AdvanceToNextPhase();
		RequestStateSync();
	}

	public int DrawCardsForEffect(PlayerCharacter? character, int count, string logMessage = "", string eventMessage = "")
	{
		if (character is null || count <= 0)
		{
			return 0;
		}

		int drawnCount = DrawCardsToCharacter(character, count);
		if (drawnCount <= 0)
		{
			return 0;
		}

		if (!string.IsNullOrWhiteSpace(logMessage))
		{
			AddBattleLog(logMessage.Replace("{count}", drawnCount.ToString(), StringComparison.Ordinal));
		}

		string resolvedEventMessage = (string.IsNullOrWhiteSpace(eventMessage) ? logMessage : eventMessage)
			.Replace("{count}", drawnCount.ToString(), StringComparison.Ordinal);
		EmitRuleEvent(RuleEventType.CardsDrawn, character, character, value: drawnCount, message: resolvedEventMessage);
		EmitCardsGained(character, character, value: drawnCount, message: resolvedEventMessage);
		return drawnCount;
	}

	private void HandlePlayPhase()
	{
		if (_isWaitingForPlayInput)
		{
			return;
		}

		if (HasPendingResponseWindow || HasPendingHarvestSelection || HasPendingTargetCardSelection || HasPendingHandCardSelection)
		{
			return;
		}

		if (_actionManager is not null && _actionManager.HasPendingActions)
		{
			return;
		}

		if (_hasQueuedPlayPhaseAction)
		{
			_hasQueuedPlayPhaseAction = false;
			_isWaitingForPlayInput = true;
			OnPlayPhaseInputRequested?.Invoke();
			RequestStateSync();
		}
	}

	private void AdvanceToNextPhase()
	{
		if (CurrentPhase == TurnPhase.EndPhase)
		{
			EmitRuleEvent(RuleEventType.TurnEnded, actingPeerId: CurrentTurnPeerId, phase: CurrentPhase, message: $"Turn ended: peer {CurrentTurnPeerId}.");
			AdvanceTurnOwner();
			EnterPhase(TurnPhase.TurnStart);
			return;
		}

		TurnPhase nextPhase = CurrentPhase switch
		{
			TurnPhase.TurnStart => TurnPhase.JudgementPhase,
			TurnPhase.JudgementPhase => TurnPhase.DrawPhase,
			TurnPhase.DrawPhase => TurnPhase.PlayPhase,
			TurnPhase.PlayPhase => TurnPhase.DiscardPhase,
			TurnPhase.DiscardPhase => TurnPhase.EndPhase,
			_ => TurnPhase.TurnStart
		};

		EnterPhase(nextPhase);
	}

	private void EnterPhase(TurnPhase phase)
	{
		CurrentPhase = phase;
		bool shouldSkipPlayPhase = false;
		if (phase != TurnPhase.PlayPhase)
		{
			ClearResponseWindow();
		}

		switch (phase)
		{
			case TurnPhase.TurnStart:
				_isWaitingForPlayInput = false;
				_isWaitingForDiscardInput = false;
				_hasQueuedPlayPhaseAction = false;
				SlashUsesThisTurn = 0;
				WineUsesThisTurn = 0;
				AddBattleLog($"Turn start: peer {CurrentTurnPeerId}.");
				EmitRuleEvent(RuleEventType.TurnStarted, actingPeerId: CurrentTurnPeerId, phase: phase, message: $"Turn start: peer {CurrentTurnPeerId}.");
				TriggerTurnStartSkills();
				break;

			case TurnPhase.JudgementPhase:
				_isWaitingForPlayInput = false;
				_isWaitingForDiscardInput = false;
				_hasQueuedPlayPhaseAction = false;
				if (_peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? judgementCharacter))
				{
					ExecuteDelayedTricks(judgementCharacter);
				}
				break;

			case TurnPhase.DrawPhase:
				_isWaitingForPlayInput = false;
				_isWaitingForDiscardInput = false;
				_hasQueuedPlayPhaseAction = false;
				ExecuteDrawPhase();
				break;

			case TurnPhase.PlayPhase:
				_isWaitingForDiscardInput = false;
				shouldSkipPlayPhase = _skipPlayPhasePeerIds.Remove(CurrentTurnPeerId);
				_isWaitingForPlayInput = !shouldSkipPlayPhase;
				_hasQueuedPlayPhaseAction = false;
				if (shouldSkipPlayPhase && _peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? skippedCharacter))
				{
					AddBattleLog($"{skippedCharacter.CharacterName} skips PlayPhase.");
					EmitRuleEvent(RuleEventType.PhaseSkipped, skippedCharacter, skippedCharacter, phase: TurnPhase.PlayPhase, message: $"{skippedCharacter.CharacterName} skips PlayPhase.");
				}
				break;

			case TurnPhase.DiscardPhase:
				_isWaitingForPlayInput = false;
				_isWaitingForDiscardInput = false;
				_hasQueuedPlayPhaseAction = false;
				ExecuteDiscardPhase();
				break;

			case TurnPhase.EndPhase:
				_isWaitingForPlayInput = false;
				_isWaitingForDiscardInput = false;
				_hasQueuedPlayPhaseAction = false;
				if (_peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? endingCharacter))
				{
					endingCharacter.ClearPendingSlashDamageBonus();
				}
				break;
		}

		OnPhaseChanged?.Invoke(CurrentPhase);
		EmitRuleEvent(RuleEventType.PhaseEntered, actingPeerId: CurrentTurnPeerId, phase: phase, message: $"Entered phase {phase}.");
		RequestStateSync();

		if (shouldSkipPlayPhase)
		{
			AdvanceToNextPhase();
			return;
		}

		if (phase == TurnPhase.PlayPhase)
		{
			OnPlayPhaseInputRequested?.Invoke();
		}

		if (phase == TurnPhase.DiscardPhase && _isWaitingForDiscardInput)
		{
			OnDiscardPhaseInputRequested?.Invoke();
		}
	}

	private ActionManager? ResolveActionManager()
	{
		if (!ActionManagerPath.IsEmpty)
		{
			ActionManager? fromPath = GetNodeOrNull<ActionManager>(ActionManagerPath);
			if (fromPath is not null)
			{
				return fromPath;
			}
		}

		ActionManager? childManager = GetNodeOrNull<ActionManager>("ActionManager");
		if (childManager is not null)
		{
			return childManager;
		}

		ActionManager newManager = new()
		{
			Name = "ActionManager"
		};

		AddChild(newManager);
		return newManager;
	}

	private bool TrySubmitBasicAttackCommand(int actingPeerId, NetworkPlayCommand command)
	{
		return TrySubmitSlashCardCommand(actingPeerId, command);
	}

	private bool TrySubmitSlashCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_actionManager is null)
		{
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		CardInstance? playedCard = sourceCharacter.FindHandCard(command.CardInstanceId);
		if (playedCard is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to play an unknown card instance: {command.CardInstanceId}.");
			return false;
		}

		if (!CardRules.IsSlash(playedCard.CardType))
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use unsupported card type {playedCard.CardType} as Slash.");
			return false;
		}

		List<PlayerCharacter> targets = ResolveTargetsForCard(sourceCharacter, playedCard, command);
		if (targets.Count == 0)
		{
			GD.PushWarning($"{playedCard.DisplayName} requires at least one valid target.");
			return false;
		}

		int maxTargets = GetMaxTargetCount(sourceCharacter, playedCard);
		if (targets.Count > maxTargets)
		{
			GD.PushWarning($"{playedCard.DisplayName} can target at most {maxTargets} character(s).");
			return false;
		}

		DamageType damageType = playedCard.CardType switch
		{
			CardType.FireSlash => DamageType.Fire,
			CardType.ThunderSlash => DamageType.Thunder,
			CardType.Slash when sourceCharacter.CanUseVermilionFan
				&& string.Equals(command.DamageType, DamageType.Fire.ToString(), StringComparison.OrdinalIgnoreCase) => DamageType.Fire,
			_ => Enum.TryParse(command.DamageType, true, out DamageType parsedDamageType)
				? parsedDamageType
				: DamageType.Physical
		};
		if (playedCard.CardType == CardType.Slash && damageType == DamageType.Fire)
		{
			AddBattleLog($"{sourceCharacter.CharacterName} converts Slash to Fire Slash with Vermilion Fan.");
		}

		List<PlayerCharacter> unblockedTargets = new();
		foreach (PlayerCharacter targetCharacter in targets)
		{
			if (ShouldRenwangShieldBlockSlash(sourceCharacter, targetCharacter, playedCard))
			{
				AddBattleLog($"{targetCharacter.CharacterName}'s Renwang Shield blocks {sourceCharacter.CharacterName}'s Slash.");
				continue;
			}

			unblockedTargets.Add(targetCharacter);
		}

		int originalDamageValue = playedCard.DamageValue;
		playedCard.DamageValue = Math.Max(1, playedCard.DamageValue + sourceCharacter.PendingSlashDamageBonus);

		if (unblockedTargets.Count > 0)
		{
			SlashUseAction slashUseAction = new(_actionManager, sourceCharacter, unblockedTargets, playedCard, damageType);
			if (!SubmitPlayPhaseAction(slashUseAction))
			{
				playedCard.DamageValue = originalDamageValue;
				return false;
			}
		}
		else
		{
			_isWaitingForPlayInput = true;
			_hasQueuedPlayPhaseAction = false;
			OnPlayPhaseInputRequested?.Invoke();
		}

		sourceCharacter.ConsumePendingSlashDamageBonus();
		SlashUsesThisTurn++;
		foreach (PlayerCharacter target in targets)
		{
			EmitCardUsed(sourceCharacter, playedCard, target);
		}

		sourceCharacter.TryConsumeHandCardByInstanceId(command.CardInstanceId, out _);
		RequestStateSync();
		return true;
	}

	private static bool ShouldRenwangShieldBlockSlash(PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, CardInstance playedCard)
	{
		return targetCharacter.HasRenwangShield
			&& playedCard.CardType == CardType.Slash
			&& CardRules.IsBlackSuit(playedCard.Suit)
			&& !sourceCharacter.IgnoresTargetArmor;
	}

	private void ExecuteDrawPhase()
	{
		if (!_peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? currentCharacter))
		{
			return;
		}

		if (_skipDrawPhasePeerIds.Remove(CurrentTurnPeerId))
		{
			AddBattleLog($"{currentCharacter.CharacterName} skips DrawPhase.");
			EmitRuleEvent(RuleEventType.PhaseSkipped, currentCharacter, currentCharacter, phase: TurnPhase.DrawPhase, message: $"{currentCharacter.CharacterName} skips DrawPhase.");
			return;
		}

		int drawCount = Math.Max(0, DrawCardsPerTurn);
		if (currentCharacter.EquippedTreasure?.EquipmentEffect == EquipmentEffectType.ImperialSeal)
		{
			drawCount += 1;
			AddBattleLog($"{currentCharacter.CharacterName}'s Imperial Seal grants +1 draw.");
		}

		int drawnCount = DrawCardsToCharacter(currentCharacter, drawCount);
		AddBattleLog($"{currentCharacter.CharacterName} draws {drawnCount} card(s).");
		EmitRuleEvent(RuleEventType.CardsDrawn, currentCharacter, currentCharacter, value: drawnCount, message: $"{currentCharacter.CharacterName} draws {drawnCount} card(s).");
		EmitCardsGained(currentCharacter, currentCharacter, value: drawnCount, message: $"{currentCharacter.CharacterName} draws {drawnCount} card(s).");
	}

	private void ExecuteDiscardPhase()
	{
		if (!_peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? currentCharacter))
		{
			return;
		}

		int handLimit = Math.Max(0, currentCharacter.CurrentHealth);
		if (currentCharacter.HandCardCount > handLimit)
		{
			_isWaitingForDiscardInput = true;
			AddBattleLog($"{currentCharacter.CharacterName} must discard {currentCharacter.HandCardCount - handLimit} card(s) down to hand limit {handLimit}.");
			return;
		}

		AddBattleLog($"{currentCharacter.CharacterName} keeps all cards within hand limit {handLimit}.");
	}

	private bool TryDiscardCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? currentCharacter))
		{
			return false;
		}

		int handLimit = Math.Max(0, currentCharacter.CurrentHealth);
		if (currentCharacter.HandCardCount <= handLimit)
		{
			_isWaitingForDiscardInput = false;
			currentCharacter.ClearPendingSlashDamageBonus();
			AddBattleLog($"{currentCharacter.CharacterName} has no excess hand cards to discard.");
			RequestStateSync();
			return true;
		}

		if (!currentCharacter.TryConsumeHandCardByInstanceId(command.CardInstanceId, out CardInstance? discardedCard) || discardedCard is null)
		{
			GD.PushWarning($"{currentCharacter.CharacterName} tried to discard an invalid hand card.");
			return false;
		}

		AddBattleLog($"{currentCharacter.CharacterName} discards {discardedCard.DisplayName}.");
		EmitRuleEvent(RuleEventType.CardsDiscarded, currentCharacter, currentCharacter, discardedCard, value: 1, message: $"{currentCharacter.CharacterName} discards {discardedCard.DisplayName}.");

		if (currentCharacter.HandCardCount <= handLimit)
		{
			_isWaitingForDiscardInput = false;
			currentCharacter.ClearPendingSlashDamageBonus();
			AddBattleLog($"{currentCharacter.CharacterName} has reached hand limit {handLimit}.");
		}
		else
		{
			_isWaitingForDiscardInput = true;
			OnDiscardPhaseInputRequested?.Invoke();
		}

		RequestStateSync();
		return true;
	}

	private void AdvanceTurnOwner()
	{
		if (_turnOrder.Count == 0)
		{
			return;
		}

		ClearTurnTemporaryState(CurrentTurnPeerId);
		int nextPeerId = GetNextAlivePeerIdAfter(CurrentTurnPeerId);
		if (nextPeerId <= 0)
		{
			return;
		}

		CurrentTurnPeerId = nextPeerId;
		_currentTurnIndex = Math.Max(0, _turnOrder.IndexOf(CurrentTurnPeerId));
		OnTurnOwnerChanged?.Invoke(CurrentTurnPeerId);
		RequestStateSync();
	}

	private void ClearTurnTemporaryState(int peerId)
	{
		SlashUsesThisTurn = 0;
		WineUsesThisTurn = 0;
		if (_peerCharacters.TryGetValue(peerId, out PlayerCharacter? character))
		{
			character.ClearPendingSlashDamageBonus();
		}
	}

	private bool ShouldProcessGameFlow()
	{
		LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
		if (networkManager?.IsConnected == true)
		{
			return networkManager.IsHost;
		}

		return true;
	}

	private void HandleCharacterStatsChanged(PlayerCharacter character)
	{
		RequestStateSync();
	}

	private void HandleCharacterDefeated(PlayerCharacter character)
	{
		CheckForMatchEnd();
		RequestStateSync();
	}

	private void HandleHandCardRemoved(PlayerCharacter character, CardInstance card)
	{
		if (card.CardType == CardType.None)
		{
			return;
		}

		_discardPile.Add(card.Clone());
		EmitCardsLost(character, character, card, value: 1, message: $"{character.CharacterName} loses {card.DisplayName}.");
		RequestStateSync();
	}

	private void RequestStateSync()
	{
		OnStateChanged?.Invoke();
		OnStateSyncRequested?.Invoke();
	}

	public void NotifyCardUseStage(CardUseContext useContext, CardUseStage stage, Node? currentTarget = null)
	{
		OnCardUseStageChanged?.Invoke(useContext, stage, currentTarget);
		EmitRuleEvent(new RuleEventContext
		{
			EventType = RuleEventType.CardUseStage,
			Source = useContext.Source,
			Target = currentTarget,
			Card = useContext.Card.Clone(),
			Phase = CurrentPhase,
			CardUseStage = stage,
			ActingPeerId = CurrentTurnPeerId,
			Message = useContext.CancelReason
		});
	}

	public void BeginResponseWindow(int peerId, string prompt, ResponseWindowKind responseKind = ResponseWindowKind.None)
	{
		HasPendingResponseWindow = peerId > 0;
		PendingResponsePeerId = Math.Max(0, peerId);
		PendingResponsePrompt = prompt ?? string.Empty;
		PendingResponseKind = HasPendingResponseWindow ? responseKind : ResponseWindowKind.None;
		EmitRuleEvent(RuleEventType.ResponseWindowOpened, actingPeerId: PendingResponsePeerId, responseKind: PendingResponseKind, message: PendingResponsePrompt);
		OnResponseWindowChanged?.Invoke();
		RequestStateSync();
	}

	public void ClearResponseWindow()
	{
		bool hadWindow = HasPendingResponseWindow || PendingResponsePeerId != 0 || !string.IsNullOrWhiteSpace(PendingResponsePrompt);
		ResponseWindowKind previousResponseKind = PendingResponseKind;
		HasPendingResponseWindow = false;
		PendingResponsePeerId = 0;
		PendingResponsePrompt = string.Empty;
		PendingResponseKind = ResponseWindowKind.None;
		_pendingResponseDamageAction = null;
		_pendingDamageTargetingArgs = null;
		_pendingRequiredResponseCardType = CardType.None;
		_pendingRequiredResponseSuccess = null;
		_pendingRequiredResponseDecline = null;
		ClearPendingNullificationState();
		if (!hadWindow)
		{
			return;
		}

		EmitRuleEvent(RuleEventType.ResponseWindowClosed, responseKind: previousResponseKind);
		OnResponseWindowChanged?.Invoke();
		RequestStateSync();
	}

	public void AddBattleLog(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		_battleLog.Add(new NetworkBattleLogEntry
		{
			Sequence = _nextBattleLogSequence++,
			Message = message.Trim()
		});

		const int maxEntries = 30;
		if (_battleLog.Count > maxEntries)
		{
			_battleLog.RemoveRange(0, _battleLog.Count - maxEntries);
		}

		OnBattleLogChanged?.Invoke();
		RequestStateSync();
	}

	public void EmitRuleEvent(RuleEventType eventType, Node? source = null, Node? target = null, CardInstance? card = null, DamageInfo? damageInfo = null, int value = 0, string message = "", TurnPhase? phase = null, CardUseStage? cardUseStage = null, ResponseWindowKind responseKind = ResponseWindowKind.None, int? actingPeerId = null)
	{
		EmitRuleEvent(new RuleEventContext
		{
			EventType = eventType,
			Source = source,
			Target = target,
			Card = card?.Clone(),
			DamageInfo = damageInfo,
			Value = value,
			Message = message ?? string.Empty,
			Phase = phase ?? CurrentPhase,
			CardUseStage = cardUseStage,
			ResponseKind = responseKind,
			ActingPeerId = actingPeerId ?? CurrentTurnPeerId
		});
	}

	public void EmitRuleEvent(RuleEventContext context)
	{
		if (context is null)
		{
			return;
		}

		foreach ((PlayerCharacter character, CharacterSkill skill) in _peerCharacters.Values
			.SelectMany(character => character.Skills.Select(skill => (character, skill)))
			.OrderByDescending(entry => entry.skill.TriggerPriority)
			.ThenBy(entry => entry.character.OwnerPeerId))
		{
			skill.HandleRuleEvent(this, context);
		}

		OnRuleEvent?.Invoke(context);
		QueuePresentationEvent(context);
	}

	private void QueuePresentationEvent(RuleEventContext context)
	{
		if (!ShouldProcessGameFlow() || !ShouldReplicatePresentation(context))
		{
			return;
		}

		PlayerCharacter? sourceCharacter = context.Source as PlayerCharacter
			?? context.FromCharacter
			?? context.DamageInfo?.Source as PlayerCharacter;
		PlayerCharacter? targetCharacter = context.Target as PlayerCharacter
			?? context.ToCharacter
			?? context.DamageInfo?.Target as PlayerCharacter;
		CardType cardType = context.Card?.CardType
			?? context.DamageInfo?.CauseCardType
			?? CardType.None;

		NetworkPresentationEvent presentationEvent = new()
		{
			Sequence = _nextPresentationEventSequence++,
			EventType = context.EventType.ToString(),
			DamageType = context.DamageInfo?.Type.ToString() ?? DamageType.Physical.ToString(),
			CardType = cardType.ToString(),
			ResponseKind = context.ResponseKind.ToString(),
			Value = Math.Max(0, context.Value),
			SourcePeerId = sourceCharacter?.OwnerPeerId ?? 0,
			TargetPeerId = targetCharacter?.OwnerPeerId ?? 0,
			SourceName = sourceCharacter?.CharacterName ?? string.Empty,
			TargetName = targetCharacter?.CharacterName ?? string.Empty,
			DisplayName = context.Card?.DisplayName ?? string.Empty
		};

		_presentationEvents.Add(presentationEvent);
		const int maxPresentationEvents = 32;
		if (_presentationEvents.Count > maxPresentationEvents)
		{
			_presentationEvents.RemoveRange(0, _presentationEvents.Count - maxPresentationEvents);
		}

		OnPresentationEvent?.Invoke(ClonePresentationEvent(presentationEvent));
		RequestStateSync();
	}

	private void ApplyPresentationEventSnapshot(IEnumerable<NetworkPresentationEvent>? incomingEvents, bool wasMatchRunning)
	{
		List<NetworkPresentationEvent> orderedEvents = (incomingEvents ?? Array.Empty<NetworkPresentationEvent>())
			.Where(entry => entry is not null && entry.Sequence > 0)
			.OrderBy(entry => entry.Sequence)
			.Select(ClonePresentationEvent)
			.ToList();

		if (!wasMatchRunning && IsMatchRunning)
		{
			_presentationSnapshotInitialized = false;
			_lastAppliedPresentationSequence = 0;
		}

		_presentationEvents.Clear();
		_presentationEvents.AddRange(orderedEvents.Select(ClonePresentationEvent));
		_nextPresentationEventSequence = (orderedEvents.LastOrDefault()?.Sequence ?? 0) + 1;

		if (!_presentationSnapshotInitialized)
		{
			_presentationSnapshotInitialized = true;
			_lastAppliedPresentationSequence = orderedEvents.LastOrDefault()?.Sequence ?? 0;
			return;
		}

		foreach (NetworkPresentationEvent presentationEvent in orderedEvents.Where(entry => entry.Sequence > _lastAppliedPresentationSequence))
		{
			OnPresentationEvent?.Invoke(ClonePresentationEvent(presentationEvent));
		}

		_lastAppliedPresentationSequence = Math.Max(
			_lastAppliedPresentationSequence,
			orderedEvents.LastOrDefault()?.Sequence ?? 0);
	}

	private static bool ShouldReplicatePresentation(RuleEventContext context)
	{
		return context.EventType switch
		{
			RuleEventType.MatchStarted => true,
			RuleEventType.MatchEnded => true,
			RuleEventType.CardUsed => context.Card is not null,
			RuleEventType.DamageApplied => context.Value > 0,
			RuleEventType.Healed => context.Value > 0,
			RuleEventType.ResponseUsed => true,
			RuleEventType.CharacterDefeated => true,
			RuleEventType.CardsDrawn => context.Value > 0,
			RuleEventType.CardsDiscarded => context.Value > 0,
			RuleEventType.EquipmentEquipped => true,
			RuleEventType.JudgementPerformed => true,
			_ => false
		};
	}

	private static NetworkPresentationEvent ClonePresentationEvent(NetworkPresentationEvent source)
	{
		return new NetworkPresentationEvent
		{
			Sequence = source.Sequence,
			EventType = source.EventType ?? string.Empty,
			DamageType = source.DamageType ?? string.Empty,
			CardType = source.CardType ?? string.Empty,
			ResponseKind = source.ResponseKind ?? string.Empty,
			Value = source.Value,
			SourcePeerId = source.SourcePeerId,
			TargetPeerId = source.TargetPeerId,
			SourceName = source.SourceName ?? string.Empty,
			TargetName = source.TargetName ?? string.Empty,
			DisplayName = source.DisplayName ?? string.Empty
		};
	}

	public void EmitCardsLost(PlayerCharacter owner, Node? cause, CardInstance? card = null, int value = 1, string message = "")
	{
		if (owner is null)
		{
			return;
		}

		EmitRuleEvent(new RuleEventContext
		{
			EventType = RuleEventType.CardsLost,
			Source = cause ?? owner,
			Target = owner,
			FromCharacter = owner,
			Card = card?.Clone(),
			Value = Math.Max(0, value),
			Message = message ?? string.Empty,
			Phase = CurrentPhase,
			ActingPeerId = CurrentTurnPeerId
		});
	}

	public void EmitCardsGained(PlayerCharacter owner, Node? cause, CardInstance? card = null, int value = 1, string message = "")
	{
		if (owner is null)
		{
			return;
		}

		EmitRuleEvent(new RuleEventContext
		{
			EventType = RuleEventType.CardsGained,
			Source = cause ?? owner,
			Target = owner,
			ToCharacter = owner,
			Card = card?.Clone(),
			Value = Math.Max(0, value),
			Message = message ?? string.Empty,
			Phase = CurrentPhase,
			ActingPeerId = CurrentTurnPeerId
		});
	}

	public void EmitCardMoved(Node? cause, PlayerCharacter? from, PlayerCharacter? to, CardInstance? card, string message = "")
	{
		if (card is null)
		{
			return;
		}

		EmitRuleEvent(new RuleEventContext
		{
			EventType = RuleEventType.CardMoved,
			Source = cause,
			Target = to ?? from,
			FromCharacter = from,
			ToCharacter = to,
			Card = card.Clone(),
			Value = 1,
			Message = message ?? string.Empty,
			Phase = CurrentPhase,
			ActingPeerId = CurrentTurnPeerId
		});
	}

	private void CheckForMatchEnd()
	{
		if (!IsMatchRunning)
		{
			return;
		}

		List<PlayerCharacter> aliveCharacters = _peerCharacters.Values
			.Where(character => character.IsAlive)
			.ToList();

		if (EnableFactionVictoryRules
			&& TryResolveFactionVictory(aliveCharacters, out int factionWinnerPeerId, out string factionResultMessage))
		{
			EndMatch(factionWinnerPeerId, factionResultMessage);
			return;
		}

		if (aliveCharacters.Count > 1)
		{
			return;
		}

		if (aliveCharacters.Count == 1)
		{
			PlayerCharacter winner = aliveCharacters[0];
			EndMatch(winner.OwnerPeerId, $"{winner.CharacterName} wins the match.");
			return;
		}

		EndMatch(0, "The match ended in a draw.");
	}

	private static List<PlayerFaction> BuildIdentityFactionLayout(int playerCount)
	{
		return playerCount switch
		{
			<= 1 => new List<PlayerFaction> { PlayerFaction.Neutral },
			2 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Rebel },
			3 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Rebel, PlayerFaction.Renegade },
			4 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Loyalist, PlayerFaction.Rebel, PlayerFaction.Renegade },
			5 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Loyalist, PlayerFaction.Rebel, PlayerFaction.Rebel, PlayerFaction.Renegade },
			6 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Loyalist, PlayerFaction.Rebel, PlayerFaction.Rebel, PlayerFaction.Rebel, PlayerFaction.Renegade },
			7 => new List<PlayerFaction> { PlayerFaction.Lord, PlayerFaction.Loyalist, PlayerFaction.Loyalist, PlayerFaction.Rebel, PlayerFaction.Rebel, PlayerFaction.Rebel, PlayerFaction.Renegade },
			_ => BuildLargeIdentityFactionLayout(playerCount)
		};
	}

	private static List<PlayerFaction> BuildLargeIdentityFactionLayout(int playerCount)
	{
		List<PlayerFaction> factions = new()
		{
			PlayerFaction.Lord,
			PlayerFaction.Loyalist,
			PlayerFaction.Loyalist,
			PlayerFaction.Rebel,
			PlayerFaction.Rebel,
			PlayerFaction.Rebel,
			PlayerFaction.Rebel,
			PlayerFaction.Renegade
		};

		while (factions.Count < playerCount)
		{
			int insertIndex = Math.Max(1, factions.Count - 1);
			int loyalistCount = factions.Count(faction => faction == PlayerFaction.Loyalist);
			int rebelCount = factions.Count(faction => faction == PlayerFaction.Rebel);
			factions.Insert(insertIndex, rebelCount <= loyalistCount * 2 ? PlayerFaction.Rebel : PlayerFaction.Loyalist);
		}

		return factions;
	}

	private void ShuffleList<T>(IList<T> values, int startIndex = 0)
	{
		for (int index = values.Count - 1; index > startIndex; index--)
		{
			int swapIndex = _random.Next(startIndex, index + 1);
			(values[index], values[swapIndex]) = (values[swapIndex], values[index]);
		}
	}

	private bool TryResolveFactionVictory(IReadOnlyList<PlayerCharacter> aliveCharacters, out int winnerPeerId, out string resultMessage)
	{
		winnerPeerId = 0;
		resultMessage = string.Empty;

		List<PlayerCharacter> allCharacters = _peerCharacters.Values.ToList();
		bool hasIdentityFactions = allCharacters.Any(character => character.Faction is PlayerFaction.Lord
			or PlayerFaction.Loyalist
			or PlayerFaction.Rebel
			or PlayerFaction.Renegade);
		if (!hasIdentityFactions)
		{
			return false;
		}

		if (aliveCharacters.Count == 0)
		{
			resultMessage = "The identity match ended in a draw.";
			return true;
		}

		bool lordExists = allCharacters.Any(character => character.Faction == PlayerFaction.Lord);
		bool lordAlive = aliveCharacters.Any(character => character.Faction == PlayerFaction.Lord);
		if (lordExists && !lordAlive)
		{
			PlayerCharacter? renegadeWinner = aliveCharacters.Count == 1 && aliveCharacters[0].Faction == PlayerFaction.Renegade
				? aliveCharacters[0]
				: null;
			if (renegadeWinner is not null)
			{
				winnerPeerId = renegadeWinner.OwnerPeerId;
				resultMessage = $"{renegadeWinner.CharacterName} wins as Renegade.";
				return true;
			}

			PlayerCharacter? rebelRepresentative = aliveCharacters.FirstOrDefault(character => character.Faction == PlayerFaction.Rebel)
				?? aliveCharacters.FirstOrDefault();
			winnerPeerId = rebelRepresentative?.OwnerPeerId ?? 0;
			resultMessage = "Rebel faction wins because the Lord is defeated.";
			return true;
		}

		bool rebelAlive = aliveCharacters.Any(character => character.Faction == PlayerFaction.Rebel);
		bool renegadeAlive = aliveCharacters.Any(character => character.Faction == PlayerFaction.Renegade);
		if (lordAlive && !rebelAlive && !renegadeAlive)
		{
			PlayerCharacter? lord = aliveCharacters.FirstOrDefault(character => character.Faction == PlayerFaction.Lord);
			winnerPeerId = lord?.OwnerPeerId ?? 0;
			resultMessage = "Lord faction wins because all Rebels and Renegades are defeated.";
			return true;
		}

		return false;
	}

	private void TriggerTurnStartSkills()
	{
		if (!_peerCharacters.TryGetValue(CurrentTurnPeerId, out PlayerCharacter? currentCharacter))
		{
			return;
		}

		foreach (CharacterSkill skill in currentCharacter.Skills)
		{
			skill.OnTurnStart(this);
		}
	}

	public bool PlaceDelayedTrick(PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, CardInstance delayedTrick)
	{
		if (sourceCharacter is null || targetCharacter is null || delayedTrick is null || !delayedTrick.IsDelayedTrick)
		{
			return false;
		}

		if (!targetCharacter.AddDelayedTrick(delayedTrick.Clone()))
		{
			AddBattleLog($"{targetCharacter.CharacterName} cannot receive {delayedTrick.DisplayName}.");
			return false;
		}

		AddBattleLog($"{sourceCharacter.CharacterName} places {delayedTrick.DisplayName} on {targetCharacter.CharacterName}.");
		EmitCardsGained(targetCharacter, sourceCharacter, delayedTrick, value: 1, message: $"{targetCharacter.CharacterName} receives {delayedTrick.DisplayName} in judgement area.");
		EmitCardMoved(sourceCharacter, sourceCharacter, targetCharacter, delayedTrick, $"{delayedTrick.DisplayName} moves from {sourceCharacter.CharacterName} to {targetCharacter.CharacterName}'s judgement area.");
		RequestStateSync();
		return true;
	}

	public bool BeginRequiredCardResponseWindow(PlayerCharacter responder, CardType requiredCardType, string prompt, Action<PlayerCharacter>? onSuccess, Action<PlayerCharacter>? onDecline, bool allowAutomaticDodge = true)
	{
		if (responder is null || requiredCardType == CardType.None)
		{
			return false;
		}

		ResponseWindowKind responseKind = requiredCardType switch
		{
			CardType.Peach => ResponseWindowKind.Peach,
			CardType.Dodge => ResponseWindowKind.Dodge,
			_ when CardRules.IsSlash(requiredCardType) || requiredCardType == CardType.Slash => ResponseWindowKind.Slash,
			_ => ResponseWindowKind.None
		};

		if (responseKind == ResponseWindowKind.None)
		{
			return false;
		}

		if (allowAutomaticDodge
			&& requiredCardType == CardType.Dodge
			&& TryJudgeEightDiagramDodge(responder))
		{
			onSuccess?.Invoke(responder);
			return false;
		}

		if (!HasRequiredResponseCard(responder, requiredCardType))
		{
			AddBattleLog($"{responder.CharacterName} has no {requiredCardType} to respond with.");
			onDecline?.Invoke(responder);
			return false;
		}

		_pendingRequiredResponseCardType = requiredCardType;
		_pendingRequiredResponseSuccess = onSuccess;
		_pendingRequiredResponseDecline = onDecline;
		BeginResponseWindow(responder.OwnerPeerId, prompt, responseKind);
		return true;
	}

	private static bool HasRequiredResponseCard(PlayerCharacter responder, CardType requiredCardType)
	{
		if (CardRules.IsSlash(requiredCardType) || requiredCardType == CardType.Slash)
		{
			return responder.HandCards.Any(card => CardRules.IsSlash(card.CardType))
				|| responder.CanUseSerpentSpear && responder.HandCardCount >= 2;
		}

		return responder.FindFirstHandCardOfType(requiredCardType) is not null;
	}

	private bool TryJudgeEightDiagramDodge(PlayerCharacter responder)
	{
		if (responder.EquippedArmor?.EquipmentEffect != EquipmentEffectType.EightDiagram)
		{
			return false;
		}

		if (!JudgeCard(responder, "Eight Diagram", card => CardRules.IsRedSuit(card.Suit)))
		{
			return false;
		}

		AddBattleLog($"{responder.CharacterName}'s Eight Diagram provides a Dodge.");
		EmitRuleEvent(
			RuleEventType.ResponseUsed,
			responder,
			responder,
			value: 1,
			message: $"{responder.CharacterName}'s Eight Diagram provides a Dodge.",
			responseKind: ResponseWindowKind.Dodge,
			actingPeerId: responder.OwnerPeerId);
		return true;
	}

	public bool BeginNullificationWindow(PlayerCharacter sourceCharacter, CardInstance trickCard, Action<PlayerCharacter>? onNullified, Action? onPass)
	{
		if (sourceCharacter is null || trickCard is null || !CardRules.IsTrick(trickCard.CardType))
		{
			onPass?.Invoke();
			return false;
		}

		_pendingNullificationSuccess = onNullified;
		_pendingNullificationDecline = onPass;
		_pendingNullificationSubjectName = string.IsNullOrWhiteSpace(trickCard.DisplayName) ? trickCard.CardType.ToString() : trickCard.DisplayName;
		_pendingNullificationResponseOrder.Clear();
		_pendingNullificationResponseOrder.AddRange(_peerCharacters.Values
			.Where(character => character.IsAlive && character.FindFirstHandCardOfType(CardType.Nullification) is not null)
			.OrderBy(character => GetTurnOrderDistance(CurrentTurnPeerId, character.OwnerPeerId))
			.Select(character => character.OwnerPeerId));
		_pendingNullificationResponseIndex = 0;
		return ContinueNullificationWindow();
	}

	private bool ContinueNullificationWindow()
	{
		while (_pendingNullificationResponseIndex < _pendingNullificationResponseOrder.Count)
		{
			int responderPeerId = _pendingNullificationResponseOrder[_pendingNullificationResponseIndex++];
			if (!_peerCharacters.TryGetValue(responderPeerId, out PlayerCharacter? responder)
				|| !responder.IsAlive
				|| responder.FindFirstHandCardOfType(CardType.Nullification) is null)
			{
				continue;
			}

			BeginResponseWindow(responder.OwnerPeerId, $"Play Nullification to cancel {_pendingNullificationSubjectName}, or pass.", ResponseWindowKind.Nullification);
			return true;
		}

		Action? onPass = _pendingNullificationDecline;
		ClearPendingNullificationState();
		onPass?.Invoke();
		return false;
	}

	private void ClearPendingNullificationState()
	{
		_pendingNullificationSuccess = null;
		_pendingNullificationDecline = null;
		_pendingNullificationSubjectName = string.Empty;
		_pendingNullificationResponseOrder.Clear();
		_pendingNullificationResponseIndex = 0;
	}

	public void ResolveHarvest(PlayerCharacter sourceCharacter, IEnumerable<PlayerCharacter> targets)
	{
		if (sourceCharacter is null || targets is null)
		{
			return;
		}

		List<PlayerCharacter> orderedTargets = targets
			.Where(target => target is not null && target.IsAlive)
			.OrderBy(target => GetTurnOrderDistance(sourceCharacter.OwnerPeerId, target.OwnerPeerId))
			.ToList();
		if (orderedTargets.Count == 0)
		{
			return;
		}

		ClearHarvestSelection(discardRemainingCards: true);

		List<CardInstance> harvestPool = new();
		for (int i = 0; i < orderedTargets.Count; i++)
		{
			if (!TryDrawCard(out CardInstance? card) || card is null)
			{
				break;
			}

			harvestPool.Add(card);
		}

		if (harvestPool.Count == 0)
		{
			AddBattleLog($"{sourceCharacter.CharacterName} uses Harvest, but the draw pile is empty.");
			return;
		}

		AddBattleLog($"{sourceCharacter.CharacterName} reveals {harvestPool.Count} card(s) for Harvest: {string.Join(", ", harvestPool.Select(FormatCardIdentity))}.");
		_harvestPool.AddRange(harvestPool);
		foreach (PlayerCharacter target in orderedTargets)
		{
			_pendingHarvestPeerOrder.Enqueue(target.OwnerPeerId);
		}

		ContinueHarvestSelection();
	}

	private void ContinueHarvestSelection()
	{
		PendingHarvestPeerId = 0;
		PendingHarvestPrompt = string.Empty;

		while (_pendingHarvestPeerOrder.Count > 0 && _harvestPool.Count > 0)
		{
			int peerId = _pendingHarvestPeerOrder.Dequeue();
			if (!_peerCharacters.TryGetValue(peerId, out PlayerCharacter? target) || !target.IsAlive)
			{
				continue;
			}

			PendingHarvestPeerId = peerId;
			PendingHarvestPrompt = $"Choose one card from Harvest. Remaining: {_harvestPool.Count}.";
			AddBattleLog($"{target.CharacterName} is choosing a card from Harvest.");
			RequestStateSync();
			return;
		}

		ClearHarvestSelection(discardRemainingCards: true);
		AddBattleLog("Harvest selection finished.");
		RequestStateSync();
	}

	private bool TryChooseHarvestCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? character))
		{
			return false;
		}

		CardInstance? selectedCard = _harvestPool.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
			?? _harvestPool.FirstOrDefault();
		if (selectedCard is null)
		{
			ClearHarvestSelection(discardRemainingCards: false);
			return false;
		}

		_harvestPool.Remove(selectedCard);
		character.AddCard(selectedCard);
		AddBattleLog($"{character.CharacterName} takes {FormatCardIdentity(selectedCard)} from Harvest.");
		EmitRuleEvent(RuleEventType.CardsDrawn, character, character, selectedCard, value: 1, message: $"{character.CharacterName} takes {selectedCard.DisplayName} from Harvest.");
		EmitCardsGained(character, character, selectedCard, value: 1, message: $"{character.CharacterName} gains {selectedCard.DisplayName} from Harvest.");
		EmitCardMoved(character, null, character, selectedCard, $"{selectedCard.DisplayName} moves from Harvest to {character.CharacterName}.");
		ContinueHarvestSelection();
		return true;
	}

	private void ClearHarvestSelection(bool discardRemainingCards)
	{
		if (discardRemainingCards)
		{
			foreach (CardInstance remainingCard in _harvestPool)
			{
				DiscardCardToPile(remainingCard);
			}
		}

		_harvestPool.Clear();
		_pendingHarvestPeerOrder.Clear();
		PendingHarvestPeerId = 0;
		PendingHarvestPrompt = string.Empty;
	}

	public bool BeginTargetCardSelection(
		PlayerCharacter sourceCharacter,
		PlayerCharacter targetCharacter,
		string prompt,
		Action<PlayerCharacter, PlayerCharacter, CardInstance, bool> onSelected,
		Func<CardInstance, bool>? cardFilter = null)
	{
		if (sourceCharacter is null || targetCharacter is null || onSelected is null)
		{
			return false;
		}

		ClearTargetCardSelection();
		List<CardInstance> selectableCards = BuildSelectableCardsFromTarget(targetCharacter);
		if (cardFilter is not null)
		{
			selectableCards = selectableCards.Where(cardFilter).ToList();
		}
		if (selectableCards.Count == 0)
		{
			AddBattleLog($"{targetCharacter.CharacterName} has no selectable card.");
			return false;
		}

		_pendingTargetCardSelectionSource = sourceCharacter;
		_pendingTargetCardSelectionTarget = targetCharacter;
		_pendingTargetCardSelectionResolved = onSelected;
		_targetCardSelectionPool.AddRange(selectableCards);
		PendingTargetCardSelectionPeerId = sourceCharacter.OwnerPeerId;
		PendingTargetCardSelectionPrompt = string.IsNullOrWhiteSpace(prompt)
			? $"Choose one card from {targetCharacter.CharacterName}."
			: prompt;
		AddBattleLog($"{sourceCharacter.CharacterName} is choosing one card from {targetCharacter.CharacterName}.");
		RequestStateSync();
		return true;
	}

	private bool TryChooseTargetCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_pendingTargetCardSelectionSource is null
			|| _pendingTargetCardSelectionTarget is null
			|| _pendingTargetCardSelectionResolved is null)
		{
			ClearTargetCardSelection();
			return false;
		}

		CardInstance? selectedPublicCard = _targetCardSelectionPool.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
			?? _targetCardSelectionPool.FirstOrDefault();
		if (selectedPublicCard is null)
		{
			ClearTargetCardSelection();
			return false;
		}

		PlayerCharacter sourceCharacter = _pendingTargetCardSelectionSource;
		PlayerCharacter targetCharacter = _pendingTargetCardSelectionTarget;
		bool selectedFromHiddenHand = selectedPublicCard.CardType == CardType.None;
		Action<PlayerCharacter, PlayerCharacter, CardInstance, bool> onSelected = _pendingTargetCardSelectionResolved;
		ClearTargetCardSelection();

		if (!targetCharacter.TryRemoveCardOrEquipmentByInstanceId(selectedPublicCard.InstanceId, out CardInstance? removedCard) || removedCard is null)
		{
			AddBattleLog($"{targetCharacter.CharacterName}'s selected card is no longer available.");
			return false;
		}

		if (CardRules.IsEquipment(removedCard.CardType))
		{
			NotifyEquipmentLost(targetCharacter, sourceCharacter, removedCard);
		}

		EmitCardsLost(targetCharacter, sourceCharacter, removedCard, value: 1, message: $"{targetCharacter.CharacterName} loses {removedCard.DisplayName}.");
		onSelected(sourceCharacter, targetCharacter, removedCard, selectedFromHiddenHand);
		RequestStateSync();
		return true;
	}

	private static List<CardInstance> BuildSelectableCardsFromTarget(PlayerCharacter targetCharacter)
	{
		List<CardInstance> cards = new();
		cards.AddRange(targetCharacter.HandCards.Select(card => new CardInstance
		{
			InstanceId = card.InstanceId,
			CardType = CardType.None,
			DisplayName = "Hand Card",
			Description = "A hidden hand card.",
			Suit = CardSuit.None,
			Rank = 0
		}));

		AddVisibleCard(cards, targetCharacter.EquippedWeapon);
		AddVisibleCard(cards, targetCharacter.EquippedArmor);
		AddVisibleCard(cards, targetCharacter.EquippedOffensiveHorse);
		AddVisibleCard(cards, targetCharacter.EquippedDefensiveHorse);
		AddVisibleCard(cards, targetCharacter.EquippedTreasure);
		cards.AddRange(targetCharacter.DelayedTricks.Select(card => card.Clone()));
		return cards;
	}

	private static void AddVisibleCard(List<CardInstance> cards, CardInstance? card)
	{
		if (card is not null)
		{
			cards.Add(card.Clone());
		}
	}

	private void ClearTargetCardSelection()
	{
		_targetCardSelectionPool.Clear();
		PendingTargetCardSelectionPeerId = 0;
		PendingTargetCardSelectionPrompt = string.Empty;
		_pendingTargetCardSelectionSource = null;
		_pendingTargetCardSelectionTarget = null;
		_pendingTargetCardSelectionResolved = null;
	}

	public bool BeginHandCardSelection(
		PlayerCharacter character,
		string prompt,
		Action<PlayerCharacter, CardInstance> onSelected,
		Func<CardInstance, bool>? cardFilter = null,
		bool consumeSelectedCard = true,
		bool canDecline = false,
		Action<PlayerCharacter>? onDecline = null)
	{
		if (character is null || onSelected is null)
		{
			return false;
		}

		ClearHandCardSelection();
		List<CardInstance> selectableCards = character.HandCards
			.Where(card => cardFilter?.Invoke(card) ?? true)
			.Select(card => card.Clone())
			.ToList();
		if (selectableCards.Count == 0)
		{
			AddBattleLog($"{character.CharacterName} has no selectable hand card.");
			return false;
		}

		_pendingHandCardSelectionCharacter = character;
		_pendingHandCardSelectionResolved = onSelected;
		_pendingHandCardSelectionDeclined = onDecline;
		_pendingHandCardSelectionConsumesCard = consumeSelectedCard;
		CanDeclineHandCardSelection = canDecline;
		_handCardSelectionPool.AddRange(selectableCards);
		PendingHandCardSelectionPeerId = character.OwnerPeerId;
		PendingHandCardSelectionPrompt = string.IsNullOrWhiteSpace(prompt)
			? $"Choose one hand card from {character.CharacterName}."
			: prompt;
		AddBattleLog($"{character.CharacterName} is choosing one hand card.");
		RequestStateSync();
		return true;
	}

	private bool TryChooseHandCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_pendingHandCardSelectionCharacter is null || _pendingHandCardSelectionResolved is null)
		{
			ClearHandCardSelection();
			return false;
		}

		CardInstance? selectedPublicCard = _handCardSelectionPool.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
			?? _handCardSelectionPool.FirstOrDefault();
		if (selectedPublicCard is null)
		{
			ClearHandCardSelection();
			return false;
		}

		PlayerCharacter character = _pendingHandCardSelectionCharacter;
		Action<PlayerCharacter, CardInstance> onSelected = _pendingHandCardSelectionResolved;
		bool consumesCard = _pendingHandCardSelectionConsumesCard;
		ClearHandCardSelection();

		CardInstance? selectedCard;
		if (consumesCard)
		{
			if (!character.TryConsumeHandCardByInstanceId(selectedPublicCard.InstanceId, out CardInstance? consumedCard) || consumedCard is null)
			{
				AddBattleLog($"{character.CharacterName}'s selected hand card is no longer available.");
				return false;
			}

			selectedCard = consumedCard;
		}
		else
		{
			selectedCard = character.FindHandCard(selectedPublicCard.InstanceId)?.Clone();
			if (selectedCard is null)
			{
				AddBattleLog($"{character.CharacterName}'s selected hand card is no longer available.");
				return false;
			}
		}

		onSelected(character, selectedCard);
		RequestStateSync();
		return true;
	}

	private bool TryDeclineHandCardSelectionCommand(int actingPeerId)
	{
		if (!CanDeclineHandCardSelection
			|| _pendingHandCardSelectionCharacter is null
			|| _pendingHandCardSelectionCharacter.OwnerPeerId != actingPeerId)
		{
			return false;
		}

		PlayerCharacter character = _pendingHandCardSelectionCharacter;
		Action<PlayerCharacter>? onDecline = _pendingHandCardSelectionDeclined;
		ClearHandCardSelection();
		onDecline?.Invoke(character);
		RequestStateSync();
		return true;
	}

	private void ClearHandCardSelection()
	{
		_handCardSelectionPool.Clear();
		PendingHandCardSelectionPeerId = 0;
		PendingHandCardSelectionPrompt = string.Empty;
		CanDeclineHandCardSelection = false;
		_pendingHandCardSelectionCharacter = null;
		_pendingHandCardSelectionResolved = null;
		_pendingHandCardSelectionDeclined = null;
		_pendingHandCardSelectionConsumesCard = true;
	}

	private int GetTurnOrderDistance(int fromPeerId, int toPeerId)
	{
		if (_turnOrder.Count == 0)
		{
			return 0;
		}

		int fromIndex = _turnOrder.IndexOf(fromPeerId);
		int toIndex = _turnOrder.IndexOf(toPeerId);
		if (fromIndex < 0 || toIndex < 0)
		{
			return int.MaxValue;
		}

		return (toIndex - fromIndex + _turnOrder.Count) % _turnOrder.Count;
	}

	public void BeginRequiredCardResponseSequence(
		PlayerCharacter sourceCharacter,
		IEnumerable<PlayerCharacter> targets,
		CardType requiredCardType,
		string sequenceName,
		Func<PlayerCharacter, string> promptFactory,
		Action<PlayerCharacter>? onSuccess,
		Action<PlayerCharacter> onDecline,
		CardInstance? nullifiableEffectCard = null)
	{
		if (sourceCharacter is null || targets is null || promptFactory is null || onDecline is null)
		{
			return;
		}

		Queue<PlayerCharacter> pendingTargets = new(targets.Where(target => target is not null && target.IsAlive));
		ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
	}

	private void ContinueRequiredCardResponseSequence(
		PlayerCharacter sourceCharacter,
		CardType requiredCardType,
		string sequenceName,
		Func<PlayerCharacter, string> promptFactory,
		Action<PlayerCharacter>? onSuccess,
		Action<PlayerCharacter> onDecline,
		Queue<PlayerCharacter> pendingTargets,
		CardInstance? nullifiableEffectCard)
	{
		while (pendingTargets.Count > 0)
		{
			PlayerCharacter target = pendingTargets.Dequeue();
			if (!target.IsAlive)
			{
				continue;
			}

			if (nullifiableEffectCard is not null)
			{
				CardInstance subjectCard = nullifiableEffectCard.Clone();
				subjectCard.DisplayName = $"{nullifiableEffectCard.DisplayName} on {target.CharacterName}";
				BeginNullificationWindow(
					sourceCharacter,
					subjectCard,
					responder =>
					{
						AddBattleLog($"{nullifiableEffectCard.DisplayName} on {target.CharacterName} is cancelled by {responder.CharacterName}'s Nullification.");
						ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
					},
					() => ResolveRequiredCardResponseTarget(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, target, nullifiableEffectCard));
				return;
			}

			ResolveRequiredCardResponseTarget(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, target, nullifiableEffectCard);
			return;
		}

		if (!string.IsNullOrWhiteSpace(sequenceName))
		{
			AddBattleLog($"{sequenceName} response sequence finished.");
		}
	}

	private void ResolveRequiredCardResponseTarget(
		PlayerCharacter sourceCharacter,
		CardType requiredCardType,
		string sequenceName,
		Func<PlayerCharacter, string> promptFactory,
		Action<PlayerCharacter>? onSuccess,
		Action<PlayerCharacter> onDecline,
		Queue<PlayerCharacter> pendingTargets,
		PlayerCharacter target,
		CardInstance? nullifiableEffectCard)
	{
		while (true)
		{
			if (requiredCardType == CardType.Dodge && TryJudgeEightDiagramDodge(target))
			{
				onSuccess?.Invoke(target);
				ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
				return;
			}

			if (!HasRequiredResponseCard(target, requiredCardType))
			{
				AddBattleLog($"{target.CharacterName} has no {requiredCardType} to respond with.");
				onDecline(target);
				ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
				return;
			}

			bool opened = BeginRequiredCardResponseWindow(
				target,
				requiredCardType,
				promptFactory(target),
				responder =>
				{
					onSuccess?.Invoke(responder);
					ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
				},
				responder =>
				{
					onDecline(responder);
					ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
				},
				allowAutomaticDodge: false);

			if (opened)
			{
				return;
			}

			ContinueRequiredCardResponseSequence(sourceCharacter, requiredCardType, sequenceName, promptFactory, onSuccess, onDecline, pendingTargets, nullifiableEffectCard);
			return;
		}
	}

	private void ExecuteDelayedTricks(PlayerCharacter currentCharacter)
	{
		while (currentCharacter.TryPopFirstDelayedTrick(out CardInstance? delayedTrick) && delayedTrick is not null)
		{
			switch (delayedTrick.CardType)
			{
				case CardType.Indulgence:
					if (JudgeCard(currentCharacter, "Indulgence", card => card.Suit == CardSuit.Heart))
					{
						AddBattleLog($"{currentCharacter.CharacterName}'s Indulgence judgement succeeds. PlayPhase is not skipped.");
					}
					else
					{
						_skipPlayPhasePeerIds.Add(currentCharacter.OwnerPeerId);
						AddBattleLog($"{currentCharacter.CharacterName}'s Indulgence resolves: skip PlayPhase.");
					}

					DiscardCardToPile(delayedTrick);
					break;

				case CardType.SupplyShortage:
					if (JudgeCard(currentCharacter, "Supply Shortage", card => card.Suit == CardSuit.Club))
					{
						AddBattleLog($"{currentCharacter.CharacterName}'s Supply Shortage judgement succeeds. DrawPhase is not skipped.");
					}
					else
					{
						_skipDrawPhasePeerIds.Add(currentCharacter.OwnerPeerId);
						AddBattleLog($"{currentCharacter.CharacterName}'s Supply Shortage resolves: skip DrawPhase.");
					}

					DiscardCardToPile(delayedTrick);
					break;

				case CardType.Lightning:
					ResolveLightning(currentCharacter, delayedTrick);
					break;

				default:
					DiscardCardToPile(delayedTrick);
					break;
			}
		}
	}

	private void ResolveLightning(PlayerCharacter currentCharacter, CardInstance lightningCard)
	{
		bool strikes = JudgeCard(currentCharacter, "Lightning", card => card.Suit == CardSuit.Spade && card.Rank >= 2 && card.Rank <= 9);
		if (strikes)
		{
			DiscardCardToPile(lightningCard);
			AddBattleLog($"{currentCharacter.CharacterName}'s Lightning strikes for 3 thunder damage.");
			if (_actionManager is not null)
			{
				_actionManager.AddToBottom(new DamageAction(_actionManager, new DamageInfo(currentCharacter, currentCharacter, 3, DamageType.Thunder, allowsDodgeResponse: false)));
			}

			return;
		}

		int nextPeerId = GetNextAlivePeerIdAfter(currentCharacter.OwnerPeerId);
		if (nextPeerId > 0
			&& nextPeerId != currentCharacter.OwnerPeerId
			&& _peerCharacters.TryGetValue(nextPeerId, out PlayerCharacter? nextCharacter)
			&& !nextCharacter.HasDelayedTrick(CardType.Lightning))
		{
			nextCharacter.AddDelayedTrick(lightningCard);
			AddBattleLog($"{currentCharacter.CharacterName}'s Lightning passes to {nextCharacter.CharacterName}.");
			RequestStateSync();
			return;
		}

		DiscardCardToPile(lightningCard);
		AddBattleLog($"{currentCharacter.CharacterName}'s Lightning dissipates.");
	}

	private bool JudgeCard(PlayerCharacter judgeTarget, string reason, Func<CardInstance, bool> predicate)
	{
		if (judgeTarget is null || predicate is null)
		{
			return false;
		}

		if (!TryDrawCard(out CardInstance? judgementCard) || judgementCard is null)
		{
			AddBattleLog($"{judgeTarget.CharacterName}'s {reason} judgement cannot be performed because the draw pile is empty.");
			return false;
		}

		bool succeeded = predicate(judgementCard);
		AddBattleLog($"{judgeTarget.CharacterName} judges {FormatCardIdentity(judgementCard)} for {reason}: {(succeeded ? "success" : "failure")}.");
		EmitRuleEvent(RuleEventType.JudgementPerformed, judgeTarget, judgeTarget, judgementCard, value: succeeded ? 1 : 0, message: $"{judgeTarget.CharacterName} judges {FormatCardIdentity(judgementCard)} for {reason}: {(succeeded ? "success" : "failure")}.");
		DiscardCardToPile(judgementCard);
		return succeeded;
	}

	private bool TryUsePeachCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_actionManager is null)
		{
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		CardInstance? playedCard = sourceCharacter.FindHandCard(command.CardInstanceId);
		if (playedCard is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use an unknown card instance: {command.CardInstanceId}.");
			return false;
		}

		if (playedCard.CardType != CardType.Peach)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use unsupported card type {playedCard.CardType} as Peach.");
			return false;
		}

		if (sourceCharacter.CurrentHealth >= sourceCharacter.MaxHealth)
		{
			GD.PushWarning($"{sourceCharacter.CharacterName} cannot use Peach at full health.");
			return false;
		}

		PeachUseAction peachUseAction = new(_actionManager, sourceCharacter, sourceCharacter, playedCard);
		if (!SubmitPlayPhaseAction(peachUseAction))
		{
			return false;
		}

		sourceCharacter.TryConsumeHandCardByInstanceId(command.CardInstanceId, out _);
		EmitCardUsed(sourceCharacter, playedCard, sourceCharacter);
		return true;
	}

	private bool TryUseEquipmentCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_actionManager is null)
		{
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		if (!sourceCharacter.TakeHandCardByInstanceId(command.CardInstanceId, out CardInstance? equipmentCard) || equipmentCard is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to equip an unknown card instance: {command.CardInstanceId}.");
			return false;
		}

		if (!CardRules.IsEquipment(equipmentCard.CardType))
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use unsupported card type {equipmentCard.CardType} as Equipment.");
			sourceCharacter.AddCard(equipmentCard);
			return false;
		}

		EquipmentUseAction equipmentUseAction = new(_actionManager, sourceCharacter, equipmentCard, DiscardCardToPile);
		if (!SubmitPlayPhaseAction(equipmentUseAction))
		{
			sourceCharacter.AddCard(equipmentCard);
			return false;
		}

		EmitCardsLost(sourceCharacter, sourceCharacter, equipmentCard, value: 1, message: $"{sourceCharacter.CharacterName} loses {equipmentCard.DisplayName} from hand.");
		EmitCardUsed(sourceCharacter, equipmentCard, sourceCharacter);
		return true;
	}

	private bool TryUseGenericCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (_actionManager is null)
		{
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		CardInstance? playedCard = sourceCharacter.FindHandCard(command.CardInstanceId);
		if (playedCard is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use an unknown card instance: {command.CardInstanceId}.");
			return false;
		}

		if (CardRules.IsSlash(playedCard.CardType))
		{
			return TrySubmitSlashCardCommand(actingPeerId, command);
		}

		if (playedCard.CardType == CardType.Peach)
		{
			command.CommandType = NetworkPlayCommandType.UsePeach;
			return TryUsePeachCommand(actingPeerId, command);
		}

		if (playedCard.CardType == CardType.Wine)
		{
			if (WineUsesThisTurn >= 1)
			{
				GD.PushWarning($"{sourceCharacter.CharacterName} has already used Wine this turn.");
				return false;
			}

			WineUseAction wineUseAction = new(_actionManager, sourceCharacter, playedCard);
			if (!SubmitPlayPhaseAction(wineUseAction))
			{
				return false;
			}

			WineUsesThisTurn++;
			EmitCardUsed(sourceCharacter, playedCard, sourceCharacter);
			sourceCharacter.TryConsumeHandCardByInstanceId(command.CardInstanceId, out _);
			return true;
		}

		if (CardRules.IsEquipment(playedCard.CardType))
		{
			command.CommandType = NetworkPlayCommandType.UseEquipment;
			return TryUseEquipmentCommand(actingPeerId, command);
		}

		if (!CardRules.IsTrick(playedCard.CardType))
		{
			GD.PushWarning($"Peer {actingPeerId} tried to use unsupported card type {playedCard.CardType}.");
			return false;
		}

		if (CardRules.IsResponseOnly(playedCard.CardType))
		{
			GD.PushWarning($"{playedCard.DisplayName} can only be used in a response window.");
			return false;
		}

		List<PlayerCharacter> targets = ResolveTargetsForCard(sourceCharacter, playedCard, command);
		if (CardRules.NeedsTarget(playedCard.CardType) && targets.Count == 0)
		{
			GD.PushWarning($"{playedCard.DisplayName} requires a valid target.");
			return false;
		}

		if (playedCard.IsDelayedTrick && targets.Count > 0 && targets[0].HasDelayedTrick(playedCard.CardType))
		{
			GD.PushWarning($"{targets[0].CharacterName} already has {playedCard.DisplayName} in their judgement area.");
			return false;
		}

		TrickUseAction trickUseAction = new(_actionManager, sourceCharacter, playedCard, targets.ToArray());
		if (!SubmitPlayPhaseAction(trickUseAction))
		{
			return false;
		}

		if (playedCard.IsDelayedTrick)
		{
			if (sourceCharacter.TakeHandCardByInstanceId(command.CardInstanceId, out CardInstance? removedDelayedTrick) && removedDelayedTrick is not null)
			{
				EmitCardsLost(sourceCharacter, sourceCharacter, removedDelayedTrick, value: 1, message: $"{sourceCharacter.CharacterName} loses {removedDelayedTrick.DisplayName}.");
			}
		}
		else
		{
			sourceCharacter.TryConsumeHandCardByInstanceId(command.CardInstanceId, out _);
		}

		EmitCardUsed(sourceCharacter, playedCard, targets.FirstOrDefault());
		return true;
	}

	private bool TryRecastCardCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		if (!sourceCharacter.TakeHandCardByInstanceId(command.CardInstanceId, out CardInstance? recastCard) || recastCard is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to recast an unknown card instance: {command.CardInstanceId}.");
			return false;
		}

		if (!CardRules.CanRecast(recastCard.CardType))
		{
			sourceCharacter.AddCard(recastCard);
			GD.PushWarning($"{recastCard.DisplayName} cannot be recast.");
			return false;
		}

		DiscardCardToPile(recastCard);
		AddBattleLog($"{sourceCharacter.CharacterName} recasts {recastCard.DisplayName}.");
		EmitRuleEvent(RuleEventType.CardRecast, sourceCharacter, sourceCharacter, recastCard, value: 1, message: $"{sourceCharacter.CharacterName} recasts {recastCard.DisplayName}.");
		EmitCardsLost(sourceCharacter, sourceCharacter, recastCard, value: 1, message: $"{sourceCharacter.CharacterName} loses {recastCard.DisplayName} for recast.");
		EmitRuleEvent(RuleEventType.CardsDiscarded, sourceCharacter, sourceCharacter, recastCard, value: 1, message: $"{sourceCharacter.CharacterName} discards {recastCard.DisplayName} for recast.");
		DrawCardsForEffect(sourceCharacter, 1, $"{sourceCharacter.CharacterName} draws {{count}} card(s) after recast.", $"{sourceCharacter.CharacterName} draws after recast.");
		_isWaitingForPlayInput = true;
		_hasQueuedPlayPhaseAction = false;
		OnPlayPhaseInputRequested?.Invoke();
		RequestStateSync();
		return true;
	}

	private bool TryActivateSkillCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? sourceCharacter))
		{
			GD.PushWarning($"No source PlayerCharacter registered for peer {actingPeerId}.");
			return false;
		}

		if (string.IsNullOrWhiteSpace(command.SkillId))
		{
			GD.PushWarning($"{sourceCharacter.CharacterName} tried to activate a skill without SkillId.");
			return false;
		}

		CharacterSkill? skill = sourceCharacter.Skills.FirstOrDefault(skill => string.Equals(skill.SkillId, command.SkillId, StringComparison.OrdinalIgnoreCase));
		if (skill is null)
		{
			GD.PushWarning($"{sourceCharacter.CharacterName} does not have skill {command.SkillId}.");
			return false;
		}

		SkillActivationContext activationContext = new(
			sourceCharacter,
			ResolveSkillActivationTargets(command),
			command.CardInstanceId);
		if (!ValidateSkillActivationShape(skill, activationContext, out string shapeReason))
		{
			GD.PushWarning(shapeReason);
			AddBattleLog($"{sourceCharacter.CharacterName} failed to activate {skill.DisplayName}: {shapeReason}");
			return false;
		}

		if (!skill.CanActivate(this, activationContext, out string reason))
		{
			string resolvedReason = string.IsNullOrWhiteSpace(reason) ? $"{skill.DisplayName} cannot be activated." : reason;
			GD.PushWarning(resolvedReason);
			AddBattleLog($"{sourceCharacter.CharacterName} failed to activate {skill.DisplayName}: {resolvedReason}");
			return false;
		}

		if (!skill.Activate(this, activationContext) || activationContext.IsCancelled)
		{
			string resolvedReason = string.IsNullOrWhiteSpace(activationContext.CancelReason) ? $"{skill.DisplayName} activation was cancelled." : activationContext.CancelReason;
			GD.PushWarning(resolvedReason);
			AddBattleLog($"{sourceCharacter.CharacterName} failed to activate {skill.DisplayName}: {resolvedReason}");
			return false;
		}

		AddBattleLog($"{sourceCharacter.CharacterName} activates {skill.DisplayName}.");
		_isWaitingForPlayInput = false;
		_hasQueuedPlayPhaseAction = true;
		RequestStateSync();
		return true;
	}

	private List<PlayerCharacter> ResolveSkillActivationTargets(NetworkPlayCommand command)
	{
		IEnumerable<int> targetPeerIds = command.TargetPeerIds is { Count: > 0 }
			? command.TargetPeerIds
			: command.TargetPeerId > 0
				? new[] { command.TargetPeerId }
				: Array.Empty<int>();

		return targetPeerIds
			.Distinct()
			.Select(peerId => _peerCharacters.TryGetValue(peerId, out PlayerCharacter? target) ? target : null)
			.OfType<PlayerCharacter>()
			.Where(target => target.IsAlive)
			.ToList();
	}

	private static bool ValidateSkillActivationShape(CharacterSkill skill, SkillActivationContext context, out string reason)
	{
		reason = string.Empty;
		if (!skill.IsActiveSkill)
		{
			reason = $"{skill.DisplayName} is not an active skill.";
			return false;
		}

		int targetCount = context.Targets.Count;
		if (targetCount < skill.MinTargetCount || targetCount > skill.MaxTargetCount)
		{
			reason = $"{skill.DisplayName} requires {skill.MinTargetCount}-{skill.MaxTargetCount} target(s).";
			return false;
		}

		if (skill.RequiresHandCardCost && string.IsNullOrWhiteSpace(context.CardInstanceId))
		{
			reason = $"{skill.DisplayName} requires a hand card cost.";
			return false;
		}

		return skill.TargetingMode switch
		{
			SkillTargetingMode.None => targetCount == 0 || Fail($"{skill.DisplayName} does not use targets.", out reason),
			SkillTargetingMode.Self => targetCount == 1 && context.Targets[0] == context.Source || Fail($"{skill.DisplayName} must target self.", out reason),
			SkillTargetingMode.OtherCharacter => targetCount == 1 && context.Targets[0] != context.Source || Fail($"{skill.DisplayName} must target another character.", out reason),
			SkillTargetingMode.AnyCharacter => targetCount == 1 || Fail($"{skill.DisplayName} must target one character.", out reason),
			SkillTargetingMode.MultipleCharacters => targetCount >= skill.MinTargetCount && targetCount <= skill.MaxTargetCount || Fail($"{skill.DisplayName} target count is invalid.", out reason),
			_ => true
		};
	}

	private static bool Fail(string message, out string reason)
	{
		reason = message;
		return false;
	}

	private void EmitCardUsed(PlayerCharacter sourceCharacter, CardInstance card, PlayerCharacter? primaryTarget)
	{
		EmitRuleEvent(RuleEventType.CardUsed, sourceCharacter, primaryTarget, card, value: 1, message: $"{sourceCharacter.CharacterName} uses {card.DisplayName}.");
	}

	private List<PlayerCharacter> ResolveTargetsForCard(PlayerCharacter sourceCharacter, CardInstance playedCard, NetworkPlayCommand command)
	{
		if (CardRules.TargetsAllAlive(playedCard.CardType))
		{
			return _peerCharacters.Values
				.Where(character => character.IsAlive)
				.OrderBy(character => character.OwnerPeerId)
				.ToList();
		}

		if (CardRules.TargetsAllOthers(playedCard.CardType))
		{
			return _peerCharacters.Values
				.Where(character => character.IsAlive && character.OwnerPeerId != sourceCharacter.OwnerPeerId)
				.OrderBy(character => character.OwnerPeerId)
				.ToList();
		}

		if (!CardRules.NeedsTarget(playedCard.CardType))
		{
			return new List<PlayerCharacter> { sourceCharacter };
		}

		List<int> rawTargetPeerIds = command.TargetPeerIds ?? new List<int>();
		if (rawTargetPeerIds.Count == 0)
		{
			rawTargetPeerIds = new List<int> { command.TargetPeerId };
		}

		List<int> requestedTargetPeerIds = rawTargetPeerIds
			.Where(peerId => peerId > 0)
			.Distinct()
			.ToList();

		if (playedCard.CardType == CardType.BorrowSword)
		{
			return ResolveBorrowSwordTargets(sourceCharacter, requestedTargetPeerIds);
		}

		int maxTargets = GetMaxTargetCount(sourceCharacter, playedCard);
		if (requestedTargetPeerIds.Count == 0 || requestedTargetPeerIds.Count > maxTargets)
		{
			return new List<PlayerCharacter>();
		}

		List<PlayerCharacter> targets = new();
		foreach (int requestedTargetPeerId in requestedTargetPeerIds)
		{
			if (!_peerCharacters.TryGetValue(requestedTargetPeerId, out PlayerCharacter? targetCharacter) || !targetCharacter.IsAlive)
			{
				return new List<PlayerCharacter>();
			}

			if (!CanUseCardOnTarget(sourceCharacter, targetCharacter, playedCard.CardType, out string reason))
			{
				GD.PushWarning(reason);
				return new List<PlayerCharacter>();
			}

			targets.Add(targetCharacter);
		}

		return targets;
	}

	private List<PlayerCharacter> ResolveBorrowSwordTargets(PlayerCharacter sourceCharacter, IReadOnlyList<int> requestedTargetPeerIds)
	{
		if (requestedTargetPeerIds.Count != 2)
		{
			return new List<PlayerCharacter>();
		}

		if (!_peerCharacters.TryGetValue(requestedTargetPeerIds[0], out PlayerCharacter? weaponHolder)
			|| !_peerCharacters.TryGetValue(requestedTargetPeerIds[1], out PlayerCharacter? slashTarget)
			|| !weaponHolder.IsAlive
			|| !slashTarget.IsAlive
			|| weaponHolder == slashTarget)
		{
			return new List<PlayerCharacter>();
		}

		if (weaponHolder.OwnerPeerId == sourceCharacter.OwnerPeerId)
		{
			GD.PushWarning("Borrow Sword must choose another character as the weapon holder.");
			return new List<PlayerCharacter>();
		}

		if (weaponHolder.EquippedWeapon is null)
		{
			GD.PushWarning($"{weaponHolder.CharacterName} has no weapon for Borrow Sword.");
			return new List<PlayerCharacter>();
		}

		if (!CanUseForcedSlashOnTarget(weaponHolder, slashTarget, out string reason))
		{
			GD.PushWarning(reason);
			return new List<PlayerCharacter>();
		}

		return new List<PlayerCharacter> { weaponHolder, slashTarget };
	}

	public int GetMaxTargetCount(CardType cardType)
	{
		if (cardType is CardType.IronChain or CardType.BorrowSword)
		{
			return 2;
		}

		return 1;
	}

	public int GetMaxTargetCount(PlayerCharacter? sourceCharacter, CardInstance? card)
	{
		if (sourceCharacter is not null
			&& card is not null
			&& CardRules.IsSlash(card.CardType)
			&& sourceCharacter.CanUseFangtianHalberd
			&& sourceCharacter.HandCardCount == 1
			&& sourceCharacter.FindHandCard(card.InstanceId) is not null)
		{
			return 3;
		}

		return card is null ? 1 : GetMaxTargetCount(card.CardType);
	}

	public bool CanUseCardOnTarget(PlayerCharacter? sourceCharacter, PlayerCharacter? targetCharacter, CardType cardType, out string reason)
	{
		reason = string.Empty;

		if (sourceCharacter is null || targetCharacter is null)
		{
			reason = "Source or target is missing.";
			return false;
		}

		if (!targetCharacter.IsAlive)
		{
			reason = "Target is not alive.";
			return false;
		}

		if (CardRules.IsSlash(cardType))
		{
			return CanUseSlashOnTarget(sourceCharacter, targetCharacter, out reason);
		}

		if (!CardRules.NeedsTarget(cardType))
		{
			reason = "This card does not use a target button.";
			return false;
		}

		if (sourceCharacter == targetCharacter && cardType is not CardType.IronChain and not CardType.BorrowSword)
		{
			reason = "This card must target another character.";
			return false;
		}

		if (cardType is CardType.Indulgence or CardType.SupplyShortage && targetCharacter.HasDelayedTrick(cardType))
		{
			reason = $"{targetCharacter.CharacterName} already has this delayed trick.";
			return false;
		}

		if (cardType == CardType.FireAttack && targetCharacter.HandCardCount <= 0)
		{
			reason = $"{targetCharacter.CharacterName} has no hand cards for Fire Attack.";
			return false;
		}

		if (CardRules.RequiresDistanceOne(cardType) && GetDistanceBetween(sourceCharacter.OwnerPeerId, targetCharacter.OwnerPeerId) > 1)
		{
			reason = $"{cardType} requires distance 1.";
			return false;
		}

		return true;
	}

	private bool TryHandleResponseCommand(int actingPeerId, NetworkPlayCommand command)
	{
		if (!HasPendingResponseWindow)
		{
			GD.PushWarning("Rejected response command because there is no pending response window.");
			return false;
		}

		if (actingPeerId != PendingResponsePeerId)
		{
			GD.PushWarning($"Rejected response command from peer {actingPeerId} because peer {PendingResponsePeerId} is expected to respond.");
			return false;
		}

		return PendingResponseKind switch
		{
			ResponseWindowKind.Dodge => command.CommandType switch
			{
				NetworkPlayCommandType.RespondDodge => _pendingRequiredResponseCardType != CardType.None
					? TryRespondWithRequiredCard(actingPeerId, command.CardInstanceId)
					: TryRespondWithDodge(actingPeerId, command.CardInstanceId),
				NetworkPlayCommandType.DeclineResponse => DeclinePendingResponse(actingPeerId),
				_ => false
			},
			ResponseWindowKind.Peach => command.CommandType switch
			{
				NetworkPlayCommandType.RespondPeach => _pendingRequiredResponseCardType != CardType.None
					? TryRespondWithRequiredCard(actingPeerId, command.CardInstanceId)
					: TryRespondWithPeach(actingPeerId, command.CardInstanceId),
				NetworkPlayCommandType.DeclineResponse => DeclinePendingResponse(actingPeerId),
				_ => false
			},
			ResponseWindowKind.Slash => command.CommandType switch
			{
				NetworkPlayCommandType.RespondSlash => TryRespondWithRequiredCard(actingPeerId, command.CardInstanceId),
				NetworkPlayCommandType.DeclineResponse => DeclinePendingResponse(actingPeerId),
				_ => false
			},
			ResponseWindowKind.Nullification => command.CommandType switch
			{
				NetworkPlayCommandType.RespondNullification => TryRespondWithNullification(actingPeerId, command.CardInstanceId),
				NetworkPlayCommandType.DeclineResponse => DeclinePendingResponse(actingPeerId),
				_ => false
			},
			_ => false
		};
	}

	private bool TryRespondWithDodge(int actingPeerId, string cardInstanceId)
	{
		if (_pendingDamageTargetingArgs is null || _pendingResponseDamageAction is null)
		{
			GD.PushWarning("A Dodge response was received without an active damage targeting context.");
			ClearResponseWindow();
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? targetCharacter))
		{
			return false;
		}

		if (targetCharacter.FindFirstHandCardOfType(CardType.Dodge) is null)
		{
			GD.PushWarning($"Peer {actingPeerId} tried to respond with Dodge but has no Dodge in hand.");
			ClearResponseWindow();
			return false;
		}

		bool consumed;
		if (!string.IsNullOrWhiteSpace(cardInstanceId))
		{
			CardInstance? responseCard = targetCharacter.FindHandCard(cardInstanceId);
			if (responseCard?.CardType != CardType.Dodge)
			{
				GD.PushWarning($"Peer {actingPeerId} failed to provide a valid Dodge response.");
				return false;
			}

			consumed = targetCharacter.TryConsumeHandCardByInstanceId(cardInstanceId, out _);
		}
		else
		{
			consumed = targetCharacter.TryConsumeFirstHandCardOfType(CardType.Dodge, out _);
		}

		if (!consumed)
		{
			GD.PushWarning($"Peer {actingPeerId} failed to provide a valid Dodge response.");
			return false;
		}

		if (_pendingDamageTargetingArgs is null)
		{
			GD.PushWarning("A Dodge response was received without an active pending damage targeting context.");
			ClearResponseWindow();
			return false;
		}

		DamageTargetingEventArgs targetingArgs = _pendingDamageTargetingArgs;
		targetingArgs.RespondWithDodge(new DodgeAction(targetCharacter));
		AddBattleLog($"{targetCharacter.CharacterName} manually responds with Dodge.");
		EmitRuleEvent(RuleEventType.ResponseUsed, targetCharacter, targetCharacter, value: 1, message: $"{targetCharacter.CharacterName} manually responds with Dodge.", responseKind: ResponseWindowKind.Dodge, actingPeerId: actingPeerId);
		ClearResponseWindow();
		return true;
	}

	private bool TryRespondWithRequiredCard(int actingPeerId, string cardInstanceId)
	{
		if (_pendingRequiredResponseCardType == CardType.None)
		{
			GD.PushWarning("A card response was received without an active required-card context.");
			ClearResponseWindow();
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? responder))
		{
			return false;
		}

		bool consumed;
		CardInstance? consumedCard = null;
		if (CardRules.IsSlash(_pendingRequiredResponseCardType) || _pendingRequiredResponseCardType == CardType.Slash)
		{
			consumed = TryConsumeSlashResponse(responder, cardInstanceId, out consumedCard);
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(cardInstanceId))
			{
				CardInstance? responseCard = responder.FindHandCard(cardInstanceId);
				if (responseCard?.CardType != _pendingRequiredResponseCardType)
				{
					GD.PushWarning($"{responder.CharacterName} failed to provide {_pendingRequiredResponseCardType}.");
					return false;
				}

				consumed = responder.TryConsumeHandCardByInstanceId(cardInstanceId, out consumedCard);
			}
			else
			{
				consumed = responder.TryConsumeFirstHandCardOfType(_pendingRequiredResponseCardType, out consumedCard);
			}
		}

		if (!consumed)
		{
			GD.PushWarning($"{responder.CharacterName} failed to provide {_pendingRequiredResponseCardType}.");
			return false;
		}

		Action<PlayerCharacter>? onSuccess = _pendingRequiredResponseSuccess;
		ResponseWindowKind responseKind = PendingResponseKind;
		AddBattleLog($"{responder.CharacterName} responds with {_pendingRequiredResponseCardType}.");
		EmitRuleEvent(RuleEventType.ResponseUsed, responder, responder, value: 1, message: $"{responder.CharacterName} responds with {_pendingRequiredResponseCardType}.", responseKind: responseKind, actingPeerId: actingPeerId);
		ClearResponseWindow();
		_currentResolvedResponseCard = consumedCard?.Clone();
		onSuccess?.Invoke(responder);
		_currentResolvedResponseCard = null;
		return true;
	}

	private static bool TryConsumeSlashResponse(PlayerCharacter responder, string cardInstanceId, out CardInstance? consumedResponseCard)
	{
		consumedResponseCard = null;
		if (!string.IsNullOrWhiteSpace(cardInstanceId))
		{
			CardInstance? responseCard = responder.FindHandCard(cardInstanceId);
			return responseCard is not null
				&& CardRules.IsSlash(responseCard.CardType)
				&& responder.TryConsumeHandCardByInstanceId(cardInstanceId, out consumedResponseCard);
		}

		CardInstance? slashCard = responder.HandCards.FirstOrDefault(card => CardRules.IsSlash(card.CardType));
		if (slashCard is not null && responder.TryConsumeHandCardByInstanceId(slashCard.InstanceId, out consumedResponseCard))
		{
			return true;
		}

		if (!responder.CanUseSerpentSpear || responder.HandCardCount < 2 || GameManager.Instance is not { } gameManager)
		{
			return false;
		}

		List<CardInstance> spearCosts = responder.HandCards.Take(2).ToList();
		int consumedCount = 0;
		foreach (CardInstance costCard in spearCosts)
		{
			if (!responder.TryConsumeHandCardByInstanceId(costCard.InstanceId, out CardInstance? consumedCard)
				|| consumedCard is null)
			{
				continue;
			}

			consumedCount++;
			gameManager.DiscardCardFromEffect(consumedCard);
			gameManager.EmitCardsLost(responder, responder, consumedCard, value: 1, message: $"{responder.CharacterName} loses {consumedCard.DisplayName} for Serpent Spear.");
			gameManager.EmitRuleEvent(RuleEventType.CardsDiscarded, responder, responder, consumedCard, value: 1, message: $"{responder.CharacterName} discards {consumedCard.DisplayName} for Serpent Spear.");
		}

		if (consumedCount >= 2)
		{
			gameManager.AddBattleLog($"{responder.CharacterName}'s Serpent Spear treats two hand cards as Slash.");
			return true;
		}

		return false;
	}

	private bool TryRespondWithPeach(int actingPeerId, string cardInstanceId)
	{
		if (_pendingDyingCharacter is null || !_pendingDyingCharacter.IsDying)
		{
			GD.PushWarning("A Peach response was received without an active dying context.");
			ClearResponseWindow();
			return false;
		}

		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? rescuer))
		{
			return false;
		}

		bool canUseWineForSelf = rescuer.OwnerPeerId == _pendingDyingCharacter.OwnerPeerId;
		bool consumed;
		CardType consumedCardType = CardType.Peach;
		if (!string.IsNullOrWhiteSpace(cardInstanceId))
		{
			CardInstance? responseCard = rescuer.FindHandCard(cardInstanceId);
			if (responseCard is null
				|| (responseCard.CardType != CardType.Peach && !(canUseWineForSelf && responseCard.CardType == CardType.Wine)))
			{
				GD.PushWarning($"Peer {actingPeerId} failed to provide a valid dying response.");
				return false;
			}

			consumedCardType = responseCard.CardType;
			consumed = rescuer.TryConsumeHandCardByInstanceId(cardInstanceId, out _);
		}
		else
		{
			consumed = rescuer.TryConsumeFirstHandCardOfType(CardType.Peach, out _);
			if (!consumed && canUseWineForSelf)
			{
				consumed = rescuer.TryConsumeFirstHandCardOfType(CardType.Wine, out _);
				consumedCardType = CardType.Wine;
			}
		}

		if (!consumed)
		{
			GD.PushWarning($"Peer {actingPeerId} failed to provide a valid dying response.");
			return false;
		}

		PlayerCharacter targetCharacter = _pendingDyingCharacter;
		targetCharacter.Heal(1);

		string responseName = consumedCardType == CardType.Wine ? "Wine" : "Peach";
		string logMessage = rescuer.OwnerPeerId == targetCharacter.OwnerPeerId
			? $"{targetCharacter.CharacterName} uses {responseName} to save themselves."
			: $"{rescuer.CharacterName} uses {responseName} to save {targetCharacter.CharacterName}.";
		AddBattleLog(logMessage);
		EmitRuleEvent(RuleEventType.ResponseUsed, rescuer, targetCharacter, value: 1, message: logMessage, responseKind: ResponseWindowKind.Peach, actingPeerId: actingPeerId);

		if (!targetCharacter.IsDying)
		{
			FinishPendingDyingResolution(true);
			return true;
		}

		ClearResponseWindow();
		ResetPendingDyingResponseOrder(rescuer.OwnerPeerId);
		AdvancePendingDyingResponse();
		return true;
	}

	private bool TryRespondWithNullification(int actingPeerId, string cardInstanceId)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? responder))
		{
			return false;
		}

		bool consumed;
		if (!string.IsNullOrWhiteSpace(cardInstanceId))
		{
			CardInstance? responseCard = responder.FindHandCard(cardInstanceId);
			if (responseCard?.CardType != CardType.Nullification)
			{
				GD.PushWarning($"{responder.CharacterName} failed to provide Nullification.");
				return false;
			}

			consumed = responder.TryConsumeHandCardByInstanceId(cardInstanceId, out _);
		}
		else
		{
			consumed = responder.TryConsumeFirstHandCardOfType(CardType.Nullification, out _);
		}

		if (!consumed)
		{
			GD.PushWarning($"{responder.CharacterName} failed to provide Nullification.");
			return false;
		}

		Action<PlayerCharacter>? onNullified = _pendingNullificationSuccess;
		Action? onPass = _pendingNullificationDecline;
		string subjectName = string.IsNullOrWhiteSpace(_pendingNullificationSubjectName) ? "the effect" : _pendingNullificationSubjectName;
		AddBattleLog($"{responder.CharacterName} responds with Nullification.");
		EmitRuleEvent(RuleEventType.ResponseUsed, responder, responder, value: 1, message: $"{responder.CharacterName} responds with Nullification.", responseKind: ResponseWindowKind.Nullification, actingPeerId: actingPeerId);
		ClearResponseWindow();
		BeginNullificationWindow(
			responder,
			new CardInstance
			{
				CardType = CardType.Nullification,
				DisplayName = "Nullification",
				Description = $"Counter-nullification window for {subjectName}."
			},
			counterResponder =>
			{
				AddBattleLog($"{counterResponder.CharacterName}'s Nullification cancels {responder.CharacterName}'s Nullification.");
				onPass?.Invoke();
			},
			() => onNullified?.Invoke(responder));
		return true;
	}

	private bool DeclinePendingResponse(int actingPeerId)
	{
		if (!_peerCharacters.TryGetValue(actingPeerId, out PlayerCharacter? targetCharacter))
		{
			return false;
		}

		if (PendingResponseKind == ResponseWindowKind.Nullification)
		{
			return DeclinePendingNullification(targetCharacter);
		}

		AddBattleLog($"{targetCharacter.CharacterName} declines to respond.");
		EmitRuleEvent(RuleEventType.ResponseDeclined, targetCharacter, targetCharacter, message: $"{targetCharacter.CharacterName} declines to respond.", responseKind: PendingResponseKind, actingPeerId: actingPeerId);
		bool continueDyingResolution = PendingResponseKind == ResponseWindowKind.Peach && _pendingDyingCharacter is not null;
		Action<PlayerCharacter>? onDecline = _pendingRequiredResponseDecline;
		ClearResponseWindow();

		if (continueDyingResolution)
		{
			AdvancePendingDyingResponse();
		}

		onDecline?.Invoke(targetCharacter);

		return true;
	}

	private bool DeclinePendingNullification(PlayerCharacter targetCharacter)
	{
		AddBattleLog($"{targetCharacter.CharacterName} declines to respond.");
		EmitRuleEvent(RuleEventType.ResponseDeclined, targetCharacter, targetCharacter, message: $"{targetCharacter.CharacterName} declines to respond.", responseKind: PendingResponseKind, actingPeerId: targetCharacter.OwnerPeerId);

		Action<PlayerCharacter>? onSuccess = _pendingNullificationSuccess;
		Action? onDecline = _pendingNullificationDecline;
		string subjectName = _pendingNullificationSubjectName;
		List<int> responseOrder = new(_pendingNullificationResponseOrder);
		int responseIndex = _pendingNullificationResponseIndex;

		ClearResponseWindow();

		_pendingNullificationSuccess = onSuccess;
		_pendingNullificationDecline = onDecline;
		_pendingNullificationSubjectName = subjectName;
		_pendingNullificationResponseOrder.Clear();
		_pendingNullificationResponseOrder.AddRange(responseOrder);
		_pendingNullificationResponseIndex = responseIndex;

		ContinueNullificationWindow();
		return true;
	}

	private void InitializeDecksForMatch()
	{
		_drawPile.Clear();
		_discardPile.Clear();
		_nextCardInstanceSequence = 1;

		List<CardInstance> startingDeck = BuildStartingDeck(Math.Max(_turnOrder.Count, _peerCharacters.Count));
		ShuffleCards(startingDeck);
		_drawPile.AddRange(startingDeck);
	}

	private void ResetCharactersForMatch()
	{
		foreach (PlayerCharacter character in _peerCharacters.Values)
		{
			character.InitializeStats(character.MaxHealth, character.MaxHealth, 0);
		}
	}

	private void DealOpeningHands()
	{
		foreach (int peerId in _turnOrder)
		{
			if (_peerCharacters.TryGetValue(peerId, out PlayerCharacter? character))
			{
				int dealt = DrawCardsToCharacter(character, Math.Max(0, OpeningHandSize));
				AddBattleLog($"{character.CharacterName} receives {dealt} opening card(s).");
				EmitCardsGained(character, character, value: dealt, message: $"{character.CharacterName} receives {dealt} opening card(s).");
			}
		}
	}

	private int DrawCardsToCharacter(PlayerCharacter character, int count)
	{
		if (character is null || count <= 0)
		{
			return 0;
		}

		List<CardInstance> drawnCards = new();
		for (int i = 0; i < count; i++)
		{
			if (!TryDrawCard(out CardInstance? drawnCard) || drawnCard is null)
			{
				break;
			}

			drawnCards.Add(drawnCard);
		}

		character.AddCards(drawnCards);
		return drawnCards.Count;
	}

	private bool TryDrawCard(out CardInstance? drawnCard)
	{
		drawnCard = null;

		if (_drawPile.Count == 0)
		{
			TryReshuffleDiscardIntoDrawPile();
		}

		if (_drawPile.Count == 0)
		{
			return false;
		}

		int topIndex = _drawPile.Count - 1;
		drawnCard = _drawPile[topIndex];
		_drawPile.RemoveAt(topIndex);
		return true;
	}

	private void TryReshuffleDiscardIntoDrawPile()
	{
		if (_discardPile.Count == 0)
		{
			return;
		}

		List<CardInstance> reshuffledCards = _discardPile.Select(card => card.Clone()).ToList();
		_discardPile.Clear();
		ShuffleCards(reshuffledCards);
		_drawPile.AddRange(reshuffledCards);
		AddBattleLog("The discard pile is reshuffled into the draw pile.");
	}

	private void DiscardCardToPile(CardInstance? card)
	{
		if (card is null)
		{
			return;
		}

		_discardPile.Add(card.Clone());
		RequestStateSync();
	}

	public void DiscardCardFromEffect(CardInstance? card)
	{
		DiscardCardToPile(card);
	}

	public void ResolveEquipmentLostEffects(PlayerCharacter owner, CardInstance? equipmentCard)
	{
		if (owner is null || equipmentCard is null || owner.IsDefeated)
		{
			return;
		}

		if (equipmentCard.EquipmentEffect != EquipmentEffectType.SilverLion)
		{
			return;
		}

		if (owner.CurrentHealth >= owner.MaxHealth)
		{
			AddBattleLog($"{owner.CharacterName}'s Silver Lion leaves equipment area, but they are already at full health.");
			return;
		}

		owner.Heal(1);
		AddBattleLog($"{owner.CharacterName}'s Silver Lion leaves equipment area and heals 1 HP.");
		EmitRuleEvent(RuleEventType.Healed, owner, owner, equipmentCard, value: 1, message: $"{owner.CharacterName}'s Silver Lion heals 1 HP.");
		RequestStateSync();
	}

	public void NotifyEquipmentLost(PlayerCharacter owner, Node? cause, CardInstance? equipmentCard)
	{
		if (owner is null || equipmentCard is null || !CardRules.IsEquipment(equipmentCard.CardType))
		{
			return;
		}

		EmitRuleEvent(RuleEventType.EquipmentLost, cause ?? owner, owner, equipmentCard, message: $"{owner.CharacterName} loses {equipmentCard.DisplayName}.");
		ResolveEquipmentLostEffects(owner, equipmentCard);
	}

	public bool TryDiscardOneCardFromTarget(PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, string logMessage)
	{
		if (sourceCharacter is null || targetCharacter is null)
		{
			return false;
		}

		if (!targetCharacter.TryRemoveOneCardOrEquipment(out CardInstance? removedCard) || removedCard is null)
		{
			AddBattleLog($"{targetCharacter.CharacterName} has no card to discard.");
			return false;
		}

		DiscardCardToPile(removedCard);
		AddBattleLog(string.IsNullOrWhiteSpace(logMessage) ? $"{sourceCharacter.CharacterName} discards one card from {targetCharacter.CharacterName}." : logMessage);
		EmitCardsLost(targetCharacter, sourceCharacter, removedCard, value: 1, message: $"{targetCharacter.CharacterName} loses {removedCard.DisplayName}.");
		if (CardRules.IsEquipment(removedCard.CardType))
		{
			NotifyEquipmentLost(targetCharacter, sourceCharacter, removedCard);
		}

		EmitRuleEvent(RuleEventType.CardsDiscarded, sourceCharacter, targetCharacter, removedCard, value: 1, message: logMessage);
		return true;
	}

	public bool TryStealOneCard(PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, string logMessage)
	{
		if (sourceCharacter is null || targetCharacter is null)
		{
			return false;
		}

		if (!targetCharacter.TryRemoveOneCardOrEquipment(out CardInstance? stolenCard) || stolenCard is null)
		{
			AddBattleLog($"{targetCharacter.CharacterName} has no card to steal.");
			return false;
		}

		sourceCharacter.AddCard(stolenCard);
		AddBattleLog(string.IsNullOrWhiteSpace(logMessage) ? $"{sourceCharacter.CharacterName} steals one card from {targetCharacter.CharacterName}." : logMessage);
		EmitCardsLost(targetCharacter, sourceCharacter, stolenCard, value: 1, message: $"{targetCharacter.CharacterName} loses {stolenCard.DisplayName}.");
		if (CardRules.IsEquipment(stolenCard.CardType))
		{
			NotifyEquipmentLost(targetCharacter, sourceCharacter, stolenCard);
		}

		EmitCardsGained(sourceCharacter, sourceCharacter, stolenCard, value: 1, message: $"{sourceCharacter.CharacterName} gains {stolenCard.DisplayName}.");
		EmitCardMoved(sourceCharacter, targetCharacter, sourceCharacter, stolenCard, $"{stolenCard.DisplayName} moves from {targetCharacter.CharacterName} to {sourceCharacter.CharacterName}.");
		return true;
	}

	public bool TryStealEquipmentBySlot(PlayerCharacter sourceCharacter, PlayerCharacter targetCharacter, EquipmentSlotType slot, string logMessage)
	{
		if (sourceCharacter is null || targetCharacter is null || slot == EquipmentSlotType.None)
		{
			return false;
		}

		if (!targetCharacter.TryRemoveEquipmentBySlot(slot, out CardInstance? stolenCard) || stolenCard is null)
		{
			AddBattleLog($"{targetCharacter.CharacterName} has no {slot} to steal.");
			return false;
		}

		sourceCharacter.AddCard(stolenCard);
		AddBattleLog(string.IsNullOrWhiteSpace(logMessage) ? $"{sourceCharacter.CharacterName} steals {targetCharacter.CharacterName}'s {stolenCard.DisplayName}." : logMessage);
		EmitCardsLost(targetCharacter, sourceCharacter, stolenCard, value: 1, message: $"{targetCharacter.CharacterName} loses {stolenCard.DisplayName}.");
		NotifyEquipmentLost(targetCharacter, sourceCharacter, stolenCard);
		EmitCardsGained(sourceCharacter, sourceCharacter, stolenCard, value: 1, message: $"{sourceCharacter.CharacterName} gains {stolenCard.DisplayName}.");
		EmitCardMoved(sourceCharacter, targetCharacter, sourceCharacter, stolenCard, $"{stolenCard.DisplayName} moves from {targetCharacter.CharacterName} to {sourceCharacter.CharacterName}.");
		return true;
	}

	private IEnumerable<PlayerCharacter> GetDyingResponseOrder(int startPeerId)
	{
		if (_turnOrder.Count == 0)
		{
			yield break;
		}

		int startIndex = _turnOrder.IndexOf(startPeerId);
		if (startIndex < 0)
		{
			startIndex = Math.Max(0, _turnOrder.IndexOf(CurrentTurnPeerId));
		}

		for (int offset = 0; offset < _turnOrder.Count; offset++)
		{
			int index = (startIndex - offset + _turnOrder.Count) % _turnOrder.Count;
			int peerId = _turnOrder[index];
			if (!_peerCharacters.TryGetValue(peerId, out PlayerCharacter? rescuer))
			{
				continue;
			}

			if (rescuer.IsDefeated)
			{
				continue;
			}

			yield return rescuer;
		}
	}

	private List<int> GetAliveTurnOrder()
	{
		return _turnOrder
			.Where(peerId => _peerCharacters.TryGetValue(peerId, out PlayerCharacter? character) && character.IsAlive)
			.ToList();
	}

	private int GetNextAlivePeerIdAfter(int currentPeerId)
	{
		if (_turnOrder.Count == 0)
		{
			return 0;
		}

		int startIndex = _turnOrder.IndexOf(currentPeerId);
		if (startIndex < 0)
		{
			startIndex = 0;
		}

		for (int step = 1; step <= _turnOrder.Count; step++)
		{
			int index = (startIndex + step) % _turnOrder.Count;
			int peerId = _turnOrder[index];
			if (_peerCharacters.TryGetValue(peerId, out PlayerCharacter? character) && character.IsAlive)
			{
				return peerId;
			}
		}

		return currentPeerId;
	}

	private List<CardInstance> BuildStartingDeck(int playerCount)
	{
		List<CardInstance> fixedDeck = StandardDeckDefinition.CreateDeck()
			.Select(CreateDeckCard)
			.ToList();
		if (fixedDeck.Count > 0)
		{
			return fixedDeck;
		}

		int safePlayerCount = Math.Max(2, playerCount);
		List<CardInstance> deck = new();
		AddCardsToDeck(deck, CardType.Slash, safePlayerCount * 10);
		AddCardsToDeck(deck, CardType.FireSlash, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.ThunderSlash, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.Dodge, safePlayerCount * 8);
		AddCardsToDeck(deck, CardType.Peach, safePlayerCount * 5);
		AddCardsToDeck(deck, CardType.Wine, safePlayerCount * 3);
		AddCardsToDeck(deck, CardType.Dismantle, safePlayerCount * 3);
		AddCardsToDeck(deck, CardType.Snatch, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.Duel, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.ExNihilo, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.Nullification, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.Barbarians, safePlayerCount);
		AddCardsToDeck(deck, CardType.ArrowBarrage, safePlayerCount);
		AddCardsToDeck(deck, CardType.PeachGarden, safePlayerCount);
		AddCardsToDeck(deck, CardType.Harvest, safePlayerCount);
		AddCardsToDeck(deck, CardType.Indulgence, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.SupplyShortage, safePlayerCount);
		AddCardsToDeck(deck, CardType.Lightning, safePlayerCount);
		AddCardsToDeck(deck, CardType.IronChain, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.FireAttack, safePlayerCount);
		AddCardsToDeck(deck, CardType.BorrowSword, safePlayerCount);
		AddCardsToDeck(deck, CardType.Weapon, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.Armor, safePlayerCount * 2);
		AddCardsToDeck(deck, CardType.OffensiveHorse, safePlayerCount);
		AddCardsToDeck(deck, CardType.DefensiveHorse, safePlayerCount);
		AddCardsToDeck(deck, CardType.Treasure, safePlayerCount);

		return deck;
	}

	private CardInstance CreateDeckCard(DeckCardDefinition definition)
	{
		string instanceId = $"deck-{_nextCardInstanceSequence++:D5}";
		CardInstance card = CreateDeckCardCore(instanceId, definition.CardType);
		ApplyDeckCardDefinition(card, definition);
		return card;
	}

	private void AddCardsToDeck(List<CardInstance> deck, CardType cardType, int count)
	{
		for (int i = 0; i < count; i++)
		{
			deck.Add(CreateDeckCard(cardType));
		}
	}

	private CardInstance CreateDeckCard(CardType cardType)
	{
		string instanceId = $"deck-{_nextCardInstanceSequence++:D5}";
		CardInstance card = CreateDeckCardCore(instanceId, cardType);
		AssignCardIdentity(card);
		return card;
	}

	private CardInstance CreateDeckCardCore(string instanceId, CardType cardType)
	{
		return cardType switch
		{
			CardType.Dodge => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Dodge,
				DisplayName = "Dodge",
				Description = "Respond to one attack.",
				DamageValue = 0
			},
			CardType.Peach => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Peach,
				DisplayName = "Peach",
				Description = "Heal yourself for 1.",
				DamageValue = 1
			},
			CardType.Wine => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Wine,
				DisplayName = "Wine",
				Description = "Boost your next Slash by 1 damage, or recover while dying.",
				DamageValue = 0
			},
			CardType.FireSlash => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.FireSlash,
				DisplayName = "Fire Slash",
				Description = "Deal 1 fire damage to one target.",
				DamageValue = 1
			},
			CardType.ThunderSlash => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.ThunderSlash,
				DisplayName = "Thunder Slash",
				Description = "Deal 1 thunder damage to one target.",
				DamageValue = 1
			},
			CardType.Dismantle => CreateTrickCard(instanceId, cardType, "Dismantle", "Discard one card from a target."),
			CardType.Snatch => CreateTrickCard(instanceId, cardType, "Snatch", "Take one card from a target."),
			CardType.Duel => CreateTrickCard(instanceId, cardType, "Duel", "Challenge a target. The target must respond with Slash or take 1 damage."),
			CardType.ExNihilo => CreateTrickCard(instanceId, cardType, "Ex Nihilo", "Draw 2 cards."),
			CardType.Nullification => CreateTrickCard(instanceId, cardType, "Nullification", "Cancel one trick card in a response window."),
			CardType.Barbarians => CreateTrickCard(instanceId, cardType, "Barbarians", "Every other alive character must respond with Slash or take 1 damage."),
			CardType.ArrowBarrage => CreateTrickCard(instanceId, cardType, "Arrow Barrage", "Every other alive character must respond with Dodge or take 1 damage."),
			CardType.PeachGarden => CreateTrickCard(instanceId, cardType, "Peach Garden", "Heal every alive character for 1."),
			CardType.Harvest => CreateTrickCard(instanceId, cardType, "Harvest", "Reveal a shared card pool. Alive characters take one card in turn order."),
			CardType.Indulgence => CreateTrickCard(instanceId, cardType, "Indulgence", "Delayed trick. Resolve at turn start to skip PlayPhase.", true),
			CardType.SupplyShortage => CreateTrickCard(instanceId, cardType, "Supply Shortage", "Delayed trick. Resolve at turn start to skip DrawPhase.", true),
			CardType.Lightning => CreateTrickCard(instanceId, cardType, "Lightning", "Delayed trick. May strike for 3 thunder damage or pass onward.", true),
			CardType.IronChain => CreateTrickCard(instanceId, cardType, "Iron Chain", "Toggle chained state on one target. Chained characters share elemental damage."),
			CardType.FireAttack => CreateTrickCard(instanceId, cardType, "Fire Attack", "Deal 1 fire damage to a target with at least one hand card."),
			CardType.BorrowSword => CreateTrickCard(instanceId, cardType, "Borrow Sword", "Choose a weapon holder and a Slash target. If they decline, take their weapon."),
			CardType.Weapon => CreateWeaponCard(instanceId),
			CardType.Armor => CreateArmorCard(instanceId),
			CardType.OffensiveHorse => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.OffensiveHorse,
				DisplayName = "Offensive Horse",
				Description = "Reduce attack distance by 1.",
				EquipmentSlot = EquipmentSlotType.OffensiveHorse,
				AttackDistanceModifier = 1
			},
			CardType.DefensiveHorse => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.DefensiveHorse,
				DisplayName = "Defensive Horse",
				Description = "Increase distance from attackers by 1.",
				EquipmentSlot = EquipmentSlotType.DefensiveHorse,
				DefenseDistanceModifier = 1
			},
			CardType.Treasure => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Treasure,
				DisplayName = "Imperial Seal",
				Description = "Treasure. Occupies the treasure slot.",
				EquipmentSlot = EquipmentSlotType.Treasure,
				EquipmentEffect = EquipmentEffectType.ImperialSeal
			},
			_ => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Slash,
				DisplayName = "Slash",
				Description = "Deal 1 damage to one target.",
				DamageValue = 1
			}
		};
	}

	private static void ApplyDeckCardDefinition(CardInstance card, DeckCardDefinition definition)
	{
		card.Suit = definition.Suit;
		card.Rank = Math.Clamp(definition.Rank, 0, 13);
		if (!string.IsNullOrWhiteSpace(definition.DisplayName))
		{
			card.DisplayName = definition.DisplayName;
		}

		if (!string.IsNullOrWhiteSpace(definition.Description))
		{
			card.Description = definition.Description;
		}

		if (definition.EquipmentSlot != EquipmentSlotType.None)
		{
			card.EquipmentSlot = definition.EquipmentSlot;
			card.EquipmentEffect = definition.EquipmentEffect;
			card.AttackRangeModifier = definition.AttackRangeModifier;
			card.DamageReductionValue = definition.DamageReductionValue;
			card.AttackDistanceModifier = definition.AttackDistanceModifier;
			card.DefenseDistanceModifier = definition.DefenseDistanceModifier;
		}

		if (definition.DamageValue >= 0)
		{
			card.DamageValue = definition.DamageValue;
		}

		if (definition.IsDelayedTrick)
		{
			card.IsDelayedTrick = true;
		}
	}

	private void AssignCardIdentity(CardInstance card)
	{
		if (card is null)
		{
			return;
		}

		card.Suit = _random.Next(4) switch
		{
			0 => CardSuit.Spade,
			1 => CardSuit.Heart,
			2 => CardSuit.Club,
			_ => CardSuit.Diamond
		};
		card.Rank = _random.Next(1, 14);
	}

	private static string FormatCardIdentity(CardInstance card)
	{
		if (card is null)
		{
			return "Unknown Card";
		}

		string rank = card.Rank switch
		{
			1 => "A",
			11 => "J",
			12 => "Q",
			13 => "K",
			<= 0 => "?",
			_ => card.Rank.ToString()
		};
		return $"{card.DisplayName} [{card.Suit} {rank}]";
	}

	private CardInstance CreateWeaponCard(string instanceId)
	{
		int variant = _nextCardInstanceSequence % 11;
		return variant switch
		{
			0 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Crossbow",
				Description = "Weapon. You may use Slash without the once-per-turn limit.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.Crossbow
			},
			1 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Qinggang Sword",
				Description = "Weapon. Your damage ignores target armor.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.QinggangSword,
				AttackRangeModifier = 1
			},
			2 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Fangtian Halberd",
				Description = "Weapon. If your last hand card is Slash, it may target up to 3 characters.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.FangtianHalberd,
				AttackRangeModifier = 2
			},
			3 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Stone Axe",
				Description = "Weapon. If your Slash is dodged, discard 2 hand cards to force the damage through.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.StoneAxe,
				AttackRangeModifier = 2
			},
			4 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Kylin Bow",
				Description = "Weapon. When your Slash deals damage, discard one horse from the target.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.KylinBow,
				AttackRangeModifier = 4
			},
			5 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Green Dragon Blade",
				Description = "Weapon. If your Slash is dodged, play another Slash to the same target.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.GreenDragonBlade,
				AttackRangeModifier = 2
			},
			6 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Double Swords",
				Description = "Weapon. When Slash targets a different-gender character, they discard 1 hand card or you draw 1.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.DoubleSwords,
				AttackRangeModifier = 1
			},
			7 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Serpent Spear",
				Description = "Weapon. Treat two hand cards as Slash when a Slash response is required.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.SerpentSpear,
				AttackRangeModifier = 2
			},
			8 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Ice Sword",
				Description = "Weapon. When Slash would deal damage, prevent it to discard up to 2 cards from the target.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.IceSword,
				AttackRangeModifier = 2
			},
			9 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Guding Blade",
				Description = "Weapon. Slash damage +1 if the target has no hand cards.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				EquipmentEffect = EquipmentEffectType.GudingBlade,
				AttackRangeModifier = 1
			},
			_ => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Weapon,
				DisplayName = "Training Blade",
				Description = "Weapon. Attack range +1.",
				EquipmentSlot = EquipmentSlotType.Weapon,
				AttackRangeModifier = 1
			}
		};
	}

	private CardInstance CreateArmorCard(string instanceId)
	{
		int variant = _nextCardInstanceSequence % 5;
		return variant switch
		{
			0 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Armor,
				DisplayName = "Eight Diagram",
				Description = "Armor. Has a chance to provide Dodge when attacked.",
				EquipmentSlot = EquipmentSlotType.Armor,
				EquipmentEffect = EquipmentEffectType.EightDiagram
			},
			1 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Armor,
				DisplayName = "Vine Armor",
				Description = "Armor. Reduces physical damage but increases fire damage.",
				EquipmentSlot = EquipmentSlotType.Armor,
				EquipmentEffect = EquipmentEffectType.VineArmor,
				DamageValue = 0
			},
			2 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Armor,
				DisplayName = "Renwang Shield",
				Description = "Armor. Blocks normal Slash unless armor is ignored.",
				EquipmentSlot = EquipmentSlotType.Armor,
				EquipmentEffect = EquipmentEffectType.RenwangShield
			},
			3 => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Armor,
				DisplayName = "Silver Lion",
				Description = "Armor. Damage greater than 1 is reduced to 1. Heal 1 when lost from equipment area.",
				EquipmentSlot = EquipmentSlotType.Armor,
				EquipmentEffect = EquipmentEffectType.SilverLion
			},
			_ => new CardInstance
			{
				InstanceId = instanceId,
				CardType = CardType.Armor,
				DisplayName = "Training Armor",
				Description = "Armor. Reduce incoming physical damage by 1.",
				EquipmentSlot = EquipmentSlotType.Armor,
				DamageValue = 0,
				DamageReductionValue = 1
			}
		};
	}

	private static CardInstance CreateTrickCard(string instanceId, CardType cardType, string displayName, string description, bool delayed = false)
	{
		return new CardInstance
		{
			InstanceId = instanceId,
			CardType = cardType,
			DisplayName = displayName,
			Description = description,
			DamageValue = 0,
			IsDelayedTrick = delayed
		};
	}

	private void ShuffleCards(List<CardInstance> cards)
	{
		for (int i = cards.Count - 1; i > 0; i--)
		{
			int swapIndex = _random.Next(i + 1);
			(cards[i], cards[swapIndex]) = (cards[swapIndex], cards[i]);
		}
	}

	private void SetPublicPileCounts(int drawPileCount, int discardPileCount)
	{
		_drawPile.Clear();
		_discardPile.Clear();

		for (int i = 0; i < Math.Max(0, drawPileCount); i++)
		{
			_drawPile.Add(new CardInstance());
		}

		for (int i = 0; i < Math.Max(0, discardPileCount); i++)
		{
			_discardPile.Add(new CardInstance());
		}
	}

	private static NetworkHandCardState ToNetworkHandCardState(CardInstance card)
	{
		return new NetworkHandCardState
		{
			InstanceId = card.InstanceId,
			CardType = card.CardType.ToString(),
			DisplayName = card.DisplayName,
			Description = card.Description,
			Suit = card.Suit.ToString(),
			Rank = card.Rank,
			DamageValue = card.DamageValue,
			EquipmentSlot = card.EquipmentSlot.ToString(),
			EquipmentEffect = card.EquipmentEffect.ToString(),
			AttackRangeModifier = card.AttackRangeModifier,
			DamageReductionValue = card.DamageReductionValue,
			AttackDistanceModifier = card.AttackDistanceModifier,
			DefenseDistanceModifier = card.DefenseDistanceModifier,
			IsDelayedTrick = card.IsDelayedTrick
		};
	}

	private List<NetworkHandCardState> BuildHandCardSelectionSnapshot(int viewerPeerId)
	{
		bool revealToViewer = viewerPeerId <= 0 || viewerPeerId == PendingHandCardSelectionPeerId;
		return _handCardSelectionPool
			.Select(card => ToNetworkHandCardState(revealToViewer ? card : CreateHiddenHandSelectionCard(card)))
			.ToList();
	}

	private static CardInstance CreateHiddenHandSelectionCard(CardInstance card)
	{
		return new CardInstance
		{
			InstanceId = card.InstanceId,
			CardType = CardType.None,
			DisplayName = "Hand Card",
			Description = "A hidden selectable hand card.",
			Suit = CardSuit.None,
			Rank = 0
		};
	}

	private static CardInstance FromNetworkHandCardState(NetworkHandCardState state)
	{
		return new CardInstance
		{
			InstanceId = state.InstanceId ?? string.Empty,
			CardType = Enum.TryParse(state.CardType, true, out CardType cardType) ? cardType : CardType.None,
			DisplayName = state.DisplayName ?? string.Empty,
			Description = state.Description ?? string.Empty,
			Suit = Enum.TryParse(state.Suit, true, out CardSuit suit) ? suit : CardSuit.None,
			Rank = Math.Clamp(state.Rank, 0, 13),
			DamageValue = Math.Max(0, state.DamageValue),
			EquipmentSlot = Enum.TryParse(state.EquipmentSlot, true, out EquipmentSlotType slot) ? slot : EquipmentSlotType.None,
			EquipmentEffect = Enum.TryParse(state.EquipmentEffect, true, out EquipmentEffectType effect) ? effect : EquipmentEffectType.None,
			AttackRangeModifier = state.AttackRangeModifier,
			DamageReductionValue = state.DamageReductionValue,
			AttackDistanceModifier = state.AttackDistanceModifier,
			DefenseDistanceModifier = state.DefenseDistanceModifier,
			IsDelayedTrick = state.IsDelayedTrick
		};
	}

	private bool EndPlayPhaseAndReturnTrue()
	{
		EndPlayPhase();
		return true;
	}

	private bool RejectUnsupportedCommand(NetworkPlayCommandType commandType)
	{
		GD.PushWarning($"Unsupported network play command type: {commandType}.");
		return false;
	}
}
