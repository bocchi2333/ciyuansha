using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Content;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Networking;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Networking;

/// <summary>
/// 局域网联机主控。
/// 建议配置为 Autoload，并在所有联机参与方保持相同节点路径。
/// </summary>
public partial class LanMultiplayerManager : Node
{
    public static LanMultiplayerManager? Instance { get; private set; }

    [Export]
    public int Port { get; set; } = 24567;

    [Export]
    public int MaxClients { get; set; } = 8;

    [Export]
    public string BindIp { get; set; } = "*";

    public LanSessionState SessionState { get; private set; } = LanSessionState.Offline;

    public IReadOnlyDictionary<int, LanPlayerInfo> Players => _players;

    public int TransportPeerId => IsConnected ? Multiplayer.GetUniqueId() : 0;

    public int LocalPeerId => ResolveLocalLogicalPeerId();

    public int HostPeerId { get; private set; } = 1;

    public string SelectedModeId { get; private set; } = BuiltInModeIds.Duel;

    public BotDifficulty SelectedBotDifficulty { get; private set; } = BotDifficulty.Standard;

    public bool JoinAsSpectator { get; private set; }

    public string LocalReconnectToken { get; private set; } = string.Empty;

    public new bool IsConnected => SessionState != LanSessionState.Offline
        && Multiplayer.MultiplayerPeer is not null;

    public bool IsHost => IsConnected && Multiplayer.IsServer();

    public string LocalPlayerName { get; private set; } = "Player";

    public string LocalCharacterId { get; private set; } = string.Empty;

    public string LastJoinAddress { get; private set; } = string.Empty;

    public bool CanReconnect => SessionState == LanSessionState.Offline
        && !string.IsNullOrWhiteSpace(LastJoinAddress)
        && !string.IsNullOrWhiteSpace(LocalPlayerName);

    public string LastNetworkMessage { get; private set; } = string.Empty;

    public MatchStateSnapshot? LastMatchStateSnapshot { get; private set; }

    public JournalDeltaV2? LastJournalDelta { get; private set; }

    public event Action<LanSessionState>? OnSessionStateChanged;

    public event Action<IReadOnlyDictionary<int, LanPlayerInfo>>? OnLobbyChanged;

    public event Action<string>? OnNetworkMessage;

    public event Action<string>? OnNetworkError;

    public event Action<LanDisconnectNotice>? OnUnexpectedDisconnect;

