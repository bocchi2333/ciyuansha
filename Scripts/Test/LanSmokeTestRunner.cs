using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.Test;

/// <summary>
/// Headless LAN smoke test runner that hosts, joins, starts a match, and exercises a Slash -> Dodge flow.
/// </summary>
public partial class LanSmokeTestRunner : Node
{
    private const string SuccessMarker = "SMOKE_TEST_SUCCESS";
    private const double HostSuccessGraceSeconds = 2d;
    private const string HostRole = "host";
    private const string ClientRole = "client";
    private const string ScenarioDodge = "dodge";
    private const string ScenarioPeachSave = "peach_save";
    private const string ScenarioPeachRescue = "peach_rescue";
    private const string ScenarioDeathNoSave = "death_no_save";
    private const string ScenarioSlashLimit = "slash_limit";
    private const string ScenarioWeaponRange = "weapon_range";
    private const string ScenarioActiveSkill = "active_skill";
    private const string ScenarioFangtian = "fangtian";
    private const string ScenarioStoneAxe = "stone_axe";
    private const string ScenarioKylinBow = "kylin_bow";
    private const string ScenarioGreenDragon = "green_dragon";
    private const string ScenarioDoubleSwords = "double_swords";
    private const string ScenarioSerpentSpear = "serpent_spear";
    private const string ScenarioIceSword = "ice_sword";
    private const string ScenarioRenwangRed = "renwang_red";
    private const string ScenarioRenwangBlack = "renwang_black";
    private const string ScenarioGudingBlade = "guding_blade";
    private const string ScenarioSilverLion = "silver_lion";
    private const string ScenarioBorrowSwordTakeWeapon = "borrow_sword_take_weapon";
    private const string ScenarioSilverLionLost = "silver_lion_lost";
    private const string ScenarioEightDiagramArrows = "eight_diagram_arrows";
    private const string ScenarioVineSlash = "vine_slash";
    private const string ScenarioVineFireSlash = "vine_fire_slash";
    private const string ScenarioQinggangVineSlash = "qinggang_vine_slash";
    private const string ScenarioBorrowSwordFireVine = "borrow_sword_fire_vine";
    private const string ScenarioChainFire = "chain_fire";
    private const string ScenarioIndulgenceFail = "indulgence_fail";
    private const string ScenarioIndulgencePass = "indulgence_pass";
    private const string ScenarioSupplyShortageFail = "supply_shortage_fail";
    private const string ScenarioSupplyShortagePass = "supply_shortage_pass";
    private const string ScenarioLightningStrike = "lightning_strike";
    private const string ScenarioLightningPass = "lightning_pass";
    private const string ScenarioNullifyDismantle = "nullify_dismantle";
    private const string ScenarioNullifyArrowTarget = "nullify_arrow_target";
    private const string ScenarioGeneralSelect = "general_select";
    private const string ScenarioFullRoundBasic = "full_round_basic";
    private const string ScenarioIdentityVictory = "identity_victory";
    private const string ScenarioReturnLobbyAfterMatch = "return_lobby_after_match";
    private const int DefaultPort = 24601;
    private const double DefaultTimeoutSeconds = 20d;

    private string _role = HostRole;
    private string _scenario = ScenarioDodge;
    private string _address = "127.0.0.1";
    private int _port = DefaultPort;
    private double _timeoutSeconds = DefaultTimeoutSeconds;
    private int _clientIndex = 1;
    private double _elapsedSeconds;
    private bool _joinRequested;
    private bool _readySent;
    private bool _matchStartRequested;
    private bool _cardsInjected;
    private bool _slashSent;
    private bool _secondSlashAttempted;
    private bool _equipmentSent;
    private bool _dodgeSent;
    private bool _peachSent;
    private bool _activeSkillSent;
    private bool _declineSent;
    private bool _completed;
    private bool _preconditionApplied;
    private bool _observedPrimaryResolution;
    private bool _rangeGateVerified;
    private bool _fangtianGateVerified;
    private bool _generalSelectVerified;
    private bool _identityFactionsAssigned;
    private bool _returnLobbyRequested;
    private bool _fullRoundHostPlaySeen;
    private bool _fullRoundClientPlaySeen;
    private int _fullRoundLastEndedPlayPeerId;
    private string _fullRoundLastDiscardedCardId = string.Empty;
    private readonly HashSet<string> _autoDeclinedResponseWindows = new();
    private int _lastCharacterCount = -1;
    private double _scheduledSuccessAt = -1d;
    private double _nullificationResponseEligibleAt = -1d;
    private string _scheduledSuccessMessage = string.Empty;
    private string _successSignalPath = string.Empty;

    public override void _Ready()
    {
        ParseArguments();
        _successSignalPath = Path.Combine(Path.GetTempPath(), $"ciyuansha-smoke-{_scenario}-{_port}.success");
        if (IsHostRole)
        {
            File.Delete(_successSignalPath);
        }

        if (LanMultiplayerManager.Instance is null || GameManager.Instance is null)
        {
            Fail("Required autoloads were not initialized.");
            return;
        }

        LanMultiplayerManager.Instance.Port = _port;
        LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
        LanMultiplayerManager.Instance.OnNetworkError += HandleNetworkError;
        GameManager.Instance.OnBattleLogChanged += HandleBattleLogChanged;
        GameManager.Instance.OnRuleEvent += HandleRuleEvent;
        GameManager.Instance.EnableFactionVictoryRules = IsIdentityVictoryScenario;

        GD.Print($"[{_role}] Smoke test boot. scenario={_scenario} address={_address} port={_port}");

        if (IsHostRole)
        {
            Error error = LanMultiplayerManager.Instance.HostGame("SmokeHost", "guardian");
            if (error != Error.Ok)
            {
                Fail($"HostGame failed: {error}");
            }
        }
    }

