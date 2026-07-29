using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        SetSessionState(LanSessionState.Hosting);

        _players.Clear();
        _players[HostPeerId] = new LanPlayerInfo
        {
            PeerId = HostPeerId,
            TransportPeerId = HostPeerId,
            PlayerName = LocalPlayerName,
            CharacterId = LocalCharacterId,
            IsReady = true,
            IsHost = true,
            IsConnected = true
        };

        ReportNetworkInfo($"Hosting LAN room on UDP port {Port}.");
        BroadcastLobbySnapshot();
        SetSessionState(LanSessionState.InLobby);
        return Error.Ok;
    }

    public Error JoinGame(string address, string playerName, string characterId = "")
    {
        CloseSession(clearReconnect: false);

        LocalPlayerName = NormalizePlayerName(playerName);
        LocalCharacterId = characterId ?? string.Empty;
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

        return JoinGame(LastJoinAddress, LocalPlayerName, LocalCharacterId);
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
            LastNetworkMessage = string.Empty;
            if (clearReconnect)
            {
                LastJoinAddress = string.Empty;
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
    }

    public bool CanStartMatch()
    {
        if (!IsHost || _players.Count == 0)
        {
            return false;
        }

        List<LanPlayerInfo> connectedPlayers = _players.Values
            .Where(player => player.IsConnected)
            .ToList();
        if (connectedPlayers.Count < 2)
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

        string commandJson = JsonSerializer.Serialize(command);
        if (OS.IsDebugBuild())
        {
            GD.Print($"[network] Sending {command.CommandType} from transport peer {Multiplayer.GetUniqueId()} to host 1.");
        }

        Error rpcError = RpcId(1, MethodName.SubmitPlayCommandRpc, commandJson);
        if (rpcError != Error.Ok)
        {
            ReportNetworkError($"Failed to submit {command.CommandType} to the host: {rpcError}.");
            return false;
        }

        return true;
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
    private void RegisterLobbyPlayerRpc(string playerName, string characterId, bool isReady)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        int transportPeerId = Multiplayer.GetRemoteSenderId();
        string normalizedName = NormalizePlayerName(playerName);
        int logicalPeerId = ResolveLogicalPeerIdForRegistration(transportPeerId, normalizedName);
        if (logicalPeerId != transportPeerId)
        {
            _transportPeerAliases[transportPeerId] = logicalPeerId;
        }

        _players[logicalPeerId] = new LanPlayerInfo
        {
            PeerId = logicalPeerId,
            TransportPeerId = transportPeerId,
            PlayerName = normalizedName,
            CharacterId = characterId ?? string.Empty,
            IsReady = isReady,
            IsHost = logicalPeerId == HostPeerId,
            IsConnected = true
        };

        ReportNetworkInfo(logicalPeerId == transportPeerId
            ? $"{normalizedName} joined as peer {logicalPeerId}."
            : $"{normalizedName} reconnected to reserved peer {logicalPeerId}.");
        BroadcastLobbySnapshot();
        SendCurrentMatchToPeerIfNeeded(logicalPeerId);
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

        bool handled = GameManager.Instance?.TryHandleNetworkPlayCommand(senderId, command) == true;
        if (OS.IsDebugBuild())
        {
            GD.Print($"[network] Command {command.CommandType} from peer {senderId}: {(handled ? "accepted" : "rejected")}.");
        }
    }

    private readonly Dictionary<int, LanPlayerInfo> _players = new();

    private readonly Dictionary<int, int> _transportPeerAliases = new();

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
                PeerId = player.PeerId,
                TransportPeerId = player.TransportPeerId,
                PlayerName = player.PlayerName,
                CharacterId = player.CharacterId,
                IsReady = player.IsReady,
                IsHost = player.PeerId == HostPeerId || player.IsHost,
                IsConnected = player.IsConnected
            };
        }
    }

    private void HandleConnectedToServer()
    {
        RpcId(1, MethodName.RegisterLobbyPlayerRpc, LocalPlayerName, LocalCharacterId, false);
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
            string snapshotJson = JsonSerializer.Serialize(snapshot);
            RpcId(transportPeerId, MethodName.ReceiveMatchStateSnapshotRpc, snapshotJson);
        }
    }

    private void StartMatchInternal()
    {
        MatchStartInfo matchStartInfo = new()
        {
            TurnOrder = _players.Values
                .Where(player => player.IsConnected)
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

    private int ResolveLogicalPeerIdForRegistration(int transportPeerId, string normalizedName)
    {
        if (SessionState == LanSessionState.InMatch)
        {
            LanPlayerInfo? reservedPlayer = _players.Values
                .Where(player => !player.IsConnected && !player.IsHost)
                .FirstOrDefault(player => string.Equals(player.PlayerName, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (reservedPlayer is not null)
            {
                return reservedPlayer.PeerId;
            }
        }

        return transportPeerId;
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