    public override void _Ready()
    {
        if (Instance is not null && Instance != this)
        {
            GD.PushWarning("Duplicate LanMultiplayerManager detected. The newer instance will be freed.");
            QueueFree();
            return;
        }

        Instance = this;
        Multiplayer.PeerConnected += HandlePeerConnected;
        Multiplayer.PeerDisconnected += HandlePeerDisconnected;
        Multiplayer.ConnectedToServer += HandleConnectedToServer;
        Multiplayer.ConnectionFailed += HandleConnectionFailed;
        Multiplayer.ServerDisconnected += HandleServerDisconnected;
        TryBindGameManager();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Multiplayer.PeerConnected -= HandlePeerConnected;
            Multiplayer.PeerDisconnected -= HandlePeerDisconnected;
            Multiplayer.ConnectedToServer -= HandleConnectedToServer;
            Multiplayer.ConnectionFailed -= HandleConnectionFailed;
            Multiplayer.ServerDisconnected -= HandleServerDisconnected;
            UnbindGameManager();
            Instance = null;
        }
    }

    public Error HostGame(string playerName, string characterId = "")
    {
        LeaveSession();

        LocalPlayerName = NormalizePlayerName(playerName);
        LocalCharacterId = characterId ?? string.Empty;

        ENetMultiplayerPeer peer = new();

        Error error = peer.CreateServer(Port, MaxClients);
        if (error != Error.Ok)
        {
            ReportNetworkError($"Failed to host LAN session on UDP port {Port}: {error}.");
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        HostPeerId = 1;
        LocalReconnectToken = ReconnectTokenFactory.Create();
        SetSessionState(LanSessionState.Hosting);

        _players.Clear();
        _players[HostPeerId] = new LanPlayerInfo
        {
            PlayerId = $"host-{Guid.NewGuid():N}",
            PeerId = HostPeerId,
            SeatId = 1,
            TransportPeerId = HostPeerId,
            PlayerName = LocalPlayerName,
            CharacterId = LocalCharacterId,
            IsReady = true,
            IsHost = true,
            IsConnected = true
        };
        _reservedSeatTokens[HostPeerId] = LocalReconnectToken;

        ReportNetworkInfo($"Hosting LAN room on UDP port {Port}.");
        BroadcastLobbySnapshot();
        SetSessionState(LanSessionState.InLobby);
        return Error.Ok;
    }

    public Error JoinGame(string address, string playerName, string characterId = "", bool joinAsSpectator = false)
    {
        CloseSession(clearReconnect: false);

        LocalPlayerName = NormalizePlayerName(playerName);
        LocalCharacterId = characterId ?? string.Empty;
        JoinAsSpectator = joinAsSpectator;
        LastJoinAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();

        ENetMultiplayerPeer peer = new();
        Error error = peer.CreateClient(LastJoinAddress, Port);
        if (error != Error.Ok)
        {
            string message = $"Failed to join LAN session at {LastJoinAddress}:{Port}: {error}.";
            ReportNetworkError(message);
            RaiseUnexpectedDisconnect(LanDisconnectReason.ConnectionFailed, message, wasInMatch: false);
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        SetSessionState(LanSessionState.Joining);
        return Error.Ok;
    }

    public Error ReconnectLastSession()
    {
        if (!CanReconnect)
        {
            const string message = "No previous LAN room is available to reconnect.";
            ReportNetworkError(message);
            return Error.InvalidData;
        }

        return JoinGame(LastJoinAddress, LocalPlayerName, LocalCharacterId, JoinAsSpectator);
    }

    public void LeaveSession()
    {
        CloseSession(clearReconnect: true);
    }

    private void CloseSession(bool clearReconnect)
    {
        _isClosingSession = true;
        try
        {
            if (Multiplayer.MultiplayerPeer is not null)
            {
                Multiplayer.MultiplayerPeer.Close();
                Multiplayer.MultiplayerPeer = null;
            }

            _players.Clear();
            _transportPeerAliases.Clear();
            HostPeerId = 1;
            LastMatchStateSnapshot = null;
            LastJournalDelta = null;
            LastNetworkMessage = string.Empty;
            if (clearReconnect)
            {
                LastJoinAddress = string.Empty;
                LocalReconnectToken = string.Empty;
                _reservedSeatTokens.Clear();
            }

            GameManager.Instance?.EndMatch();
            SetSessionState(LanSessionState.Offline);
            RaiseLobbyChanged();
        }
        finally
        {
            _isClosingSession = false;
        }
    }

    public void SetLocalReady(bool isReady)
    {
        if (!IsConnected)
        {
            return;
        }

        SubmitLobbyCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.ReadyState,
            IsReady = isReady
        });
    }

    public void SelectLocalCharacter(string characterId)
    {
        LocalCharacterId = characterId ?? string.Empty;

        if (!IsConnected)
        {
            return;
        }

        SubmitLobbyCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.ChooseGeneral,
            GeneralId = LocalCharacterId
        });
    }

    public bool SetMode(string modeId)
    {
        if (!IsHost || GameManager.Instance?.CoreRuntime is not GameCoreRuntime runtime)
        {
            return false;
        }
        if (!new GameModeRegistry().Modes.ContainsKey(modeId))
        {
            ReportNetworkError($"Unknown game mode: {modeId}.");
            return false;
        }
        SelectedModeId = modeId;
        RemoveAutomaticBossSeats();
        if (modeId == BuiltInModeIds.Boss)
        {
            AddBossSeat(runtime);
        }
        BroadcastLobbySnapshot();
        return true;
    }

    public bool AddBot(string characterId = "", BotDifficulty? difficulty = null)
    {
        if (!IsHost || SessionState != LanSessionState.InLobby)
        {
            return false;
        }
        int peerId = _nextBotPeerId++;
        _players[peerId] = new LanPlayerInfo
        {
            PlayerId = $"bot-{Guid.NewGuid():N}",
            PeerId = peerId,
            SeatId = NextSeatId(),
            PlayerName = $"Bot {_players.Values.Count(player => player.IsBot && !player.IsBoss) + 1}",
            CharacterId = characterId ?? string.Empty,
            IsReady = true,
            IsConnected = true,
            IsBot = true,
            BotDifficulty = difficulty ?? SelectedBotDifficulty
        };
        BroadcastLobbySnapshot();
        return true;
    }

    public bool RemoveBot(int peerId)
    {
        if (!IsHost || !_players.TryGetValue(peerId, out LanPlayerInfo? player) || !player.IsBot || player.IsBoss)
        {
            return false;
        }
        _players.Remove(peerId);
        BroadcastLobbySnapshot();
        return true;
    }

    public void SetBotDifficulty(BotDifficulty difficulty)
    {
        if (!IsHost)
        {
            return;
        }
        SelectedBotDifficulty = difficulty;
        foreach (LanPlayerInfo bot in _players.Values.Where(player => player.IsBot))
        {
            bot.BotDifficulty = difficulty;
        }
        BroadcastLobbySnapshot();
    }

    public void RequestReconnectState()
    {
        if (!IsConnected)
        {
            return;
        }

        SubmitLobbyCommand(new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.ReconnectRequest
        });
        if (!IsHost)
        {
            RpcId(1, MethodName.RequestJournalDeltaV2Rpc, LastMatchStateSnapshot?.JournalCursor ?? 0, LocalReconnectToken);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestJournalDeltaV2Rpc(long afterCursor, string reconnectToken)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }
        int senderId = ResolveLogicalPeerId(Multiplayer.GetRemoteSenderId());
        if (!_players.TryGetValue(senderId, out LanPlayerInfo? player)
            || !_reservedSeatTokens.TryGetValue(senderId, out string? expected)
            || !ReconnectTokenFactory.FixedTimeEquals(expected, reconnectToken))
        {
            ReportNetworkError("Rejected journal synchronization with an invalid seat token.");
            return;
        }
        RuleJournalDelta? delta = GameManager.Instance?.CoreRuntime?.BuildDelta(afterCursor);
        if (delta is null)
        {
            SendCurrentMatchToPeerIfNeeded(senderId);
            return;
        }
        string stateHash = GameManager.Instance?.CoreRuntime?.Engine?.State.ComputeCanonicalHash() ?? string.Empty;
        JournalDeltaV2 response = new(delta.FromCursor, delta.ToCursor, delta.Entries, stateHash);
        RpcId(GetTransportPeerIdForPlayer(player), MethodName.ReceiveJournalDeltaV2Rpc, JsonSerializer.Serialize(response));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveJournalDeltaV2Rpc(string deltaJson)
    {
        JournalDeltaV2? delta = JsonSerializer.Deserialize<JournalDeltaV2>(deltaJson);
        if (delta is null || delta.FromCursor < 0 || delta.ToCursor < delta.FromCursor)
        {
            ReportNetworkError("Rejected an invalid journal delta.");
            return;
        }
        LastJournalDelta = delta;
        ReportNetworkInfo($"Received rule journal delta {delta.FromCursor}..{delta.ToCursor}.");
    }

    public bool CanStartMatch()
    {
        if (!IsHost || _players.Count == 0)
        {
            return false;
        }

        List<LanPlayerInfo> connectedPlayers = _players.Values
            .Where(player => player.IsConnected && !player.IsSpectator)
            .ToList();
        if (!new GameModeRegistry().Modes.TryGetValue(SelectedModeId, out IGameMode? mode)
            || connectedPlayers.Count < mode.MinimumPlayers
            || connectedPlayers.Count > mode.MaximumPlayers)
        {
            return false;
        }

        return connectedPlayers
            .Where(player => !player.IsHost)
            .All(player => player.IsReady);
    }

    public void StartMatch()
    {
        if (!IsHost)
        {
            return;
        }

        if (!CanStartMatch())
        {
            ReportNetworkError("Cannot start match until every player is ready.");
            return;
        }

        StartMatchInternal();
    }

    public void ReturnToLobby()
    {
        if (!IsHost)
        {
            return;
        }

        if (GameManager.Instance?.IsMatchRunning == true)
        {
            GameManager.Instance.EndMatch(0, "The host returned the match to lobby.");
        }

        foreach (int peerId in _players.Where(entry => !entry.Value.IsConnected && !entry.Value.IsHost).Select(entry => entry.Key).ToList())
        {
            _players.Remove(peerId);
        }

        foreach (LanPlayerInfo player in _players.Values)
        {
            player.IsReady = false;
        }

        LastMatchStateSnapshot = null;
        Rpc(MethodName.ReturnToLobbyRpc);
        BroadcastLobbySnapshot();
    }

    public bool SubmitPlayCommand(NetworkPlayCommand command)
    {
        if (!IsConnected || command is null)
        {
            return false;
        }

        if (command.CommandType is NetworkPlayCommandType.ChooseGeneral or NetworkPlayCommandType.ReadyState or NetworkPlayCommandType.ReconnectRequest)
        {
            return SubmitLobbyCommand(command);
        }

        if (IsHost)
        {
            return GameManager.Instance?.TryHandleNetworkPlayCommand(LocalPeerId, command) == true;
        }

        SubmitChoiceWirePayloadV2 payload = new(null, LocalReconnectToken, command);
        NetworkEnvelopeV2 envelope = new(
            ProtocolV2.Version,
            NetworkMessageKindV2.SubmitChoice,
            LastMatchStateSnapshot?.MatchId ?? string.Empty,
            ++_nextClientSequence,
            JsonSerializer.Serialize(payload));
        if (OS.IsDebugBuild())
        {
            GD.Print($"[network] Sending {command.CommandType} from transport peer {Multiplayer.GetUniqueId()} to host 1.");
        }

        Error rpcError = RpcId(1, MethodName.SubmitEnvelopeV2Rpc, JsonSerializer.Serialize(envelope));
        if (rpcError != Error.Ok)
        {
            ReportNetworkError($"Failed to submit {command.CommandType} to the host: {rpcError}.");
            return false;
        }

        return true;
    }

    public bool SubmitChoice(ChoiceResult result)
    {
        if (!IsConnected || result is null || JoinAsSpectator)
        {
            return false;
        }
        if (IsHost)
        {
            EngineStepResult? step = GameManager.Instance?.SubmitCoreChoice(LocalPeerId, LocalReconnectToken, result);
            return step is not null && step.Progress != EngineProgress.Rejected;
        }

        SubmitChoiceWirePayloadV2 payload = new(result, LocalReconnectToken);
        NetworkEnvelopeV2 envelope = new(
            ProtocolV2.Version,
            NetworkMessageKindV2.SubmitChoice,
            LastMatchStateSnapshot?.MatchId ?? string.Empty,
            ++_nextClientSequence,
            JsonSerializer.Serialize(payload));
        Error error = RpcId(1, MethodName.SubmitEnvelopeV2Rpc, JsonSerializer.Serialize(envelope));
        if (error != Error.Ok)
        {
            ReportNetworkError($"Failed to submit protocol V2 choice: {error}.");
            return false;
        }
        return true;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    private void SubmitEnvelopeV2Rpc(string envelopeJson)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }
        NetworkEnvelopeV2? envelope = JsonSerializer.Deserialize<NetworkEnvelopeV2>(envelopeJson);
        if (envelope is null
            || envelope.ProtocolVersion != ProtocolV2.Version
            || envelope.MessageKind != NetworkMessageKindV2.SubmitChoice)
        {
            ReportNetworkError("Rejected an invalid or incompatible protocol V2 envelope.");
            return;
        }
        int senderId = ResolveLogicalPeerId(Multiplayer.GetRemoteSenderId());
        if (!_players.TryGetValue(senderId, out LanPlayerInfo? sender) || sender.IsSpectator)
        {
            ReportNetworkError("A spectator or unknown peer attempted to submit a match choice.");
            return;
        }
        SubmitChoiceWirePayloadV2? payload = JsonSerializer.Deserialize<SubmitChoiceWirePayloadV2>(envelope.PayloadJson);
        if (payload is null
            || !_reservedSeatTokens.TryGetValue(senderId, out string? expectedToken)
            || !ReconnectTokenFactory.FixedTimeEquals(expectedToken, payload.ReconnectToken))
        {
            ReportNetworkError("Rejected a match choice with an invalid seat token.");
            return;
        }

        if (payload.Result is ChoiceResult choice)
        {
            EngineStepResult step = GameManager.Instance?.SubmitCoreChoice(sender.SeatId, payload.ReconnectToken, choice)
                ?? new EngineStepResult(EngineProgress.Rejected, 0, Array.Empty<CiyuanSha.GameCore.Events.RuleEvent>(), null, string.Empty, "engine.unavailable");
            if (step.Progress == EngineProgress.Rejected)
            {
                ReportNetworkError($"Choice rejected: {step.ErrorKey}.");
            }
            return;
        }

        // Temporary rendering bridge: old controls still describe the selected
        // action, but this branch remains host-validated and travels only inside
        // the V2 SubmitChoice envelope.
        if (payload.PresentationCommand is NetworkPlayCommand presentationCommand)
        {
            bool handled = GameManager.Instance?.TryHandleNetworkPlayCommand(senderId, presentationCommand) == true;
            if (!handled)
            {
                ReportNetworkError("The host rejected an illegal presentation-adapter action.");
            }
        }
    }

    public string[] GetLocalLanAddresses()
    {
        return IP.GetLocalAddresses()
            .Where(address => address.Contains('.'))
            .Where(address => address != "127.0.0.1")
            .ToArray();
    }

    public void RequestMatchSnapshotBroadcast()
    {
        BroadcastMatchSnapshotIfHost();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegisterLobbyPlayerRpc(string handshakeJson, string characterId, bool isReady)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        int transportPeerId = Multiplayer.GetRemoteSenderId();
        HandshakeRequestV2? handshake = JsonSerializer.Deserialize<HandshakeRequestV2>(handshakeJson);
        if (handshake is null)
        {
            SendHandshakeFailure(transportPeerId, "network.invalid_handshake");
            return;
        }
        IReadOnlyList<ContentPackReference> packs = GameManager.Instance?.CoreRuntime?.ContentPacks
            ?? Array.Empty<ContentPackReference>();
        ProtocolValidationResult protocolValidation = ProtocolV2Validator.ValidateHandshake(handshake, packs);
        if (!protocolValidation.IsValid)
        {
            SendHandshakeFailure(transportPeerId, protocolValidation.ErrorKey);
            return;
        }

        string normalizedName = NormalizePlayerName(handshake.PlayerName);
        int logicalPeerId = ResolveLogicalPeerIdForRegistration(transportPeerId, handshake.ReconnectToken);
        if (logicalPeerId != transportPeerId)
        {
            _transportPeerAliases[transportPeerId] = logicalPeerId;
        }

        string seatToken;
        if (!_reservedSeatTokens.TryGetValue(logicalPeerId, out string? reservedToken))
        {
            seatToken = ReconnectTokenFactory.Create();
            _reservedSeatTokens[logicalPeerId] = seatToken;
        }
        else
        {
            seatToken = reservedToken;
        }

        bool isSpectator = handshake.JoinAsSpectator;
        int seatId = isSpectator ? 0 : ResolveSeatIdForRegistration(logicalPeerId);

        _players[logicalPeerId] = new LanPlayerInfo
        {
            PlayerId = _players.TryGetValue(logicalPeerId, out LanPlayerInfo? existing) && existing.PlayerId.Length > 0
                ? existing.PlayerId
                : $"player-{Guid.NewGuid():N}",
            PeerId = logicalPeerId,
            SeatId = seatId,
            TransportPeerId = transportPeerId,
            PlayerName = normalizedName,
            CharacterId = characterId ?? string.Empty,
            IsReady = isReady,
            IsHost = logicalPeerId == HostPeerId,
            IsConnected = true,
            IsSpectator = isSpectator
        };

        HandshakeResultV2 result = new(true, string.Empty, seatId, seatToken, GameManager.Instance?.CoreRuntime?.Engine?.State.Revision ?? 0);
        RpcId(transportPeerId, MethodName.ReceiveHandshakeResultRpc, JsonSerializer.Serialize(result));

        ReportNetworkInfo(logicalPeerId == transportPeerId
            ? $"{normalizedName} joined as peer {logicalPeerId}."
            : $"{normalizedName} reconnected to reserved peer {logicalPeerId}.");
        BroadcastLobbySnapshot();
        SendCurrentMatchToPeerIfNeeded(logicalPeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHandshakeResultRpc(string resultJson)
    {
        HandshakeResultV2? result = JsonSerializer.Deserialize<HandshakeResultV2>(resultJson);
        if (result is null || !result.Accepted)
        {
            string error = result?.ErrorKey ?? "network.invalid_handshake_result";
            ReportNetworkError($"Protocol V2 handshake rejected: {error}.");
            CloseSession(clearReconnect: false);
            return;
        }
        LocalReconnectToken = result.ReconnectToken;
        ReportNetworkInfo($"Protocol V2 handshake accepted for seat {result.AssignedSeatId}.");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetReadyStateRpc(bool isReady)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        int senderId = ResolveLogicalPeerId(Multiplayer.GetRemoteSenderId());
        if (_players.TryGetValue(senderId, out LanPlayerInfo? playerInfo))
        {
            playerInfo.IsReady = isReady;
            BroadcastLobbySnapshot();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SelectCharacterRpc(string characterId)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        int senderId = ResolveLogicalPeerId(Multiplayer.GetRemoteSenderId());
        if (_players.TryGetValue(senderId, out LanPlayerInfo? playerInfo))
        {
            playerInfo.CharacterId = characterId ?? string.Empty;
            BroadcastLobbySnapshot();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLobbySnapshotRpc(string snapshotJson)
    {
        LobbySnapshot? snapshot = JsonSerializer.Deserialize<LobbySnapshot>(snapshotJson);
        if (snapshot is null)
        {
            ReportNetworkError("Failed to deserialize lobby snapshot.");
            return;
        }

        if (snapshot.ProtocolVersion != ProtocolV2.Version
            || !string.Equals(snapshot.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
        {
            ReportNetworkError("Rejected lobby snapshot from an incompatible protocol or engine version.");
            return;
        }

        SelectedModeId = snapshot.ModeId;

        ApplyLobbyPlayers(snapshot.HostPeerId, snapshot.Players);

        if (SessionState != LanSessionState.InMatch)
        {
            SetSessionState(LanSessionState.InLobby);
        }

        RaiseLobbyChanged();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMatchRpc(string matchStartInfoJson)
    {
        MatchStartInfo? matchStartInfo = JsonSerializer.Deserialize<MatchStartInfo>(matchStartInfoJson);
        if (matchStartInfo is null)
        {
            ReportNetworkError("Failed to deserialize match start info.");
            return;
        }

        if (matchStartInfo.ProtocolVersion != ProtocolV2.Version
            || !string.Equals(matchStartInfo.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
        {
            ReportNetworkError("Rejected match start from an incompatible protocol or engine version.");
            return;
        }
        ProtocolValidationResult packValidation = ProtocolV2Validator.ValidateHandshake(
            new HandshakeRequestV2(
                ProtocolV2.Version,
                ProtocolV2.EngineApiVersion,
                LocalPlayerName,
                LocalReconnectToken,
                JoinAsSpectator,
                GameManager.Instance?.CoreRuntime?.ContentPacks ?? Array.Empty<ContentPackReference>()),
            matchStartInfo.ContentPacks);
        if (!packValidation.IsValid)
        {
            ReportNetworkError($"Cannot start match: {packValidation.ErrorKey}.");
            return;
        }

        SetSessionState(LanSessionState.InMatch);

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.BeginMatch(matchStartInfo.TurnOrder, matchStartInfo.StartingPeerId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMatchStateSnapshotRpc(string snapshotJson)
    {
        MatchStateSnapshot? snapshot = JsonSerializer.Deserialize<MatchStateSnapshot>(snapshotJson);
        if (snapshot is null)
        {
            ReportNetworkError("Failed to deserialize match state snapshot.");
            return;
        }

        if (snapshot.ProtocolVersion != ProtocolV2.Version
            || !string.Equals(snapshot.EngineApiVersion, ProtocolV2.EngineApiVersion, StringComparison.Ordinal))
        {
            ReportNetworkError("Rejected match snapshot from an incompatible protocol or engine version.");
            return;
        }

        LastMatchStateSnapshot = snapshot;
        if (snapshot.LobbyPlayers.Count > 0)
        {
            int hostPeerId = snapshot.LobbyPlayers.FirstOrDefault(player => player.IsHost)?.PeerId ?? HostPeerId;
            ApplyLobbyPlayers(hostPeerId, snapshot.LobbyPlayers);
            RaiseLobbyChanged();
        }

        if (snapshot.IsMatchRunning && SessionState != LanSessionState.InMatch)
        {
            SetSessionState(LanSessionState.InMatch);
        }

        GameManager.Instance?.ApplyMatchStateSnapshot(snapshot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReturnToLobbyRpc()
    {
        LastMatchStateSnapshot = null;
        GameManager.Instance?.EndMatch();
        SetSessionState(LanSessionState.InLobby);
        RaiseLobbyChanged();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    private void SubmitPlayCommandRpc(string commandJson)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        NetworkPlayCommand? command = JsonSerializer.Deserialize<NetworkPlayCommand>(commandJson);
        if (command is null)
        {
            ReportNetworkError("Failed to deserialize network play command.");
            return;
        }

        int senderId = ResolveLogicalPeerId(Multiplayer.GetRemoteSenderId());
        if (command.CommandType is NetworkPlayCommandType.ChooseGeneral or NetworkPlayCommandType.ReadyState or NetworkPlayCommandType.ReconnectRequest)
        {
            HandleLobbyCommand(senderId, command);
            return;
        }

        ReportNetworkError("Protocol V1 in-match commands are no longer accepted; use SubmitChoice V2.");
    }

    private readonly Dictionary<int, LanPlayerInfo> _players = new();

    private readonly Dictionary<int, int> _transportPeerAliases = new();

    private readonly Dictionary<int, string> _reservedSeatTokens = new();

    private long _nextClientSequence;

    private int _nextBotPeerId = 10000;

    private bool _isClosingSession;

    private void HandlePeerConnected(long id)
    {
        if (Multiplayer.IsServer())
        {
            ReportNetworkInfo($"Peer {id} connected.");
        }
    }

    private void HandlePeerDisconnected(long id)
    {
        int transportPeerId = (int)id;
        if (!Multiplayer.IsServer())
        {
            RaiseLobbyChanged();
            return;
        }

        LanPlayerInfo? playerInfo = FindPlayerByTransportPeerId(transportPeerId);
        if (playerInfo is null)
        {
            _transportPeerAliases.Remove(transportPeerId);
            RaiseLobbyChanged();
            return;
        }

        _transportPeerAliases.Remove(transportPeerId);
        if (SessionState == LanSessionState.InMatch && !playerInfo.IsHost)
        {
            playerInfo.IsConnected = false;
            playerInfo.IsReady = false;
            playerInfo.TransportPeerId = 0;
            ReportNetworkInfo($"{playerInfo.PlayerName} disconnected. Their seat is reserved for reconnect.");
            BroadcastLobbySnapshot();
            BroadcastMatchSnapshotIfHost();
            return;
        }

        if (!playerInfo.IsHost)
        {
            _players.Remove(playerInfo.PeerId);
        }

        ReportNetworkInfo($"{playerInfo.PlayerName} left the lobby.");
        BroadcastLobbySnapshot();
    }

    private bool SubmitLobbyCommand(NetworkPlayCommand command)
    {
        if (!IsConnected || command is null)
        {
            return false;
        }

        if (IsHost)
        {
            return HandleLobbyCommand(HostPeerId, command);
        }

        string commandJson = JsonSerializer.Serialize(command);
        Error rpcError = RpcId(1, MethodName.SubmitPlayCommandRpc, commandJson);
        if (rpcError != Error.Ok)
        {
            ReportNetworkError($"Failed to submit {command.CommandType} to the host: {rpcError}.");
            return false;
        }

        return true;
    }

    private bool HandleLobbyCommand(int peerId, NetworkPlayCommand command)
    {
        if (!Multiplayer.IsServer() || command is null)
        {
            return false;
        }

        if (!_players.TryGetValue(peerId, out LanPlayerInfo? playerInfo))
        {
            return false;
        }

        switch (command.CommandType)
        {
            case NetworkPlayCommandType.ChooseGeneral:
                playerInfo.CharacterId = command.GeneralId ?? string.Empty;
                if (playerInfo.IsHost)
                {
                    playerInfo.IsReady = true;
                }

                if (peerId == HostPeerId)
                {
                    LocalCharacterId = playerInfo.CharacterId;
                }

                BroadcastLobbySnapshot();
                return true;

            case NetworkPlayCommandType.ReadyState:
                playerInfo.IsReady = command.IsReady;
                BroadcastLobbySnapshot();
                return true;

            case NetworkPlayCommandType.ReconnectRequest:
                BroadcastLobbySnapshot();
                SendCurrentMatchToPeerIfNeeded(peerId);
                return true;

            default:
                return false;
        }
    }

    private void ApplyLobbyPlayers(int hostPeerId, IEnumerable<LanPlayerInfo> players)
    {
        HostPeerId = hostPeerId > 0 ? hostPeerId : 1;
        _players.Clear();

        foreach (LanPlayerInfo player in players.OrderBy(player => player.PeerId))
        {
            _players[player.PeerId] = new LanPlayerInfo
            {
                PlayerId = player.PlayerId,
                PeerId = player.PeerId,
                SeatId = player.SeatId,
                TransportPeerId = player.TransportPeerId,
                PlayerName = player.PlayerName,
                CharacterId = player.CharacterId,
                IsReady = player.IsReady,
                IsHost = player.PeerId == HostPeerId || player.IsHost,
                IsConnected = player.IsConnected,
                IsBot = player.IsBot,
                IsSpectator = player.IsSpectator,
                IsBoss = player.IsBoss,
                BotDifficulty = player.BotDifficulty
            };
        }
    }

    private void HandleConnectedToServer()
    {
        IReadOnlyList<ContentPackReference> packs = GameManager.Instance?.CoreRuntime?.ContentPacks
            ?? Array.Empty<ContentPackReference>();
        HandshakeRequestV2 handshake = new(
            ProtocolV2.Version,
            ProtocolV2.EngineApiVersion,
            LocalPlayerName,
            LocalReconnectToken,
            JoinAsSpectator,
            packs);
        RpcId(1, MethodName.RegisterLobbyPlayerRpc, JsonSerializer.Serialize(handshake), LocalCharacterId, false);
        ReportNetworkInfo("Connected to LAN host.");
        SetSessionState(LanSessionState.InLobby);
    }

    private void HandleConnectionFailed()
    {
        if (_isClosingSession || SessionState == LanSessionState.Offline)
        {
            return;
        }

        const string message = "Failed to connect to the LAN host.";
        ReportNetworkError(message);
        CloseSession(clearReconnect: false);
        RaiseUnexpectedDisconnect(LanDisconnectReason.ConnectionFailed, message, wasInMatch: false);
    }

    private void HandleServerDisconnected()
    {
        if (_isClosingSession || SessionState == LanSessionState.Offline)
        {
            return;
        }

        bool wasInMatch = SessionState == LanSessionState.InMatch || GameManager.Instance?.IsMatchRunning == true;
        const string message = "Disconnected from the LAN host.";
        ReportNetworkError(message);
        CloseSession(clearReconnect: false);
        RaiseUnexpectedDisconnect(LanDisconnectReason.HostDisconnected, message, wasInMatch);
    }

    private void RaiseUnexpectedDisconnect(LanDisconnectReason reason, string message, bool wasInMatch)
    {
        OnUnexpectedDisconnect?.Invoke(new LanDisconnectNotice(
            reason,
            message,
            LastJoinAddress,
            Port,
            wasInMatch,
            CanReconnect));
    }

    private void BroadcastLobbySnapshot()
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        LobbySnapshot snapshot = new()
        {
            HostPeerId = HostPeerId,
            ModeId = SelectedModeId,
            ContentPacks = GameManager.Instance?.CoreRuntime?.ContentPacks.ToList() ?? new List<ContentPackReference>(),
            Players = _players.Values.OrderBy(player => player.PeerId).ToList()
        };

        string snapshotJson = JsonSerializer.Serialize(snapshot);
        Rpc(MethodName.ReceiveLobbySnapshotRpc, snapshotJson);
    }

    private void BroadcastMatchSnapshotIfHost()
    {
        TryBindGameManager();

        if (Multiplayer.MultiplayerPeer is null || !Multiplayer.IsServer() || GameManager.Instance is null)
        {
            return;
        }

        MatchStateSnapshot hostSnapshot = GameManager.Instance.BuildMatchStateSnapshot(HostPeerId);
        LastMatchStateSnapshot = hostSnapshot;

        foreach (LanPlayerInfo player in _players.Values)
        {
            if (player.PeerId == HostPeerId || !player.IsConnected)
            {
                continue;
            }

            int transportPeerId = GetTransportPeerIdForPlayer(player);
            if (transportPeerId <= 0)
            {
                continue;
            }

            MatchStateSnapshot snapshot = GameManager.Instance.BuildMatchStateSnapshot(player.PeerId);
            if (player.IsSpectator)
            {
                snapshot = GameManager.Instance.BuildMatchStateSnapshot(0);
            }
            string snapshotJson = JsonSerializer.Serialize(snapshot);
            RpcId(transportPeerId, MethodName.ReceiveMatchStateSnapshotRpc, snapshotJson);
        }
    }

    private void StartMatchInternal()
    {
        GameCoreRuntime runtime = GameManager.Instance?.CoreRuntime
            ?? throw new InvalidOperationException("GameCore runtime is unavailable.");
        ulong seed = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)));
        string matchId = Guid.NewGuid().ToString("N");
        MatchConfig coreConfig = runtime.CreateMatchConfig(SelectedModeId, _players.Values, seed, matchId);
        GameManager.Instance?.BeginCoreMatch(coreConfig);
        foreach (LanPlayerInfo player in _players.Values.Where(player => !player.IsBot && !player.IsSpectator && player.SeatId > 0))
        {
            if (_reservedSeatTokens.TryGetValue(player.PeerId, out string? token))
            {
                runtime.AssignSeatToken(player.SeatId, token);
            }
        }

        MatchStartInfo matchStartInfo = new()
        {
            MatchId = matchId,
            ModeId = SelectedModeId,
            Seed = seed,
            ContentPacks = runtime.ContentPacks.ToList(),
            TurnOrder = _players.Values
                .Where(player => player.IsConnected && !player.IsSpectator)
                .Select(player => player.PeerId)
                .OrderBy(id => id)
                .ToList()
        };

        matchStartInfo.StartingPeerId = matchStartInfo.TurnOrder.FirstOrDefault();
        if (matchStartInfo.StartingPeerId <= 0)
        {
            matchStartInfo.StartingPeerId = HostPeerId;
        }

        string matchStartInfoJson = JsonSerializer.Serialize(matchStartInfo);
        Rpc(MethodName.BeginMatchRpc, matchStartInfoJson);
    }

    private void SendCurrentMatchToPeerIfNeeded(int peerId)
    {
        if (!Multiplayer.IsServer()
            || SessionState != LanSessionState.InMatch
            || GameManager.Instance is null
            || peerId <= 0)
        {
            return;
        }

        MatchStartInfo matchStartInfo = new()
        {
            TurnOrder = _players.Keys.OrderBy(id => id).ToList(),
            StartingPeerId = GameManager.Instance.CurrentTurnPeerId
        };
        if (!_players.TryGetValue(peerId, out LanPlayerInfo? playerInfo) || !playerInfo.IsConnected)
        {
            return;
        }

        int transportPeerId = GetTransportPeerIdForPlayer(playerInfo);
        if (transportPeerId <= 0)
        {
            return;
        }

        RpcId(transportPeerId, MethodName.BeginMatchRpc, JsonSerializer.Serialize(matchStartInfo));

        MatchStateSnapshot snapshot = GameManager.Instance.BuildMatchStateSnapshot(peerId);
        if (playerInfo.IsSpectator)
        {
            snapshot = GameManager.Instance.BuildMatchStateSnapshot(0);
        }
        RpcId(transportPeerId, MethodName.ReceiveMatchStateSnapshotRpc, JsonSerializer.Serialize(snapshot));
    }

    private bool _isBoundToGameManager;
    private bool _matchSnapshotPending;

    public override void _Process(double delta)
    {
        if (!_matchSnapshotPending)
        {
            return;
        }

        _matchSnapshotPending = false;
        BroadcastMatchSnapshotIfHost();
    }

    private void TryBindGameManager()
    {
        if (_isBoundToGameManager || GameManager.Instance is null)
        {
            return;
        }

        GameManager.Instance.OnStateSyncRequested += HandleStateSyncRequested;
        _isBoundToGameManager = true;
    }

    private void UnbindGameManager()
    {
        if (!_isBoundToGameManager || GameManager.Instance is null)
        {
            return;
        }

        GameManager.Instance.OnStateSyncRequested -= HandleStateSyncRequested;
        _isBoundToGameManager = false;
    }

    private void HandleStateSyncRequested()
    {
        if (Multiplayer.MultiplayerPeer is not null && Multiplayer.IsServer())
        {
            _matchSnapshotPending = true;
        }
    }

    private void SetSessionState(LanSessionState newState)
    {
        if (SessionState == newState)
        {
            return;
        }

        SessionState = newState;
        OnSessionStateChanged?.Invoke(SessionState);
    }

    private void RaiseLobbyChanged()
    {
        OnLobbyChanged?.Invoke(_players);
    }

    private void ReportNetworkError(string message)
    {
        LastNetworkMessage = message;
        GD.PushWarning(message);
        OnNetworkError?.Invoke(message);
    }

    private void ReportNetworkInfo(string message)
    {
        LastNetworkMessage = message;
        GD.Print(message);
        OnNetworkMessage?.Invoke(message);
    }

    private int ResolveLocalLogicalPeerId()
    {
        if (IsHost)
        {
            return HostPeerId;
        }

        return ResolveLogicalPeerId(TransportPeerId);
    }

    private int ResolveLogicalPeerId(int transportPeerId)
    {
        if (transportPeerId <= 0)
        {
            return transportPeerId;
        }

        if (transportPeerId == HostPeerId)
        {
            return HostPeerId;
        }

        if (_transportPeerAliases.TryGetValue(transportPeerId, out int logicalPeerId))
        {
            return logicalPeerId;
        }

        LanPlayerInfo? playerInfo = FindPlayerByTransportPeerId(transportPeerId);
        return playerInfo?.PeerId ?? transportPeerId;
    }

    private int ResolveLogicalPeerIdForRegistration(int transportPeerId, string reconnectToken)
    {
        if (SessionState == LanSessionState.InMatch && !string.IsNullOrWhiteSpace(reconnectToken))
        {
            foreach ((int peerId, string token) in _reservedSeatTokens)
            {
                if (_players.TryGetValue(peerId, out LanPlayerInfo? reservedPlayer)
                    && !reservedPlayer.IsConnected
                    && ReconnectTokenFactory.FixedTimeEquals(token, reconnectToken))
                {
                    return reservedPlayer.PeerId;
                }
            }
        }

        return transportPeerId;
    }

    private int ResolveSeatIdForRegistration(int logicalPeerId)
    {
        if (_players.TryGetValue(logicalPeerId, out LanPlayerInfo? existing) && existing.SeatId > 0)
        {
            return existing.SeatId;
        }
        return NextSeatId();
    }

    private int NextSeatId() => _players.Values
        .Where(player => !player.IsSpectator)
        .Select(player => player.SeatId)
        .DefaultIfEmpty(0)
        .Max() + 1;

    private void AddBossSeat(GameCoreRuntime runtime)
    {
        int peerId = _nextBotPeerId++;
        _players[peerId] = new LanPlayerInfo
        {
            PlayerId = "boss",
            PeerId = peerId,
            SeatId = NextSeatId(),
            PlayerName = "Boss",
            CharacterId = runtime.Content.Bosses["boss_jifenxin"].GeneralId,
            IsReady = true,
            IsConnected = true,
            IsBot = true,
            IsBoss = true,
            BotDifficulty = BotDifficulty.Hard
        };
    }

    private void RemoveAutomaticBossSeats()
    {
        foreach (int peerId in _players.Where(entry => entry.Value.IsBoss && entry.Value.IsBot).Select(entry => entry.Key).ToArray())
        {
            _players.Remove(peerId);
        }
    }

    private void SendHandshakeFailure(int transportPeerId, string errorKey)
    {
        HandshakeResultV2 result = new(false, errorKey, 0, string.Empty, 0);
        RpcId(transportPeerId, MethodName.ReceiveHandshakeResultRpc, JsonSerializer.Serialize(result));
        ReportNetworkError($"Rejected peer {transportPeerId}: {errorKey}.");
    }

    private LanPlayerInfo? FindPlayerByTransportPeerId(int transportPeerId)
    {
        return _players.Values.FirstOrDefault(player => player.TransportPeerId == transportPeerId || player.PeerId == transportPeerId);
    }

    private static int GetTransportPeerIdForPlayer(LanPlayerInfo playerInfo)
    {
        return playerInfo.TransportPeerId > 0 ? playerInfo.TransportPeerId : playerInfo.PeerId;
    }

    private static string NormalizePlayerName(string playerName)
    {
        return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
    }
}