    public override void _ExitTree()
    {
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
            LanMultiplayerManager.Instance.OnNetworkError -= HandleNetworkError;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnBattleLogChanged -= HandleBattleLogChanged;
            GameManager.Instance.OnRuleEvent -= HandleRuleEvent;
        }
    }

    public override void _Process(double delta)
    {
        if (_completed)
        {
            return;
        }

        _elapsedSeconds += delta;
        if (_elapsedSeconds >= _timeoutSeconds)
        {
            Fail($"Timed out after {_timeoutSeconds:F1}s.");
            return;
        }

        if (_scheduledSuccessAt >= 0d && _elapsedSeconds >= _scheduledSuccessAt)
        {
            Complete(_scheduledSuccessMessage);
            return;
        }

        if (!IsHostRole && File.Exists(_successSignalPath))
        {
            string hostMessage = File.ReadAllText(_successSignalPath);
            ScheduleSuccess(0.1d, $"Client received authoritative success for {_scenario}: {hostMessage}");
            return;
        }

        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        if (!IsHostRole && !_joinRequested && _elapsedSeconds >= 0.5d)
        {
            _joinRequested = true;
            Error error = network.JoinGame(_address, $"SmokeClient{_clientIndex}", "strategist");
            if (error != Error.Ok)
            {
                Fail($"JoinGame failed: {error}");
                return;
            }
        }

        if (network.SessionState == LanSessionState.InLobby && !_readySent)
        {
            _readySent = true;
            network.SelectLocalCharacter(IsHostRole ? "guardian" : "strategist");
            network.SetLocalReady(true);
            GD.Print($"[{_role}] Ready submitted.");
        }

        if (_scenario == ScenarioGeneralSelect)
        {
            TryCompleteGeneralSelectScenario(network);
            return;
        }

        if (IsHostRole && network.SessionState == LanSessionState.InLobby && !_matchStartRequested)
        {
            bool enoughPlayers = network.Players.Count >= RequiredPlayerCount;
            bool everyoneReady = enoughPlayers && network.Players.Values.All(player => player.IsReady);
            if (everyoneReady)
            {
                _matchStartRequested = true;
                GD.Print("[host] All players ready. Starting match.");
                network.StartMatch();
            }
        }

        if (!game.IsMatchRunning)
        {
            if (_scenario == ScenarioReturnLobbyAfterMatch)
            {
                TryCompleteReturnLobbyAfterMatchScenario(network, game);
            }

            return;
        }

        List<PlayerCharacter> characters = GetCharacters();
        if (characters.Count != _lastCharacterCount)
        {
            _lastCharacterCount = characters.Count;
            GD.Print($"[{_role}] Character count => {characters.Count}");
        }

        if (_scenario == ScenarioDeathNoSave
            && characters.Any(character => character.IsDefeated)
            && !game.HasPendingResponseWindow)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Slash -> death without rescue cleaned up defeated state and discard pile.");
            return;
        }

        if (_scenario == ScenarioWeaponRange
            && _rangeGateVerified
            && _slashSent
            && !game.HasPendingResponseWindow
            && game.IsAwaitingPlayerInput)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Weapon range modifier increased range and still allowed Slash to resolve.");
            return;
        }

        if (_scenario == ScenarioRenwangRed
            && _slashSent
            && !game.HasPendingResponseWindow
            && ResolveOpponentCharacter() is { } renwangTarget
            && renwangTarget.CurrentHealth < renwangTarget.MaxHealth)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Red normal Slash was not blocked by Renwang Shield.");
            return;
        }

        if (_scenario == ScenarioRenwangBlack
            && _slashSent
            && !game.HasPendingResponseWindow
            && game.IsAwaitingPlayerInput
            && ResolveOpponentCharacter() is { } blackSlashTarget
            && blackSlashTarget.CurrentHealth == blackSlashTarget.MaxHealth)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Black normal Slash was blocked by Renwang Shield.");
            return;
        }

        if (characters.Count < RequiredPlayerCount)
        {
            return;
        }

        if (IsIdentityVictoryScenario && IsHostRole && !_identityFactionsAssigned)
        {
            game.EnableFactionVictoryRules = true;
            game.AssignIdentityFactionsByTurnOrder(shuffleNonLordFactions: false);
            _identityFactionsAssigned = true;
            GD.Print("[host] Identity factions assigned for victory smoke test.");
        }

        if (_scenario == ScenarioFullRoundBasic)
        {
            TryRunFullRoundBasicScenario(game, network);
            return;
        }

        if (IsHostRole && !_cardsInjected)
        {
            PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
            PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
            if (hostCharacter is not null && opponentCharacter is not null)
            {
                EnsureSmokeState(hostCharacter, opponentCharacter);
                _cardsInjected = true;
            }
        }

        if (IsHostRole
            && !_slashSent
            && _scenario != ScenarioWeaponRange
            && _scenario != ScenarioActiveSkill
            && _scenario != ScenarioFangtian
            && _scenario != ScenarioStoneAxe
            && _scenario != ScenarioKylinBow
            && _scenario != ScenarioGreenDragon
            && _scenario != ScenarioDoubleSwords
            && _scenario != ScenarioSerpentSpear
            && _scenario != ScenarioIceSword
            && _scenario != ScenarioRenwangRed
            && _scenario != ScenarioRenwangBlack
            && _scenario != ScenarioGudingBlade
            && _scenario != ScenarioSilverLion
            && _scenario != ScenarioBorrowSwordTakeWeapon
            && _scenario != ScenarioSilverLionLost
            && _scenario != ScenarioEightDiagramArrows
            && _scenario != ScenarioQinggangVineSlash
            && _scenario != ScenarioBorrowSwordFireVine
            && _scenario != ScenarioChainFire
            && _scenario != ScenarioIndulgenceFail
            && _scenario != ScenarioIndulgencePass
            && _scenario != ScenarioSupplyShortageFail
            && _scenario != ScenarioSupplyShortagePass
            && _scenario != ScenarioLightningStrike
            && _scenario != ScenarioLightningPass
            && _scenario != ScenarioNullifyDismantle
            && _scenario != ScenarioNullifyArrowTarget
            && _scenario != ScenarioFullRoundBasic)
        {
            TrySendSmokeSlash();
        }

        if (_scenario == ScenarioActiveSkill
            && IsHostRole
            && !_activeSkillSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendSmokeActiveSkill();
        }

        if (_scenario == ScenarioWeaponRange
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipRangeWeaponThenSlash();
        }

        if (_scenario == ScenarioFangtian
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipFangtianThenSlash();
        }

        if (_scenario == ScenarioStoneAxe
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipStoneAxeThenSlash();
        }

        if (_scenario == ScenarioKylinBow
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipKylinBowThenSlash();
        }

        if (_scenario == ScenarioGreenDragon
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipGreenDragonThenSlash();
        }

        if (_scenario == ScenarioDoubleSwords
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipDoubleSwordsThenSlash();
        }

        if (_scenario == ScenarioSerpentSpear
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseSerpentSpearBarbarians();
        }

        if (_scenario == ScenarioIceSword
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipIceSwordThenSlash();
        }

        if (_scenario == ScenarioRenwangRed
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendRenwangRedSlash();
        }

        if (_scenario == ScenarioRenwangBlack
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendRenwangBlackSlash();
        }

        if (_scenario == ScenarioGudingBlade
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipGudingBladeThenSlash();
        }

        if (_scenario == ScenarioSilverLion
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendSilverLionHeavySlash();
        }

        if (_scenario == ScenarioBorrowSwordTakeWeapon
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseBorrowSwordTakeWeapon();
        }

        if (_scenario == ScenarioBorrowSwordFireVine
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseBorrowSwordFireVine();
        }

        if (_scenario == ScenarioSilverLionLost
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseDismantleOnSilverLion();
        }

        if (_scenario == ScenarioEightDiagramArrows
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseEightDiagramArrows();
        }

        if (_scenario == ScenarioQinggangVineSlash
            && IsHostRole
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryEquipQinggangThenSlash();
        }

        if (_scenario == ScenarioChainFire
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendChainFireSlash();
        }

        if ((_scenario == ScenarioIndulgenceFail
                || _scenario == ScenarioIndulgencePass
                || _scenario == ScenarioSupplyShortageFail
                || _scenario == ScenarioSupplyShortagePass
                || _scenario == ScenarioLightningStrike
                || _scenario == ScenarioLightningPass)
            && IsHostRole
            && !_slashSent)
        {
            TryResolveDelayedTrickScenario();
        }

        if (_scenario == ScenarioNullifyDismantle
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseNullifiedDismantle();
        }

        if (_scenario == ScenarioNullifyArrowTarget
            && IsHostRole
            && !_slashSent
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TryUseNullifiedArrowBarrage();
        }

        if (_scenario == ScenarioSilverLionLost
            && IsHostRole
            && _slashSent
            && !_declineSent
            && game.HasPendingTargetCardSelection
            && game.PendingTargetCardSelectionPeerId == LocalPeerId)
        {
            TryChooseSmokeTargetCard("smoke-client-silver-lion-lost");
        }

        if (_scenario == ScenarioSlashLimit
            && IsHostRole
            && _observedPrimaryResolution
            && !_secondSlashAttempted
            && game.CurrentPhase == TurnPhase.PlayPhase
            && game.CurrentTurnPeerId == LocalPeerId
            && game.IsAwaitingPlayerInput)
        {
            TrySendSecondSlashExpectReject();
        }

        if (!IsHostRole
            && (_scenario == ScenarioDodge || _scenario == ScenarioSlashLimit || _scenario == ScenarioWeaponRange || _scenario == ScenarioStoneAxe || _scenario == ScenarioGreenDragon)
            && !_dodgeSent
            && game.HasPendingResponseWindow
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TrySendSmokeDodge();
        }

        if (_scenario == ScenarioSerpentSpear
            && !IsHostRole
            && !_dodgeSent
            && game.HasPendingResponseWindow
            && game.PendingResponseKind == ResponseWindowKind.Slash
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TrySendSmokeSlashResponse();
        }

        if (_scenario == ScenarioNullifyDismantle
            && !IsHostRole
            && !_declineSent
            && game.HasPendingResponseWindow
            && game.PendingResponseKind == ResponseWindowKind.Nullification
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TrySendSmokeNullificationAfterWindowSettles();
        }

        if (_scenario == ScenarioNullifyArrowTarget
            && !IsHostRole
            && !_declineSent
            && game.HasPendingResponseWindow
            && game.PendingResponseKind == ResponseWindowKind.Nullification
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TrySendSmokeNullificationAfterWindowSettles();
        }

        if (_scenario == ScenarioPeachSave
            && IsHostRole
            && !_declineSent
            && game.HasPendingResponseWindow
            && game.PendingResponseKind == ResponseWindowKind.Peach
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TryDeclineSmokeResponse();
        }

        if ((_scenario == ScenarioPeachSave && !IsHostRole)
            || (_scenario == ScenarioPeachRescue && IsHostRole))
        {
            if (!_peachSent
                && game.HasPendingResponseWindow
                && game.PendingResponseKind == ResponseWindowKind.Peach
                && game.PendingResponsePeerId == LocalPeerId)
            {
                TrySendSmokePeach();
            }
        }

        if ((_scenario == ScenarioDeathNoSave || IsIdentityVictoryScenario)
            && game.HasPendingResponseWindow
            && game.PendingResponseKind == ResponseWindowKind.Peach
            && game.PendingResponsePeerId == LocalPeerId)
        {
            TryAutoDeclineCurrentResponse();
        }
    }

    private bool IsHostRole => string.Equals(_role, HostRole, StringComparison.OrdinalIgnoreCase);

	private int RequiredPlayerCount => _scenario is ScenarioFangtian or ScenarioBorrowSwordTakeWeapon or ScenarioBorrowSwordFireVine or ScenarioChainFire or ScenarioNullifyArrowTarget ? 3 : 2;

    private int LocalPeerId => LanMultiplayerManager.Instance?.LocalPeerId ?? 0;

    private bool IsIdentityVictoryScenario => _scenario is ScenarioIdentityVictory or ScenarioReturnLobbyAfterMatch;

    private void TryCompleteGeneralSelectScenario(LanMultiplayerManager network)
    {
        if (_generalSelectVerified || network.SessionState != LanSessionState.InLobby)
        {
            return;
        }

        bool hostReady = network.Players.Values.Any(player =>
            player.IsHost
            && player.IsReady
            && string.Equals(player.CharacterId, "guardian", StringComparison.Ordinal));
        bool clientReady = network.Players.Values.Any(player =>
            !player.IsHost
            && player.IsReady
            && string.Equals(player.CharacterId, "strategist", StringComparison.Ordinal));

        if (network.Players.Count < 2 || !hostReady || !clientReady)
        {
            return;
        }

        _generalSelectVerified = true;
        ScheduleSuccess(IsHostRole ? 0.75d : 1d, "Lobby general selection and ready state synchronized.");
    }

    private void TryRunFullRoundBasicScenario(GameManager game, LanMultiplayerManager network)
    {
        if (game.CurrentPhase == TurnPhase.PlayPhase && game.IsAwaitingPlayerInput)
        {
            if (game.CurrentTurnPeerId == network.HostPeerId)
            {
                if (_fullRoundHostPlaySeen && _fullRoundClientPlaySeen)
                {
                    ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Two-player match advanced through a full round and returned to the host.");
                    return;
                }

                _fullRoundHostPlaySeen = true;
            }
            else
            {
                _fullRoundClientPlaySeen = true;
            }

            if (game.CurrentTurnPeerId == LocalPeerId && _fullRoundLastEndedPlayPeerId != LocalPeerId)
            {
                bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
                {
                    CommandType = NetworkPlayCommandType.EndPlayPhase
                });
                if (sent)
                {
                    _fullRoundLastEndedPlayPeerId = LocalPeerId;
                    GD.Print($"[{_role}] Full-round smoke ended PlayPhase for peer {LocalPeerId}.");
                }
            }

            return;
        }

        if (game.CurrentPhase != TurnPhase.DiscardPhase
            || !game.IsAwaitingDiscardInput
            || game.CurrentTurnPeerId != LocalPeerId)
        {
            return;
        }

        PlayerCharacter? localCharacter = ResolveCharacter(LocalPeerId);
        if (localCharacter is null)
        {
            return;
        }

        int handLimit = Math.Max(0, localCharacter.CurrentHealth);
        if (localCharacter.HandCardCount <= handLimit)
        {
            return;
        }

        // A reliable RPC being queued does not immediately mutate the client's
        // replicated hand. Wait until the authoritative snapshot removes the
        // previously submitted card before selecting the next discard; otherwise
        // a fast frame loop can enqueue several legal-looking cards after the host
        // has already left DiscardPhase.
        if (!string.IsNullOrWhiteSpace(_fullRoundLastDiscardedCardId))
        {
            bool previousDiscardStillVisible = localCharacter.HandCards.Any(card =>
                string.Equals(card.InstanceId, _fullRoundLastDiscardedCardId, StringComparison.Ordinal));
            if (previousDiscardStillVisible)
            {
                return;
            }

            _fullRoundLastDiscardedCardId = string.Empty;
        }

        CardInstance? discardCard = localCharacter.HandCards.FirstOrDefault();
        if (discardCard is null)
        {
            return;
        }

        bool discardSent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.DiscardCard,
            CardInstanceId = discardCard.InstanceId
        });
        if (discardSent)
        {
            _fullRoundLastDiscardedCardId = discardCard.InstanceId;
            GD.Print($"[{_role}] Full-round smoke discarded {discardCard.DisplayName}.");
        }
    }

    private void TryCompleteReturnLobbyAfterMatchScenario(LanMultiplayerManager network, GameManager game)
    {
        bool hasResult = game.WinnerPeerId > 0 || !string.IsNullOrWhiteSpace(game.ResultMessage);
        if (IsHostRole
            && !_returnLobbyRequested
            && network.SessionState == LanSessionState.InMatch
            && hasResult)
        {
            _returnLobbyRequested = true;
            network.ReturnToLobby();
            GD.Print("[host] Return-to-lobby smoke requested lobby return.");
            return;
        }

        if (network.SessionState != LanSessionState.InLobby)
        {
            return;
        }

        bool everyoneUnready = network.Players.Count >= RequiredPlayerCount
            && network.Players.Values.All(player => !player.IsReady);
        if (!everyoneUnready)
        {
            return;
        }

        ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Match ended and all players returned to lobby unready.");
    }

    private void TryAutoDeclineCurrentResponse()
    {
        GameManager? game = GameManager.Instance;
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (game is null || network is null || !game.HasPendingResponseWindow)
        {
            return;
        }

        string responseKey = $"{game.PendingResponseKind}:{game.PendingResponsePeerId}:{game.PendingResponsePrompt}";
        if (!_autoDeclinedResponseWindows.Add(responseKey))
        {
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.DeclineResponse
        });
        if (sent)
        {
            GD.Print($"[{_role}] Auto-declined {game.PendingResponseKind} response.");
            return;
        }

        Fail($"Failed to auto-decline {game.PendingResponseKind} response.");
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        GD.Print($"[{_role}] SessionState => {state}");
    }

    private void HandleNetworkError(string message)
    {
        if (message.Contains("Disconnected from the LAN host", StringComparison.OrdinalIgnoreCase)
            && (((_scenario == ScenarioDodge || _scenario == ScenarioSlashLimit || _scenario == ScenarioWeaponRange) && _observedPrimaryResolution) || _scheduledSuccessAt >= 0d))
        {
            Complete("Client observed host shutdown after successful smoke flow.");
            return;
        }

        Fail($"Network error: {message}");
    }

    private void HandleBattleLogChanged()
    {
        GameManager? game = GameManager.Instance;
        if (game is null)
        {
            return;
        }

        string joinedLog = string.Join("\n", game.BattleLog.Select(entry => entry.Message));
        if (_scenario == ScenarioDodge
            && joinedLog.Contains("manually responds with Dodge.", StringComparison.Ordinal))
        {
            _observedPrimaryResolution = true;
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Slash -> Dodge chain resolved successfully.");
        }

        if (_scenario == ScenarioSlashLimit
            && joinedLog.Contains("manually responds with Dodge.", StringComparison.Ordinal))
        {
            _observedPrimaryResolution = true;
        }

        if (_scenario == ScenarioWeaponRange
            && joinedLog.Contains("equips Range Pike.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioFangtian
            && joinedLog.Contains("equips Fangtian Halberd.", StringComparison.Ordinal))
        {
            _fangtianGateVerified = true;
        }

        if (_scenario == ScenarioStoneAxe
            && joinedLog.Contains("equips Stone Axe.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioKylinBow
            && joinedLog.Contains("equips Kylin Bow.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioGreenDragon
            && joinedLog.Contains("equips Green Dragon Blade.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioDoubleSwords
            && joinedLog.Contains("equips Double Swords.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioIceSword
            && joinedLog.Contains("equips Ice Sword.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioGudingBlade
            && joinedLog.Contains("equips Guding Blade.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioQinggangVineSlash
            && joinedLog.Contains("equips Qinggang Sword.", StringComparison.Ordinal))
        {
            _rangeGateVerified = true;
        }

        if (_scenario == ScenarioWeaponRange
            && joinedLog.Contains("manually responds with Dodge.", StringComparison.Ordinal))
        {
            _observedPrimaryResolution = true;
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Weapon range modifier increased range and still allowed Slash to resolve.");
        }

        if (_scenario == ScenarioPeachSave
            && joinedLog.Contains("uses Peach to save themselves.", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Slash -> Peach self-save chain resolved successfully.");
        }

        if (_scenario == ScenarioPeachRescue
            && joinedLog.Contains("uses Peach to save ", StringComparison.Ordinal)
            && !joinedLog.Contains("uses Peach to save themselves.", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Slash -> Peach rescue chain resolved successfully.");
        }

        if (_scenario == ScenarioDeathNoSave
            && joinedLog.Contains("wins the match.", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Slash -> death without rescue resolved successfully.");
        }

        if (_scenario == ScenarioIdentityVictory
            && joinedLog.Contains("Lord faction wins because all Rebels and Renegades are defeated.", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Identity victory resolved and ended the match.");
        }

        if (_scenario == ScenarioActiveSkill
            && joinedLog.Contains("activates Stored Momentum", StringComparison.Ordinal)
            && joinedLog.Contains("Smoke Cost", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Active skill command consumed a hand-card cost and queued damage successfully.");
        }

        if (_scenario == ScenarioFangtian
            && joinedLog.Contains("uses Smoke Fangtian Slash on", StringComparison.Ordinal)
            && CountOccurrences(joinedLog, "uses Smoke Fangtian Slash on") >= 2)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Fangtian Halberd allowed the last Slash to target multiple characters.");
        }

        if (_scenario == ScenarioStoneAxe
            && joinedLog.Contains("manually responds with Dodge.", StringComparison.Ordinal)
            && joinedLog.Contains("Stone Axe forces damage through", StringComparison.Ordinal))
        {
            _observedPrimaryResolution = true;
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Stone Axe forced damage through after Dodge.");
        }

        if (_scenario == ScenarioKylinBow
            && joinedLog.Contains("Kylin Bow discards", StringComparison.Ordinal)
            && joinedLog.Contains("Smoke Horse", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Kylin Bow discarded the target's horse after Slash damage.");
        }

        if (_scenario == ScenarioGreenDragon
            && joinedLog.Contains("manually responds with Dodge.", StringComparison.Ordinal)
            && joinedLog.Contains("Green Dragon Blade follows up", StringComparison.Ordinal))
        {
            _observedPrimaryResolution = true;
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Green Dragon Blade followed up after a dodged Slash.");
        }

        if (_scenario == ScenarioDoubleSwords
            && joinedLog.Contains("discards Smoke Double Swords Cost", StringComparison.Ordinal)
            && joinedLog.Contains("Double Swords", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Double Swords forced the opposite-gender target to discard.");
        }

        if (_scenario == ScenarioSerpentSpear
            && joinedLog.Contains("Serpent Spear treats two hand cards as Slash.", StringComparison.Ordinal)
            && joinedLog.Contains("responds with Slash.", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Serpent Spear converted two hand cards into a Slash response.");
        }

        if (_scenario == ScenarioIceSword
            && joinedLog.Contains("Ice Sword prevents damage and discards 2 card", StringComparison.Ordinal)
            && joinedLog.Contains("Smoke Ice Cost", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Ice Sword prevented Slash damage and discarded target cards.");
        }

        if (_scenario == ScenarioRenwangRed
            && joinedLog.Contains("Renwang Shield blocks", StringComparison.Ordinal))
        {
            Fail("Renwang Shield blocked a red normal Slash, but it should only block black normal Slash.");
        }

        if (_scenario == ScenarioRenwangBlack
            && joinedLog.Contains("Renwang Shield blocks", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Black normal Slash was blocked by Renwang Shield.");
        }

        if (_scenario == ScenarioGudingBlade
            && joinedLog.Contains("Guding Blade adds +1 damage", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } gudingTarget
            && gudingTarget.CurrentHealth <= gudingTarget.MaxHealth - 2)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Guding Blade added damage against a handless target.");
        }

        if (_scenario == ScenarioSilverLion
            && joinedLog.Contains("Silver Lion reduces damage to 1", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } lionTarget
            && lionTarget.CurrentHealth == lionTarget.MaxHealth - 1)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Silver Lion reduced high damage to 1.");
        }

        if (_scenario == ScenarioBorrowSwordTakeWeapon
            && joinedLog.Contains("takes", StringComparison.Ordinal)
            && joinedLog.Contains("weapon with Borrow Sword", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { EquippedWeapon: null })
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Borrow Sword directly took the target weapon after Slash was declined.");
        }

        if (_scenario == ScenarioSilverLionLost
            && joinedLog.Contains("Silver Lion leaves equipment area and heals 1 HP", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { EquippedArmor: null } lionOwner
            && lionOwner.CurrentHealth == lionOwner.MaxHealth)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Silver Lion healed its owner after leaving the equipment area.");
        }

        if (_scenario == ScenarioEightDiagramArrows
            && joinedLog.Contains("Eight Diagram provides a Dodge.", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } eightTarget
            && eightTarget.CurrentHealth == eightTarget.MaxHealth)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Eight Diagram provided Dodge for Arrow Barrage.");
        }

        if (_scenario == ScenarioVineSlash
            && joinedLog.Contains("Vine Armor makes Slash ineffective", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } vineTarget
            && vineTarget.CurrentHealth == vineTarget.MaxHealth)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Vine Armor made normal Slash ineffective before Dodge response.");
        }

        if (_scenario == ScenarioVineFireSlash
            && joinedLog.Contains("Vine Armor increases fire damage by 1", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } vineFireTarget
            && vineFireTarget.CurrentHealth == vineFireTarget.MaxHealth - 2)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Vine Armor increased Fire Slash damage by 1.");
        }

        if (_scenario == ScenarioQinggangVineSlash
            && joinedLog.Contains("armor is ignored", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } qinggangTarget
            && qinggangTarget.CurrentHealth == qinggangTarget.MaxHealth - 1)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Qinggang Sword ignored Vine Armor for normal Slash.");
        }

        if (_scenario == ScenarioBorrowSwordFireVine
            && joinedLog.Contains("plays Slash for Borrow Sword", StringComparison.Ordinal)
            && joinedLog.Contains("Vine Armor increases fire damage by 1", StringComparison.Ordinal)
            && ResolveOpponentCharacters().Skip(1).FirstOrDefault() is { } vineVictim
            && vineVictim.CurrentHealth == vineVictim.MaxHealth - 2)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Borrow Sword preserved Fire Slash damage into Vine Armor.");
        }

        if (_scenario == ScenarioChainFire
            && joinedLog.Contains("chain conducts Fire damage", StringComparison.Ordinal)
            && ResolveOpponentCharacters().Count >= 2
            && ResolveOpponentCharacters().Take(2).All(target => target.CurrentHealth == target.MaxHealth - 1))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Fire damage propagated through chained characters.");
        }

        if (_scenario == ScenarioIndulgenceFail
            && joinedLog.Contains("Indulgence resolves: skip PlayPhase", StringComparison.Ordinal)
            && joinedLog.Contains("skips PlayPhase", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Indulgence failed judgement and skipped PlayPhase.");
        }

        if (_scenario == ScenarioIndulgencePass
            && joinedLog.Contains("Indulgence judgement succeeds. PlayPhase is not skipped", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Indulgence passed judgement and did not skip PlayPhase.");
        }

        if (_scenario == ScenarioSupplyShortageFail
            && joinedLog.Contains("Supply Shortage resolves: skip DrawPhase", StringComparison.Ordinal)
            && joinedLog.Contains("skips DrawPhase", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Supply Shortage failed judgement and skipped DrawPhase.");
        }

        if (_scenario == ScenarioSupplyShortagePass
            && joinedLog.Contains("Supply Shortage judgement succeeds. DrawPhase is not skipped", StringComparison.Ordinal))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Supply Shortage passed judgement and did not skip DrawPhase.");
        }

        if (_scenario == ScenarioLightningStrike
            && joinedLog.Contains("Lightning strikes for 3 thunder damage", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } lightningTarget
            && lightningTarget.CurrentHealth == lightningTarget.MaxHealth - 3)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Lightning struck for 3 thunder damage.");
        }

        if (_scenario == ScenarioLightningPass
            && joinedLog.Contains("Lightning passes to", StringComparison.Ordinal)
            && ResolveCharacter(LocalPeerId) is { } lightningReceiver
            && lightningReceiver.HasDelayedTrick(CardType.Lightning))
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Lightning missed and passed to the next alive character.");
        }

        if (_scenario == ScenarioNullifyDismantle
            && joinedLog.Contains("Dismantle on", StringComparison.Ordinal)
            && joinedLog.Contains("is cancelled by", StringComparison.Ordinal)
            && ResolveOpponentCharacter() is { } nullifiedTarget
            && nullifiedTarget.FindHandCard("smoke-client-nullify-kept") is not null
            && GameManager.Instance?.HasPendingTargetCardSelection != true)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Nullification cancelled Dismantle before target-card selection.");
        }

        if (_scenario == ScenarioNullifyArrowTarget
            && joinedLog.Contains("Arrow Barrage on", StringComparison.Ordinal)
            && joinedLog.Contains("is cancelled by", StringComparison.Ordinal)
            && ResolveOpponentCharacters().Count >= 2
            && ResolveOpponentCharacters().First().CurrentHealth == ResolveOpponentCharacters().First().MaxHealth
            && ResolveOpponentCharacters().Skip(1).First().CurrentHealth == ResolveOpponentCharacters().Skip(1).First().MaxHealth - 1)
        {
            ScheduleSuccess(IsHostRole ? 0.75d : 0d, "Nullification cancelled Arrow Barrage for one target only.");
        }
    }

    private void HandleRuleEvent(RuleEventContext context)
    {
        if ((_scenario == ScenarioChainFire || _scenario == ScenarioNullifyArrowTarget)
            && context.EventType == RuleEventType.DamageApplied)
        {
            HandleBattleLogChanged();
        }
    }

    private void TrySendSmokeSlash()
    {
        GameManager? game = GameManager.Instance;
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (game is null || network is null)
        {
            return;
        }

        if (game.CurrentPhase != TurnPhase.PlayPhase || game.CurrentTurnPeerId != LocalPeerId || !game.IsAwaitingPlayerInput)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        string expectedSlashId = _scenario == ScenarioWeaponRange ? "smoke-host-range-slash" : "smoke-host-slash";
        CardInstance? slashCard = hostCharacter.HandCards.FirstOrDefault(card => card.InstanceId == expectedSlashId);
        if (slashCard is null)
        {
            Fail("Host smoke Slash card was not found.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit smoke Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Slash submitted.");
    }

    private void TrySendSmokeDodge()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? localCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? dodgeCard = localCharacter?.HandCards.FirstOrDefault(card => card.CardType == CardType.Dodge);
        if (localCharacter is null || dodgeCard is null)
        {
            Fail("Client response window opened without a Dodge in hand.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.RespondDodge,
            CardInstanceId = dodgeCard.InstanceId
        });

        if (!sent)
        {
            Fail("Client failed to submit Dodge response.");
            return;
        }

        _dodgeSent = true;
        GD.Print("[client] Smoke Dodge submitted.");
    }

    private void TrySendSmokeActiveSkill()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? costCard = hostCharacter?.FindHandCard("smoke-host-skill-cost");
        if (hostCharacter is null || opponentCharacter is null || costCard is null)
        {
            Fail("Active skill test could not resolve source, target, or cost card.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.ActivateSkill,
            SkillId = "discard_strike",
            TargetPeerId = opponentCharacter.OwnerPeerId,
            TargetPeerIds = new List<int> { opponentCharacter.OwnerPeerId },
            CardInstanceId = costCard.InstanceId
        });

        if (!sent)
        {
            Fail("Host failed to submit smoke active skill command.");
            return;
        }

        _activeSkillSent = true;
        GD.Print("[host] Smoke active skill submitted.");
    }

    private void TrySendSecondSlashExpectReject()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? secondSlash = hostCharacter?.HandCards.FirstOrDefault(card => card.InstanceId == "smoke-host-second-slash");
        if (hostCharacter is null || opponentCharacter is null || secondSlash is null)
        {
            Fail("Second Slash limit test could not find the prepared card.");
            return;
        }

        _secondSlashAttempted = true;
        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = secondSlash.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (sent)
        {
            Fail("Second Slash was accepted, but the once-per-turn limit should reject it.");
            return;
        }

        ScheduleSuccess(0.75d, "Second Slash was correctly rejected by the once-per-turn limit.");
    }

    private void TryEquipRangeWeaponThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            if (hostCharacter.EffectiveAttackRange != 1)
            {
                Fail($"Range test expected base attack range 1 before equipping, but got {hostCharacter.EffectiveAttackRange}.");
                return;
            }

            CardInstance? weaponCard = hostCharacter.HandCards.FirstOrDefault(card => card.InstanceId == "smoke-host-range-weapon");
            if (weaponCard is null)
            {
                Fail("Range test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Range test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke range weapon submitted.");
            return;
        }

        if (hostCharacter.EffectiveAttackRange < 2)
        {
            Fail($"Range test expected equipped attack range >= 2, but got {hostCharacter.EffectiveAttackRange}.");
            return;
        }

        TrySendSmokeSlash();
    }

    private void TryEquipFangtianThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        List<PlayerCharacter> opponents = ResolveOpponentCharacters();
        if (hostCharacter is null || opponents.Count < 2)
        {
            return;
        }

        if (!_fangtianGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-fangtian");
            if (weaponCard is null)
            {
                Fail("Fangtian test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Fangtian test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Fangtian weapon submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-fangtian-slash");
        if (slashCard is null)
        {
            Fail("Fangtian test could not find the prepared last Slash.");
            return;
        }

        if (hostCharacter.HandCardCount != 1)
        {
            Fail($"Fangtian test expected Slash to be the last hand card, but hand count is {hostCharacter.HandCardCount}.");
            return;
        }

        List<int> targetPeerIds = opponents
            .Select(character => character.OwnerPeerId)
            .Take(2)
            .ToList();
        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = slashCard.InstanceId,
            TargetPeerId = targetPeerIds.FirstOrDefault(),
            TargetPeerIds = targetPeerIds,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Fangtian multi-target Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Fangtian multi-target Slash submitted.");
    }

    private void TryEquipStoneAxeThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-stone-axe");
            if (weaponCard is null)
            {
                Fail("Stone Axe test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Stone Axe test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Stone Axe submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-stone-axe-slash");
        if (slashCard is null)
        {
            Fail("Stone Axe test could not find the prepared Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Stone Axe Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Stone Axe Slash submitted.");
    }

    private void TryEquipKylinBowThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-kylin-bow");
            if (weaponCard is null)
            {
                Fail("Kylin Bow test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Kylin Bow test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Kylin Bow submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-kylin-slash");
        if (slashCard is null)
        {
            Fail("Kylin Bow test could not find the prepared Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Kylin Bow Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Kylin Bow Slash submitted.");
    }

    private void TryEquipGreenDragonThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-green-dragon");
            if (weaponCard is null)
            {
                Fail("Green Dragon test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Green Dragon test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Green Dragon Blade submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-green-dragon-slash-1");
        if (slashCard is null)
        {
            Fail("Green Dragon test could not find the prepared first Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Green Dragon Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Green Dragon Slash submitted.");
    }

    private void TryEquipDoubleSwordsThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-double-swords");
            if (weaponCard is null)
            {
                Fail("Double Swords test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Double Swords test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Double Swords submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-double-swords-slash");
        if (slashCard is null)
        {
            Fail("Double Swords test could not find the prepared Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Double Swords Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Double Swords Slash submitted.");
    }

    private void TryUseSerpentSpearBarbarians()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? barbariansCard = hostCharacter?.FindHandCard("smoke-host-serpent-barbarians");
        if (hostCharacter is null || barbariansCard is null)
        {
            Fail("Serpent Spear test could not find the prepared Barbarians card.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = barbariansCard.InstanceId
        });

        if (!sent)
        {
            Fail("Host failed to submit Serpent Spear Barbarians command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Barbarians submitted for Serpent Spear response.");
    }

    private void TryEquipIceSwordThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-ice-sword");
            if (weaponCard is null)
            {
                Fail("Ice Sword test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Ice Sword test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Ice Sword submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-ice-slash");
        if (slashCard is null)
        {
            Fail("Ice Sword test could not find the prepared Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Ice Sword Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Ice Sword Slash submitted.");
    }

    private void TrySendRenwangRedSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? slashCard = hostCharacter?.FindHandCard("smoke-host-renwang-red-slash");
        if (hostCharacter is null || opponentCharacter is null || slashCard is null)
        {
            Fail("Renwang red Slash test could not resolve source, target, or Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Renwang red Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Renwang red Slash submitted.");
    }

    private void TrySendRenwangBlackSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? slashCard = hostCharacter?.FindHandCard("smoke-host-renwang-black-slash");
        if (hostCharacter is null || opponentCharacter is null || slashCard is null)
        {
            Fail("Renwang black Slash test could not resolve source, target, or Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Renwang black Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Renwang black Slash submitted.");
    }

    private void TryEquipGudingBladeThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-guding-blade");
            if (weaponCard is null)
            {
                Fail("Guding Blade test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Guding Blade test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Guding Blade submitted.");
            return;
        }

        CardInstance? slashCard = hostCharacter.FindHandCard("smoke-host-guding-slash");
        if (slashCard is null)
        {
            Fail("Guding Blade test could not find the prepared Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Guding Blade Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Guding Blade Slash submitted.");
    }

    private void TryEquipQinggangThenSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        if (hostCharacter is null || opponentCharacter is null)
        {
            return;
        }

        if (!_rangeGateVerified)
        {
            CardInstance? weaponCard = hostCharacter.FindHandCard("smoke-host-qinggang");
            if (weaponCard is null)
            {
                Fail("Qinggang Vine test could not find the prepared weapon card.");
                return;
            }

            bool equipped = network.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UseEquipment,
                CardInstanceId = weaponCard.InstanceId
            });

            if (!equipped)
            {
                Fail("Qinggang Vine test failed to equip the prepared weapon.");
                return;
            }

            _equipmentSent = true;
            GD.Print("[host] Smoke Qinggang Sword submitted.");
            return;
        }

        TrySendSmokeSlash();
    }

    private void TrySendSilverLionHeavySlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? slashCard = hostCharacter?.FindHandCard("smoke-host-silver-heavy-slash");
        if (hostCharacter is null || opponentCharacter is null || slashCard is null)
        {
            Fail("Silver Lion test could not resolve source, target, or Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Physical",
            DamageValue = 3
        });

        if (!sent)
        {
            Fail("Host failed to submit Silver Lion heavy Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Silver Lion heavy Slash submitted.");
    }

    private void TryUseBorrowSwordTakeWeapon()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        List<PlayerCharacter> targets = ResolveOpponentCharacters();
        PlayerCharacter? weaponHolder = targets.FirstOrDefault();
        PlayerCharacter? slashTarget = targets.Skip(1).FirstOrDefault() ?? hostCharacter;
        CardInstance? borrowSwordCard = hostCharacter?.FindHandCard("smoke-host-borrow-sword");
        if (hostCharacter is null || weaponHolder is null || slashTarget is null || borrowSwordCard is null)
        {
            Fail("Borrow Sword test could not resolve source, weapon holder, slash target, or card.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = borrowSwordCard.InstanceId,
            TargetPeerId = weaponHolder.OwnerPeerId,
            TargetPeerIds = new List<int> { weaponHolder.OwnerPeerId, slashTarget.OwnerPeerId }
        });

        if (!sent)
        {
            Fail("Host failed to submit Borrow Sword command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Borrow Sword submitted.");
    }

    private void TryUseBorrowSwordFireVine()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        List<PlayerCharacter> targets = ResolveOpponentCharacters();
        PlayerCharacter? weaponHolder = targets.FirstOrDefault();
        PlayerCharacter? slashTarget = targets.Skip(1).FirstOrDefault();
        CardInstance? borrowSwordCard = hostCharacter?.FindHandCard("smoke-host-borrow-fire-vine");
        if (hostCharacter is null || weaponHolder is null || slashTarget is null || borrowSwordCard is null)
        {
            Fail("Borrow Sword Fire Vine test could not resolve source, weapon holder, slash target, or card.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = borrowSwordCard.InstanceId,
            TargetPeerId = weaponHolder.OwnerPeerId,
            TargetPeerIds = new List<int> { weaponHolder.OwnerPeerId, slashTarget.OwnerPeerId }
        });

        if (!sent)
        {
            Fail("Host failed to submit Borrow Sword Fire Vine command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Borrow Sword Fire Vine submitted.");
    }

    private void TryUseDismantleOnSilverLion()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? dismantleCard = hostCharacter?.FindHandCard("smoke-host-silver-lion-dismantle");
        if (hostCharacter is null || opponentCharacter is null || dismantleCard is null)
        {
            Fail("Silver Lion lost test could not resolve source, target, or Dismantle.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = dismantleCard.InstanceId,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            TargetPeerIds = new List<int> { opponentCharacter.OwnerPeerId }
        });

        if (!sent)
        {
            Fail("Host failed to submit Silver Lion Dismantle command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Silver Lion Dismantle submitted.");
    }

    private void TryUseEightDiagramArrows()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? arrowCard = hostCharacter?.FindHandCard("smoke-host-eight-arrows");
        if (hostCharacter is null || arrowCard is null)
        {
            Fail("Eight Diagram arrows test could not resolve source or Arrow Barrage.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = arrowCard.InstanceId
        });

        if (!sent)
        {
            Fail("Host failed to submit Arrow Barrage for Eight Diagram test.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Arrow Barrage submitted for Eight Diagram test.");
    }

    private void TryUseNullifiedDismantle()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? opponentCharacter = ResolveOpponentCharacter();
        CardInstance? dismantleCard = hostCharacter?.FindHandCard("smoke-host-nullified-dismantle");
        if (hostCharacter is null || opponentCharacter is null || dismantleCard is null)
        {
            Fail("Nullify Dismantle test could not resolve source, target, or Dismantle.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = dismantleCard.InstanceId,
            TargetPeerId = opponentCharacter.OwnerPeerId,
            TargetPeerIds = new List<int> { opponentCharacter.OwnerPeerId }
        });

        if (!sent)
        {
            Fail("Host failed to submit nullified Dismantle command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke nullified Dismantle submitted.");
    }

    private void TryUseNullifiedArrowBarrage()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? arrowCard = hostCharacter?.FindHandCard("smoke-host-nullified-arrows");
        if (hostCharacter is null || arrowCard is null)
        {
            Fail("Nullify Arrow target test could not resolve source or Arrow Barrage.");
            return;
        }

        // DrawPhase may give the source a Nullification after fixture injection.
        RemoveCardsOfType(hostCharacter, CardType.Nullification);

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = arrowCard.InstanceId
        });

        if (!sent)
        {
            Fail("Host failed to submit nullified Arrow Barrage command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke nullified Arrow Barrage submitted.");
    }

    private void TrySendChainFireSlash()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? firstTarget = ResolveOpponentCharacters().FirstOrDefault();
        CardInstance? slashCard = hostCharacter?.FindHandCard("smoke-host-chain-fire-slash");
        if (hostCharacter is null || firstTarget is null || slashCard is null)
        {
            Fail("Chain Fire test could not resolve source, first target, or Fire Slash.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.BasicAttack,
            TargetPeerId = firstTarget.OwnerPeerId,
            CardInstanceId = slashCard.InstanceId,
            DamageType = "Fire",
            DamageValue = 1
        });

        if (!sent)
        {
            Fail("Host failed to submit Chain Fire Slash command.");
            return;
        }

        _slashSent = true;
        GD.Print("[host] Smoke Chain Fire Slash submitted.");
    }

    private void TryResolveDelayedTrickScenario()
    {
        GameManager? game = GameManager.Instance;
        PlayerCharacter? hostCharacter = ResolveCharacter(LocalPeerId);
        PlayerCharacter? targetCharacter = ResolveOpponentCharacter();
        if (game is null || hostCharacter is null || targetCharacter is null)
        {
            return;
        }

        CardType delayedType = _scenario switch
        {
            ScenarioIndulgencePass => CardType.Indulgence,
            ScenarioIndulgenceFail => CardType.Indulgence,
            ScenarioSupplyShortagePass => CardType.SupplyShortage,
            ScenarioSupplyShortageFail => CardType.SupplyShortage,
            ScenarioLightningPass => CardType.Lightning,
            ScenarioLightningStrike => CardType.Lightning,
            _ => CardType.None
        };
        if (delayedType == CardType.None)
        {
            return;
        }

        RemoveAllHandCards(hostCharacter);
        RemoveAllHandCards(targetCharacter);
        RemoveCardsOfType(hostCharacter, CardType.Peach);
        RemoveCardsOfType(targetCharacter, CardType.Peach);

        CardInstance delayedTrick = new()
        {
            InstanceId = $"smoke-delayed-{_scenario}",
            CardType = delayedType,
            DisplayName = delayedType.ToString(),
            Description = $"Smoke-test delayed trick: {_scenario}.",
            DamageValue = 0,
            IsDelayedTrick = true
        };
        if (!game.PlaceDelayedTrick(hostCharacter, targetCharacter, delayedTrick))
        {
            Fail($"Failed to place delayed trick for scenario {_scenario}.");
            return;
        }

        game.PushCardToDrawPileTopForTest(CreateDelayedJudgementCard(_scenario));
        game.SetCurrentTurnPeerId(targetCharacter.OwnerPeerId);
        game.StartTurn();
        _slashSent = true;
        GD.Print($"[host] Smoke delayed trick scenario resolved: {_scenario}.");
    }

    private static CardInstance CreateDelayedJudgementCard(string scenario)
    {
        return scenario switch
        {
            ScenarioIndulgencePass => new CardInstance
            {
                InstanceId = "smoke-judge-indulgence-pass",
                CardType = CardType.Slash,
                DisplayName = "Smoke Heart Judgement",
                Description = "Smoke-test heart judgement for Indulgence.",
                Suit = CardSuit.Heart,
                Rank = 6,
                DamageValue = 1
            },
            ScenarioIndulgenceFail => new CardInstance
            {
                InstanceId = "smoke-judge-indulgence-fail",
                CardType = CardType.Slash,
                DisplayName = "Smoke Black Judgement",
                Description = "Smoke-test non-heart judgement for Indulgence.",
                Suit = CardSuit.Spade,
                Rank = 6,
                DamageValue = 1
            },
            ScenarioSupplyShortagePass => new CardInstance
            {
                InstanceId = "smoke-judge-supply-pass",
                CardType = CardType.Slash,
                DisplayName = "Smoke Club Judgement",
                Description = "Smoke-test club judgement for Supply Shortage.",
                Suit = CardSuit.Club,
                Rank = 6,
                DamageValue = 1
            },
            ScenarioSupplyShortageFail => new CardInstance
            {
                InstanceId = "smoke-judge-supply-fail",
                CardType = CardType.Slash,
                DisplayName = "Smoke Non-Club Judgement",
                Description = "Smoke-test non-club judgement for Supply Shortage.",
                Suit = CardSuit.Heart,
                Rank = 6,
                DamageValue = 1
            },
            ScenarioLightningPass => new CardInstance
            {
                InstanceId = "smoke-judge-lightning-pass",
                CardType = CardType.Slash,
                DisplayName = "Smoke Heart Judgement",
                Description = "Smoke-test non-strike judgement for Lightning.",
                Suit = CardSuit.Heart,
                Rank = 5,
                DamageValue = 1
            },
            ScenarioLightningStrike => new CardInstance
            {
                InstanceId = "smoke-judge-lightning-strike",
                CardType = CardType.Slash,
                DisplayName = "Smoke Spade Judgement",
                Description = "Smoke-test Spade 2-9 judgement for Lightning.",
                Suit = CardSuit.Spade,
                Rank = 5,
                DamageValue = 1
            },
            _ => new CardInstance
            {
                InstanceId = "smoke-judge-default",
                CardType = CardType.Slash,
                DisplayName = "Smoke Judgement",
                Description = "Smoke-test judgement card.",
                Suit = CardSuit.Spade,
                Rank = 1,
                DamageValue = 1
            }
        };
    }

    private void TryChooseSmokeTargetCard(string preferredInstanceId)
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        GameManager? game = GameManager.Instance;
        if (network is null || game is null)
        {
            return;
        }

        CardInstance? selectedCard = game.TargetCardSelectionPool.FirstOrDefault(card => card.InstanceId == preferredInstanceId)
            ?? game.TargetCardSelectionPool.FirstOrDefault();
        if (selectedCard is null)
        {
            Fail("Smoke target-card selection opened without a selectable card.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.ChooseTargetCard,
            CardInstanceId = selectedCard.InstanceId
        });

        if (!sent)
        {
            Fail("Failed to choose smoke target card.");
            return;
        }

        _declineSent = true;
        GD.Print($"[host] Smoke target card chosen: {selectedCard.DisplayName}.");
    }

    private void TrySendSmokePeach()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? localCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? peachCard = localCharacter?.HandCards.FirstOrDefault(card => card.CardType == CardType.Peach);
        if (localCharacter is null || peachCard is null)
        {
            Fail("Peach response window opened without a Peach in hand.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.RespondPeach,
            CardInstanceId = peachCard.InstanceId
        });

        if (!sent)
        {
            Fail("Failed to submit Peach response.");
            return;
        }

        _peachSent = true;
        GD.Print($"[{_role}] Smoke Peach submitted.");
    }

    private void TrySendSmokeSlashResponse()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.RespondSlash
        });

        if (!sent)
        {
            Fail("Client failed to submit Slash response.");
            return;
        }

        _dodgeSent = true;
        GD.Print("[client] Smoke Slash response submitted.");
    }

    private void TrySendSmokeNullification()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        PlayerCharacter? localCharacter = ResolveCharacter(LocalPeerId);
        CardInstance? nullificationCard = localCharacter?.FindHandCard("smoke-client-nullification");
        if (localCharacter is null || nullificationCard is null)
        {
            Fail("Nullification response window opened without the prepared Nullification.");
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.RespondNullification,
            CardInstanceId = nullificationCard.InstanceId
        });

        if (!sent)
        {
            Fail("Client failed to submit Nullification response.");
            return;
        }

        _declineSent = true;
        GD.Print("[client] Smoke Nullification submitted.");
    }

    private void TrySendSmokeNullificationAfterWindowSettles()
    {
        if (_nullificationResponseEligibleAt < 0d)
        {
            _nullificationResponseEligibleAt = _elapsedSeconds + 0.20d;
            return;
        }

        if (_elapsedSeconds >= _nullificationResponseEligibleAt)
        {
            TrySendSmokeNullification();
        }
    }

    private void TryDeclineSmokeResponse()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        bool sent = network.SubmitPlayCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.DeclineResponse
        });

        if (!sent)
        {
            Fail("Failed to decline the smoke response window.");
            return;
        }

        _declineSent = true;
        GD.Print($"[{_role}] Smoke response declined.");
    }

    private void EnsureSmokeState(PlayerCharacter hostCharacter, PlayerCharacter clientCharacter)
    {
        if (hostCharacter.FindHandCard("smoke-host-slash") is null)
        {
            hostCharacter.AddCard(new CardInstance
            {
                InstanceId = "smoke-host-slash",
                CardType = CardType.Slash,
                DisplayName = "Smoke Slash",
                Description = "Smoke-test Slash.",
                DamageValue = 1
            });
        }

        if (_scenario == ScenarioWeaponRange && hostCharacter.FindHandCard("smoke-host-range-slash") is null)
        {
            hostCharacter.AddCard(new CardInstance
            {
                InstanceId = "smoke-host-range-slash",
                CardType = CardType.Slash,
                DisplayName = "Smoke Range Slash",
                Description = "Smoke-test out-of-range Slash.",
                DamageValue = 1
            });
        }

        if (_scenario == ScenarioSlashLimit && hostCharacter.FindHandCard("smoke-host-second-slash") is null)
        {
            hostCharacter.AddCard(new CardInstance
            {
                InstanceId = "smoke-host-second-slash",
                CardType = CardType.Slash,
                DisplayName = "Smoke Slash 2",
                Description = "Smoke-test second Slash.",
                DamageValue = 1
            });
        }

        if (_scenario == ScenarioWeaponRange && hostCharacter.FindHandCard("smoke-host-range-weapon") is null)
        {
            hostCharacter.AddCard(new CardInstance
            {
                InstanceId = "smoke-host-range-weapon",
                CardType = CardType.Weapon,
                DisplayName = "Range Pike",
                Description = "Smoke-test range weapon.",
                EquipmentSlot = EquipmentSlotType.Weapon,
                AttackRangeModifier = 1
            });
        }

        if (_scenario == ScenarioActiveSkill)
        {
            if (hostCharacter.Skills.All(skill => skill.SkillId != "discard_strike"))
            {
                hostCharacter.AddSkill(new DiscardStrikeSkill());
            }

            if (hostCharacter.FindHandCard("smoke-host-skill-cost") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-skill-cost",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Cost",
                    Description = "Smoke-test active skill cost.",
                    DamageValue = 1
                });
            }

            RemoveCardsOfType(clientCharacter, CardType.Peach);
            RemoveCardsOfType(clientCharacter, CardType.Dodge);
        }

        if (_scenario == ScenarioFangtian)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                foreach (PlayerCharacter opponent in ResolveOpponentCharacters())
                {
                    RemoveCardsOfType(opponent, CardType.Peach);
                    RemoveCardsOfType(opponent, CardType.Dodge);
                }

                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-fangtian") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-fangtian")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-fangtian",
                    CardType = CardType.Weapon,
                    DisplayName = "Fangtian Halberd",
                    Description = "Smoke-test Fangtian Halberd.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.FangtianHalberd,
                    AttackRangeModifier = 2
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-fangtian-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-fangtian-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Fangtian Slash",
                    Description = "Smoke-test last-hand Slash for Fangtian Halberd.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioStoneAxe)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveCardsOfType(clientCharacter, CardType.Peach);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-stone-axe") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-stone-axe")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-stone-axe",
                    CardType = CardType.Weapon,
                    DisplayName = "Stone Axe",
                    Description = "Smoke-test Stone Axe.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.StoneAxe,
                    AttackRangeModifier = 2
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-stone-axe-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-stone-axe-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Stone Axe Slash",
                    Description = "Smoke-test Slash for Stone Axe.",
                    DamageValue = 1
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-stone-axe-cost-1") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-stone-axe-cost-1",
                    CardType = CardType.Dodge,
                    DisplayName = "Stone Axe Cost 1",
                    Description = "Smoke-test Stone Axe discard cost.",
                    DamageValue = 0
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-stone-axe-cost-2") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-stone-axe-cost-2",
                    CardType = CardType.Peach,
                    DisplayName = "Stone Axe Cost 2",
                    Description = "Smoke-test Stone Axe discard cost.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioKylinBow)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveCardsOfType(clientCharacter, CardType.Dodge);
                RemoveCardsOfType(clientCharacter, CardType.Peach);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-horse",
                    CardType = CardType.DefensiveHorse,
                    DisplayName = "Smoke Horse",
                    Description = "Smoke-test horse for Kylin Bow.",
                    EquipmentSlot = EquipmentSlotType.DefensiveHorse,
                    DefenseDistanceModifier = 1
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-kylin-bow") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-kylin-bow")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-kylin-bow",
                    CardType = CardType.Weapon,
                    DisplayName = "Kylin Bow",
                    Description = "Smoke-test Kylin Bow.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.KylinBow,
                    AttackRangeModifier = 4
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-kylin-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-kylin-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Kylin Slash",
                    Description = "Smoke-test Slash for Kylin Bow.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioGreenDragon)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveCardsOfType(clientCharacter, CardType.Peach);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-green-dragon") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-green-dragon")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-green-dragon",
                    CardType = CardType.Weapon,
                    DisplayName = "Green Dragon Blade",
                    Description = "Smoke-test Green Dragon Blade.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.GreenDragonBlade,
                    AttackRangeModifier = 2
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-green-dragon-slash-1") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-green-dragon-slash-1",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Green Dragon Slash 1",
                    Description = "Smoke-test first Slash for Green Dragon Blade.",
                    DamageValue = 1
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-green-dragon-slash-2") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-green-dragon-slash-2",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Green Dragon Slash 2",
                    Description = "Smoke-test follow-up Slash for Green Dragon Blade.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioDoubleSwords)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                hostCharacter.SetGender(PlayerGender.Male);
                clientCharacter.SetGender(PlayerGender.Female);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-double-swords") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-double-swords")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-double-swords",
                    CardType = CardType.Weapon,
                    DisplayName = "Double Swords",
                    Description = "Smoke-test Double Swords.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.DoubleSwords,
                    AttackRangeModifier = 1
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-double-swords-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-double-swords-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Double Swords Slash",
                    Description = "Smoke-test Slash for Double Swords.",
                    DamageValue = 1
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-double-swords-cost") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-double-swords-cost",
                    CardType = CardType.Peach,
                    DisplayName = "Smoke Double Swords Cost",
                    Description = "Smoke-test forced discard for Double Swords.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioSerpentSpear)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-serpent-spear",
                    CardType = CardType.Weapon,
                    DisplayName = "Serpent Spear",
                    Description = "Smoke-test Serpent Spear.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.SerpentSpear,
                    AttackRangeModifier = 2
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-serpent-barbarians") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-serpent-barbarians",
                    CardType = CardType.Barbarians,
                    DisplayName = "Smoke Barbarians",
                    Description = "Smoke-test Barbarians for Serpent Spear.",
                    DamageValue = 0
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-serpent-cost-1") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-serpent-cost-1",
                    CardType = CardType.Peach,
                    DisplayName = "Serpent Spear Cost 1",
                    Description = "Smoke-test Serpent Spear cost.",
                    DamageValue = 1
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-serpent-cost-2") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-serpent-cost-2",
                    CardType = CardType.Dodge,
                    DisplayName = "Serpent Spear Cost 2",
                    Description = "Smoke-test Serpent Spear cost.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioIceSword)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-ice-sword") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-ice-sword")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-ice-sword",
                    CardType = CardType.Weapon,
                    DisplayName = "Ice Sword",
                    Description = "Smoke-test Ice Sword.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.IceSword,
                    AttackRangeModifier = 2
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-ice-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-ice-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Ice Slash",
                    Description = "Smoke-test Slash for Ice Sword.",
                    DamageValue = 1
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-ice-cost-1") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-ice-cost-1",
                    CardType = CardType.Peach,
                    DisplayName = "Smoke Ice Cost 1",
                    Description = "Smoke-test Ice Sword discard target.",
                    DamageValue = 1
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-ice-cost-2") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-ice-cost-2",
                    CardType = CardType.Dodge,
                    DisplayName = "Smoke Ice Cost 2",
                    Description = "Smoke-test Ice Sword discard target.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioRenwangRed)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-renwang-shield",
                    CardType = CardType.Armor,
                    DisplayName = "Renwang Shield",
                    Description = "Smoke-test Renwang Shield.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.RenwangShield
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-renwang-red-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-renwang-red-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Renwang Red Slash",
                    Description = "Smoke-test red normal Slash against Renwang Shield.",
                    Suit = CardSuit.Heart,
                    Rank = 7,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioRenwangBlack)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-renwang-shield",
                    CardType = CardType.Armor,
                    DisplayName = "Renwang Shield",
                    Description = "Smoke-test Renwang Shield.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.RenwangShield
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-renwang-black-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-renwang-black-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Renwang Black Slash",
                    Description = "Smoke-test black normal Slash against Renwang Shield.",
                    Suit = CardSuit.Spade,
                    Rank = 7,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioGudingBlade)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-guding-blade") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-guding-blade")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-guding-blade",
                    CardType = CardType.Weapon,
                    DisplayName = "Guding Blade",
                    Description = "Smoke-test Guding Blade.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.GudingBlade,
                    AttackRangeModifier = 1
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-guding-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-guding-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Guding Slash",
                    Description = "Smoke-test Slash for Guding Blade.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioSilverLion)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-silver-lion",
                    CardType = CardType.Armor,
                    DisplayName = "Silver Lion",
                    Description = "Smoke-test Silver Lion.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.SilverLion
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-silver-heavy-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-silver-heavy-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Silver Heavy Slash",
                    Description = "Smoke-test high damage Slash against Silver Lion.",
                    DamageValue = 3
                });
            }
        }

        if (_scenario == ScenarioBorrowSwordTakeWeapon)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                foreach (PlayerCharacter opponent in ResolveOpponentCharacters())
                {
                    RemoveAllHandCards(opponent);
                }

                PlayerCharacter? weaponHolder = ResolveOpponentCharacters().FirstOrDefault();
                weaponHolder?.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-borrow-holder-weapon",
                    CardType = CardType.Weapon,
                    DisplayName = "Borrowed Training Blade",
                    Description = "Smoke-test weapon for Borrow Sword.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    AttackRangeModifier = 1
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-borrow-sword") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-borrow-sword",
                    CardType = CardType.BorrowSword,
                    DisplayName = "Smoke Borrow Sword",
                    Description = "Smoke-test Borrow Sword.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioBorrowSwordFireVine)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                foreach (PlayerCharacter opponent in ResolveOpponentCharacters())
                {
                    RemoveAllHandCards(opponent);
                }

                List<PlayerCharacter> opponents = ResolveOpponentCharacters();
                PlayerCharacter? weaponHolder = opponents.FirstOrDefault();
                PlayerCharacter? slashTarget = opponents.Skip(1).FirstOrDefault();
                weaponHolder?.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-borrow-fire-holder-weapon",
                    CardType = CardType.Weapon,
                    DisplayName = "Borrowed Training Blade",
                    Description = "Smoke-test weapon for Borrow Sword.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    AttackRangeModifier = 4
                }, out _);
                slashTarget?.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-borrow-fire-vine",
                    CardType = CardType.Armor,
                    DisplayName = "Vine Armor",
                    Description = "Smoke-test Vine Armor.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.VineArmor
                }, out _);
                weaponHolder?.AddCard(new CardInstance
                {
                    InstanceId = "smoke-borrow-holder-fire-slash",
                    CardType = CardType.FireSlash,
                    DisplayName = "Smoke Borrow Fire Slash",
                    Description = "Smoke-test Fire Slash for Borrow Sword.",
                    Suit = CardSuit.Heart,
                    Rank = 9,
                    DamageValue = 1
                });
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-borrow-fire-vine") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-borrow-fire-vine",
                    CardType = CardType.BorrowSword,
                    DisplayName = "Smoke Borrow Sword",
                    Description = "Smoke-test Borrow Sword with Fire Slash.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioSilverLionLost)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                while (clientCharacter.CurrentHealth > clientCharacter.MaxHealth - 1)
                {
                    clientCharacter.TakeDamage(1);
                }

                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-silver-lion-lost",
                    CardType = CardType.Armor,
                    DisplayName = "Silver Lion",
                    Description = "Smoke-test Silver Lion leaving equipment area.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.SilverLion
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-silver-lion-dismantle") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-silver-lion-dismantle",
                    CardType = CardType.Dismantle,
                    DisplayName = "Smoke Dismantle",
                    Description = "Smoke-test Dismantle for Silver Lion.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioEightDiagramArrows)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-eight-diagram",
                    CardType = CardType.Armor,
                    DisplayName = "Eight Diagram",
                    Description = "Smoke-test Eight Diagram.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.EightDiagram
                }, out _);
                GameManager.Instance?.PushCardToDrawPileTopForTest(new CardInstance
                {
                    InstanceId = "smoke-judge-eight-red",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Red Judgement",
                    Description = "Smoke-test red judgement card for Eight Diagram.",
                    Suit = CardSuit.Heart,
                    Rank = 2,
                    DamageValue = 1
                });
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-eight-arrows") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-eight-arrows",
                    CardType = CardType.ArrowBarrage,
                    DisplayName = "Smoke Arrow Barrage",
                    Description = "Smoke-test Arrow Barrage against Eight Diagram.",
                    DamageValue = 0
                });
            }
        }

        if (_scenario == ScenarioVineSlash)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-vine-armor",
                    CardType = CardType.Armor,
                    DisplayName = "Vine Armor",
                    Description = "Smoke-test Vine Armor.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.VineArmor
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Slash",
                    Description = "Smoke-test normal Slash into Vine Armor.",
                    Suit = CardSuit.Spade,
                    Rank = 7,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioVineFireSlash)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-vine-armor",
                    CardType = CardType.Armor,
                    DisplayName = "Vine Armor",
                    Description = "Smoke-test Vine Armor.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.VineArmor
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-slash",
                    CardType = CardType.FireSlash,
                    DisplayName = "Smoke Fire Slash",
                    Description = "Smoke-test Fire Slash into Vine Armor.",
                    Suit = CardSuit.Heart,
                    Rank = 7,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioQinggangVineSlash)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                clientCharacter.TryEquipCard(new CardInstance
                {
                    InstanceId = "smoke-client-vine-armor",
                    CardType = CardType.Armor,
                    DisplayName = "Vine Armor",
                    Description = "Smoke-test Vine Armor.",
                    EquipmentSlot = EquipmentSlotType.Armor,
                    EquipmentEffect = EquipmentEffectType.VineArmor
                }, out _);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-qinggang") is null
                && hostCharacter.EquippedWeapon?.InstanceId != "smoke-host-qinggang")
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-qinggang",
                    CardType = CardType.Weapon,
                    DisplayName = "Qinggang Sword",
                    Description = "Smoke-test Qinggang Sword.",
                    EquipmentSlot = EquipmentSlotType.Weapon,
                    EquipmentEffect = EquipmentEffectType.QinggangSword,
                    AttackRangeModifier = 1
                });
            }

            if (hostCharacter.FindHandCard("smoke-host-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-slash",
                    CardType = CardType.Slash,
                    DisplayName = "Smoke Slash",
                    Description = "Smoke-test normal Slash with Qinggang Sword.",
                    Suit = CardSuit.Spade,
                    Rank = 7,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioChainFire)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                foreach (PlayerCharacter opponent in ResolveOpponentCharacters())
                {
                    RemoveAllHandCards(opponent);
                    opponent.SetChained(true);
                }

                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-chain-fire-slash") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-chain-fire-slash",
                    CardType = CardType.FireSlash,
                    DisplayName = "Smoke Chain Fire Slash",
                    Description = "Smoke-test Fire Slash through chained characters.",
                    Suit = CardSuit.Heart,
                    Rank = 10,
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioNullifyDismantle)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                RemoveAllHandCards(clientCharacter);
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-nullified-dismantle") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-nullified-dismantle",
                    CardType = CardType.Dismantle,
                    DisplayName = "Smoke Dismantle",
                    Description = "Smoke-test Dismantle cancelled by Nullification.",
                    DamageValue = 0
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-nullification") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-nullification",
                    CardType = CardType.Nullification,
                    DisplayName = "Smoke Nullification",
                    Description = "Smoke-test Nullification.",
                    DamageValue = 0
                });
            }

            if (clientCharacter.FindHandCard("smoke-client-nullify-kept") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-nullify-kept",
                    CardType = CardType.Peach,
                    DisplayName = "Smoke Kept Card",
                    Description = "Smoke-test card that should survive Dismantle.",
                    DamageValue = 1
                });
            }
        }

        if (_scenario == ScenarioNullifyArrowTarget)
        {
            if (!_preconditionApplied)
            {
                RemoveAllHandCards(hostCharacter);
                List<PlayerCharacter> opponents = ResolveOpponentCharacters();
                foreach (PlayerCharacter opponent in opponents)
                {
                    RemoveAllHandCards(opponent);
                    RemoveCardsOfType(opponent, CardType.Dodge);
                    RemoveCardsOfType(opponent, CardType.Peach);
                }

                PlayerCharacter? firstTarget = opponents.FirstOrDefault();
                firstTarget?.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-nullification",
                    CardType = CardType.Nullification,
                    DisplayName = "Smoke Nullification",
                    Description = "Smoke-test Nullification for one Arrow Barrage target.",
                    DamageValue = 0
                });
                _preconditionApplied = true;
            }

            if (hostCharacter.FindHandCard("smoke-host-nullified-arrows") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-nullified-arrows",
                    CardType = CardType.ArrowBarrage,
                    DisplayName = "Smoke Arrow Barrage",
                    Description = "Smoke-test per-target Nullification for Arrow Barrage.",
                    DamageValue = 0
                });
            }
        }

        if ((_scenario == ScenarioDodge || _scenario == ScenarioSlashLimit || _scenario == ScenarioWeaponRange || _scenario == ScenarioStoneAxe || _scenario == ScenarioGreenDragon) && clientCharacter.FindHandCard("smoke-client-dodge") is null)
        {
            clientCharacter.AddCard(new CardInstance
            {
                InstanceId = "smoke-client-dodge",
                CardType = CardType.Dodge,
                DisplayName = "Smoke Dodge",
                Description = "Smoke-test Dodge.",
                DamageValue = 0
            });
        }

        if (_scenario == ScenarioPeachSave)
        {
            RemoveCardsOfType(hostCharacter, CardType.Peach);
            RemoveCardsOfType(clientCharacter, CardType.Dodge);

            if (clientCharacter.FindHandCard("smoke-client-peach") is null)
            {
                clientCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-client-peach",
                    CardType = CardType.Peach,
                    DisplayName = "Smoke Peach",
                    Description = "Smoke-test Peach.",
                    DamageValue = 1
                });
            }

            if (!_preconditionApplied)
            {
                while (clientCharacter.CurrentHealth > 1)
                {
                    clientCharacter.TakeDamage(1);
                }

                _preconditionApplied = true;
            }
        }

        if (_scenario == ScenarioPeachRescue)
        {
            RemoveCardsOfType(clientCharacter, CardType.Dodge);
            RemoveCardsOfType(clientCharacter, CardType.Peach);
            RemoveCardsOfType(hostCharacter, CardType.Peach);

            if (hostCharacter.FindHandCard("smoke-host-peach") is null)
            {
                hostCharacter.AddCard(new CardInstance
                {
                    InstanceId = "smoke-host-peach",
                    CardType = CardType.Peach,
                    DisplayName = "Smoke Peach",
                    Description = "Smoke-test rescue Peach.",
                    DamageValue = 1
                });
            }

            if (!_preconditionApplied)
            {
                while (clientCharacter.CurrentHealth > 1)
                {
                    clientCharacter.TakeDamage(1);
                }

                _preconditionApplied = true;
            }
        }

        if (_scenario == ScenarioDeathNoSave || IsIdentityVictoryScenario)
        {
            RemoveCardsOfType(hostCharacter, CardType.Peach);
            RemoveCardsOfType(clientCharacter, CardType.Dodge);
            RemoveCardsOfType(clientCharacter, CardType.Peach);

            if (!_preconditionApplied)
            {
                while (clientCharacter.CurrentHealth > 1)
                {
                    clientCharacter.TakeDamage(1);
                }

                _preconditionApplied = true;
            }
        }

        if (_scenario == ScenarioWeaponRange)
        {
            RemoveCardsOfType(hostCharacter, CardType.Peach);
            RemoveCardsOfType(clientCharacter, CardType.Peach);
        }

        GD.Print($"[host] Smoke test state injected for scenario '{_scenario}'.");
    }

    private List<PlayerCharacter> GetCharacters()
    {
        return GetTree()
            .GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .OrderBy(character => character.OwnerPeerId)
            .ToList();
    }

    private PlayerCharacter? ResolveCharacter(int peerId)
    {
        return GetTree()
            .GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .FirstOrDefault(character => character.OwnerPeerId == peerId);
    }

    private PlayerCharacter? ResolveOpponentCharacter()
    {
        int localPeerId = LocalPeerId;
        return GetTree()
            .GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .FirstOrDefault(character => character.OwnerPeerId != localPeerId);
    }

    private List<PlayerCharacter> ResolveOpponentCharacters()
    {
        int localPeerId = LocalPeerId;
        return GetTree()
            .GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .Where(character => character.OwnerPeerId != localPeerId && character.IsAlive)
            .OrderBy(character => character.OwnerPeerId)
            .ToList();
    }

    private void ParseArguments()
    {
        Dictionary<string, string> args = OS.GetCmdlineUserArgs()
            .Select(arg => arg.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(parts => parts[0][2..], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (args.TryGetValue("role", out string? role) && !string.IsNullOrWhiteSpace(role))
        {
            _role = role.Trim().ToLowerInvariant();
        }

        if (args.TryGetValue("address", out string? address) && !string.IsNullOrWhiteSpace(address))
        {
            _address = address.Trim();
        }

        if (args.TryGetValue("scenario", out string? scenario) && !string.IsNullOrWhiteSpace(scenario))
        {
            _scenario = scenario.Trim().ToLowerInvariant();
        }

        if (args.TryGetValue("port", out string? portText) && int.TryParse(portText, out int port) && port > 0)
        {
            _port = port;
        }

        if (args.TryGetValue("timeout", out string? timeoutText) && double.TryParse(timeoutText, out double timeout) && timeout > 1d)
        {
            _timeoutSeconds = timeout;
        }

        if (args.TryGetValue("client-index", out string? clientIndexText)
            && int.TryParse(clientIndexText, out int clientIndex)
            && clientIndex > 0)
        {
            _clientIndex = clientIndex;
        }
    }

    private static void RemoveCardsOfType(PlayerCharacter character, CardType cardType)
    {
        foreach (string instanceId in character.HandCards
                     .Where(card => card.CardType == cardType)
                     .Select(card => card.InstanceId)
                     .ToList())
        {
            character.TryConsumeHandCardByInstanceId(instanceId, out _);
        }
    }

    private static void RemoveAllHandCards(PlayerCharacter character)
    {
        foreach (string instanceId in character.HandCards
                     .Select(card => card.InstanceId)
                     .ToList())
        {
            character.TryConsumeHandCardByInstanceId(instanceId, out _);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private void Complete(string message)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        GD.Print($"[{_role}] {SuccessMarker}: {message}");
        LanMultiplayerManager.Instance?.LeaveSession();
        GetTree().Quit(0);
    }

    private void ScheduleSuccess(double delaySeconds, string message)
    {
        if (_completed || _scheduledSuccessAt >= 0d)
        {
            return;
        }

        delaySeconds = IsHostRole
            ? Math.Max(delaySeconds, HostSuccessGraceSeconds)
            : delaySeconds;
        _scheduledSuccessMessage = message;
        _scheduledSuccessAt = _elapsedSeconds + Math.Max(0d, delaySeconds);
        GD.Print($"[{_role}] Success scheduled in {delaySeconds:F2}s: {message}");
        if (IsHostRole)
        {
            File.WriteAllText(_successSignalPath, message);
        }
    }

    private void Fail(string message)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        GD.PushError($"[{_role}] Smoke test failed: {message}");
        if (GameManager.Instance is { } game)
        {
            int diagnosticPeerId = ResolveDiagnosticLocalPeerId();
            PlayerCharacter? localCharacter = ResolveCharacter(diagnosticPeerId);
            string localCards = localCharacter is null
                ? "<character unavailable>"
                : string.Join(",", localCharacter.HandCards.Select(card => $"{card.InstanceId}:{card.CardType}"));
            GD.PushError($"[{_role}] Diagnostic: localPeer={diagnosticPeerId}, pendingPeer={game.PendingResponsePeerId}, pendingKind={game.PendingResponseKind}, prompt='{game.PendingResponsePrompt}', localCards=[{localCards}].");
            if (IsHostRole)
            {
                foreach (PlayerCharacter character in game.GetAliveCharacters().OrderBy(character => character.OwnerPeerId))
                {
                    string cards = string.Join(",", character.HandCards.Select(card => $"{card.InstanceId}:{card.CardType}"));
                    GD.PushError($"[host] Character diagnostic: peer={character.OwnerPeerId}, cards=[{cards}].");
                }

                string logTail = string.Join(" | ", game.BattleLog.TakeLast(8).Select(entry => entry.Message));
                GD.PushError($"[host] Battle log tail: {logTail}");
            }
        }

        LanMultiplayerManager.Instance?.LeaveSession();
        GetTree().Quit(1);
    }

    private int ResolveDiagnosticLocalPeerId()
    {
        if (IsHostRole)
        {
            return LanMultiplayerManager.Instance?.HostPeerId ?? 1;
        }

        string expectedName = $"SmokeClient{_clientIndex}";
        return LanMultiplayerManager.Instance?.Players.Values
            .FirstOrDefault(player => string.Equals(player.PlayerName, expectedName, StringComparison.Ordinal))?.PeerId ?? 0;
    }
}
