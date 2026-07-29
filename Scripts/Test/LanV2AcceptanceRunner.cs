using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CiyuanSha.GameCore.AI;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Engine;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.Test;

/// <summary>
/// Protocol V2-only multi-process acceptance runner. Every in-match action is
/// derived from the redacted GameView and submitted as a ChoiceResult.
/// </summary>
public partial class LanV2AcceptanceRunner : Node
{
    private const double DefaultTimeoutSeconds = 70;
    private readonly DeterministicBotPolicy _policy = new();
    private readonly HashSet<string> _submittedRequests = new(StringComparer.Ordinal);
    private string _role = "host";
    private string _scenario = "duel2";
    private string _address = "127.0.0.1";
    private string _syncRoot = string.Empty;
    private int _port = 24701;
    private int _clientIndex = 1;
    private int _expectedProcesses = 2;
    private double _timeoutSeconds = DefaultTimeoutSeconds;
    private double _elapsed;
    private double _nextJoinAt;
    private bool _joinRequested;
    private bool _readySent;
    private bool _startRequested;
    private bool _observedInMatch;
    private bool _passed;
    private bool _doneWritten;
    private bool _sawExpectedStaleRejection;
    private bool _stalePairSent;
    private bool _reconnectDropStarted;
    private bool _reconnectRequested;
    private bool _reconnectValidated;
    private bool _replayVerified;
    private bool _returnLobbyRequested;
    private bool _lobbyMarkerWritten;
    private int _reservedSeat;
    private string _reservedToken = string.Empty;
    private string _suspendedRequestId = string.Empty;
    private long _resumeCursor;
    private double _nextProgressLogAt = 5;

    private bool IsHost => _role == "host";
    private bool IsSpectator => _role == "spectator";
    private string RoleKey => IsHost ? "host" : IsSpectator ? "spectator" : $"client{_clientIndex}";

    public override void _Ready()
    {
        ParseArguments();
        Directory.CreateDirectory(_syncRoot);
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || GameManager.Instance?.CoreRuntime is null)
        {
            Fail("Required protocol V2 autoloads are unavailable.");
            return;
        }
        network.Port = _port;
        if (_scenario == "replay") network.MatchSeedOverride = 0xC1A0_0000UL;
        network.OnNetworkError += HandleNetworkError;
        network.OnNetworkMessage += message => GD.Print($"[{RoleKey}] {message}");

        if (IsHost)
        {
            Error error = network.HostGame("V2Host", "chenchen");
            if (error != Error.Ok)
            {
                Fail($"HostGame failed: {error}");
                return;
            }
            ConfigureHost(network);
        }
        else
        {
            _nextJoinAt = IsSpectator ? (_scenario == "replay" ? 0.45 : 3.0) : 0.45 + (_clientIndex * 0.2);
        }
        GD.Print($"[{RoleKey}] V2 acceptance boot: scenario={_scenario}, port={_port}");
    }

    public override void _ExitTree()
    {
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnNetworkError -= HandleNetworkError;
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed >= _timeoutSeconds)
        {
            Fail($"Timed out after {_timeoutSeconds:F0}s.");
            return;
        }

        if (File.Exists(DonePath))
        {
            GD.Print($"[{RoleKey}] LAN_V2_ACCEPTANCE_SUCCESS {_scenario}");
            GetTree().Quit(0);
            return;
        }
        string? failure = Directory.EnumerateFiles(_syncRoot, "*.fail").FirstOrDefault();
        if (failure is not null && !string.Equals(failure, FailPath, StringComparison.OrdinalIgnoreCase))
        {
            Fail($"Another participant failed: {File.ReadAllText(failure)}");
            return;
        }

        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null) return;

        if (!IsHost && !_joinRequested && _elapsed >= _nextJoinAt)
        {
            _joinRequested = true;
            Error error = network.JoinGame(_address, $"V2{RoleKey}", GeneralForClient(), IsSpectator);
            if (error != Error.Ok) Fail($"JoinGame failed: {error}");
            return;
        }

        if (!IsHost && !IsSpectator && network.SessionState == LanSessionState.InLobby && !_readySent)
        {
            _readySent = true;
            network.SelectLocalCharacter(GeneralForClient());
            network.SetLocalReady(true);
        }

        if (IsHost && network.SessionState == LanSessionState.InLobby && !_startRequested && network.CanStartMatch()
            && (_scenario != "replay" || network.Players.Values.Any(player => player.IsSpectator)))
        {
            _startRequested = true;
            network.StartMatch();
        }

        if (network.SessionState == LanSessionState.InMatch)
        {
            _observedInMatch = true;
        }
        if (_elapsed >= _nextProgressLogAt)
        {
            _nextProgressLogAt += 5;
            GameView? progressView = IsHost
                ? GameManager.Instance?.CoreRuntime?.Engine?.BuildView(ViewerContext.ForPlayer(Math.Max(1, ResolveLocalSeat(network))))
                : network.LastMatchStateSnapshot?.CoreView;
            GD.Print($"[{RoleKey}] progress session={network.SessionState} players={network.Players.Count} "
                + $"revision={progressView?.Revision ?? -1} status={progressView?.Status} actor={progressView?.PendingChoice?.ActingSeatId ?? 0} local={ResolveLocalSeat(network)}");
        }

        if (_scenario == "reconnect" && !IsHost && !IsSpectator)
        {
            HandleReconnectScenario(network);
        }

        if (!_reconnectDropStarted || _reconnectValidated || _scenario != "reconnect" || IsHost)
        {
            SubmitLocalChoiceIfNeeded(network);
        }

        if (!_passed && AcceptanceReached(network))
        {
            MarkPass(BuildEvidence(network));
        }

        if (_scenario == "replay" && _passed && !IsHost
            && network.SessionState == LanSessionState.InLobby
            && network.Players.Values.All(player => player.IsSpectator || !player.IsReady)
            && !_lobbyMarkerWritten)
        {
            _lobbyMarkerWritten = true;
            File.WriteAllText(LobbyPath, $"scenario={_scenario}; role={RoleKey}; returned=true");
        }

        if (IsHost && _passed && !_doneWritten
            && Directory.EnumerateFiles(_syncRoot, "*.pass").Count() >= _expectedProcesses)
        {
            if (_scenario == "replay")
            {
                if (!_returnLobbyRequested)
                {
                    _returnLobbyRequested = true;
                    network.ReturnToLobby();
                    return;
                }
                if (network.SessionState != LanSessionState.InLobby
                    || network.Players.Values.Any(player => !player.IsSpectator && player.IsReady))
                {
                    return;
                }
                if (!_lobbyMarkerWritten)
                {
                    _lobbyMarkerWritten = true;
                    File.WriteAllText(LobbyPath, $"scenario={_scenario}; role={RoleKey}; returned=true");
                }
                if (Directory.EnumerateFiles(_syncRoot, "*.lobby").Count() < _expectedProcesses)
                {
                    return;
                }
            }
            _doneWritten = true;
            File.WriteAllText(DonePath, $"scenario={_scenario}; participants={_expectedProcesses}");
        }
    }

    private void ConfigureHost(LanMultiplayerManager network)
    {
        string mode = _scenario switch
        {
            "boss3" => BuiltInModeIds.Boss,
            "identity4" or "bots" => BuiltInModeIds.Identity,
            _ => BuiltInModeIds.Duel
        };
        if (!network.SetMode(mode))
        {
            Fail($"Could not select mode {mode}.");
            return;
        }
        if (_scenario == "bots")
        {
            network.SetBotDifficulty(BotDifficulty.Hard);
            for (int index = 0; index < 3; index++)
            {
                if (!network.AddBot(difficulty: (BotDifficulty)(index % 3)))
                {
                    Fail("Could not add bot fill seat.");
                    return;
                }
            }
        }
        else if (_scenario == "replay")
        {
            network.SetBotDifficulty(BotDifficulty.Easy);
            if (!network.AddBot("funingna", BotDifficulty.Easy)) Fail("Could not add replay opponent bot.");
        }
    }

    private void SubmitLocalChoiceIfNeeded(LanMultiplayerManager network)
    {
        if (!_observedInMatch || IsSpectator) return;
        int localSeat = ResolveLocalSeat(network);
        GameView? view = IsHost
            ? GameManager.Instance?.CoreRuntime?.Engine?.BuildView(ViewerContext.ForPlayer(localSeat))
            : network.LastMatchStateSnapshot?.CoreView;
        ChoiceRequest? request = view?.PendingChoice;
        if (view is null || request is null || request.ActingSeatId != localSeat
            || _submittedRequests.Contains(request.RequestId))
        {
            return;
        }

        ulong decisionSeedBase = _scenario == "replay" ? 0xC1A0_0000UL : (ulong)(_port * 1000L);
        ChoiceResult choice = _policy.Choose(
            view,
            request,
            new BotContext(_scenario == "replay" ? BotDifficulty.Easy : BotDifficulty.Standard,
                decisionSeedBase ^ (ulong)request.StateRevision ^ ((ulong)(uint)localSeat << 32)));
        _submittedRequests.Add(request.RequestId);

        if (_scenario == "stale" && !IsHost && !_stalePairSent)
        {
            _stalePairSent = true;
            bool firstSent = network.SubmitChoice(choice);
            bool duplicateSent = network.SubmitChoice(choice);
            if (!firstSent || !duplicateSent) Fail("Could not send stale ChoiceResult pair.");
            return;
        }
        if (!network.SubmitChoice(choice))
        {
            Fail($"SubmitChoice failed for request {request.RequestId}.");
        }
    }

    private void HandleReconnectScenario(LanMultiplayerManager network)
    {
        if (!_reconnectDropStarted && network.SessionState == LanSessionState.InMatch)
        {
            GameView? view = network.LastMatchStateSnapshot?.CoreView;
            int localSeat = ResolveLocalSeat(network);
            if (view?.PendingChoice?.ActingSeatId != localSeat) return;
            _reservedSeat = localSeat;
            _reservedToken = network.LocalReconnectToken;
            _suspendedRequestId = view.PendingChoice.RequestId;
            _resumeCursor = network.LastMatchStateSnapshot?.JournalCursor ?? 0;
            _reconnectDropStarted = true;
            _nextJoinAt = _elapsed + 0.7;
            network.DisconnectForReconnect();
            GD.Print($"[{RoleKey}] transport dropped at choice {_suspendedRequestId}, cursor {_resumeCursor}");
            return;
        }

        if (_reconnectDropStarted && !_reconnectRequested
            && network.SessionState == LanSessionState.Offline && _elapsed >= _nextJoinAt)
        {
            _reconnectRequested = true;
            Error error = network.ReconnectLastSession();
            if (error != Error.Ok) Fail($"ReconnectLastSession failed: {error}");
            return;
        }

        if (_reconnectRequested && !_reconnectValidated && network.SessionState == LanSessionState.InMatch
            && network.LastJournalDelta is { } delta && network.LastMatchStateSnapshot?.CoreView is { } resumed)
        {
            int currentSeat = ResolveLocalSeat(network);
            bool valid = currentSeat == _reservedSeat
                && network.LocalReconnectToken == _reservedToken
                && delta.FromCursor == _resumeCursor
                && resumed.PendingChoice?.RequestId == _suspendedRequestId;
            if (!valid)
            {
                Fail($"Reconnect state mismatch: seat={currentSeat}/{_reservedSeat}, delta={delta.FromCursor}/{_resumeCursor}, choice={resumed.PendingChoice?.RequestId}/{_suspendedRequestId}.");
                return;
            }
            _reconnectValidated = true;
            GD.Print($"[{RoleKey}] reconnect delta validated: {delta.FromCursor}..{delta.ToCursor}");
        }
    }

    private bool AcceptanceReached(LanMultiplayerManager network)
    {
        if (!_observedInMatch) return false;
        GameView? view = IsHost
            ? GameManager.Instance?.CoreRuntime?.Engine?.BuildView(ViewerContext.ForPlayer(ResolveLocalSeat(network)))
            : network.LastMatchStateSnapshot?.CoreView;
        if (view is null || view.Revision < 8) return false;

        int participantCount = network.Players.Values.Count(player => !player.IsSpectator);
        int expectedParticipants = _scenario switch
        {
            "boss3" or "identity4" or "bots" => 4,
            _ => 2
        };
        if (participantCount != expectedParticipants) return false;

        if (IsSpectator)
        {
            RuleJournalEntry[] entries = network.LastJournalDelta?.Entries.ToArray() ?? Array.Empty<RuleJournalEntry>();
            bool journalRedacted = entries.All(entry => entry.ChoiceResult is null
                && entry.RandomValue is null
                && string.IsNullOrEmpty(entry.EntryHash)
                && string.IsNullOrEmpty(entry.PreviousEntryHash));
            bool privacyValid = network.JoinAsSpectator
                && ResolveLocalSeat(network) == 0
                && view.PrivateHandCardIds.Count == 0
                && view.PrivateHandCards.Count == 0
                && (view.PendingChoice is null || view.PendingChoice.Options.Count == 0)
                && journalRedacted
                && !network.SubmitChoice(new ChoiceResult("forged", view.Revision, Array.Empty<string>(), false));
            return privacyValid && (_scenario != "replay" || VerifyAutoSavedReplay(network, view));
        }

        int seat = ResolveLocalSeat(network);
        bool privateViewValid = view.PrivateHandCards.All(card => card.OwnerSeatId == seat)
            && view.PrivateHandCardIds.Count == view.PrivateHandCards.Count
            && view.Players.Where(player => player.SeatId != seat).All(player =>
                view.PrivateHandCards.All(card => card.OwnerSeatId != player.SeatId));
        if (!privateViewValid) return false;

        if (_scenario == "reconnect" && !IsHost) return _reconnectValidated && view.Revision >= 10;
        if (_scenario == "replay") return VerifyAutoSavedReplay(network, view);
        if (_scenario == "stale" && IsHost) return _sawExpectedStaleRejection;
        if (_scenario == "stale" && !IsHost) return _stalePairSent;
        if (_scenario == "bots" && IsHost)
        {
            return network.Players.Values.Count(player => player.IsBot && !player.IsBoss) == 3
                && network.Players.Values.Where(player => player.IsBot).Select(player => player.BotDifficulty).Distinct().Count() == 3;
        }
        return true;
    }

    private bool VerifyAutoSavedReplay(LanMultiplayerManager network, GameView view)
    {
        if (_replayVerified) return true;
        if (view.Status != MatchStatus.Completed || string.IsNullOrWhiteSpace(network.LastSavedReplayPath)
            || !File.Exists(network.LastSavedReplayPath))
        {
            return false;
        }
        try
        {
            ReplayDocument document = Task.Run(() => ReplayArchive.ReadAsync(network.LastSavedReplayPath)).GetAwaiter().GetResult();
            AssertReplayPrivacy(document, IsHost);
            ReplayPlaybackSession playback = new(document, GameManager.Instance!.CoreRuntime!.Content);
            ReplayVerificationResult verification = playback.Verify();
            if (!verification.IsValid)
            {
                Fail($"Auto-saved replay verification failed: {verification.ErrorKey}@{verification.DivergentSequence}.");
                return false;
            }
            _replayVerified = true;
            return true;
        }
        catch (Exception exception)
        {
            Fail($"Auto-saved replay could not be opened: {exception.Message}");
            return false;
        }
    }

    private static void AssertReplayPrivacy(ReplayDocument document, bool host)
    {
        if (document.Header.IsOmniscient != host)
        {
            throw new InvalidDataException("Replay omniscience flag does not match the recording role.");
        }
        if (!host && (document.Checkpoints.Count != 0
            || document.Entries.Any(entry => entry.ChoiceResult is not null || entry.RandomValue is not null
                || !string.IsNullOrEmpty(entry.EntryHash) || !string.IsNullOrEmpty(entry.PreviousEntryHash))))
        {
            throw new InvalidDataException("Limited replay contains authoritative data.");
        }
    }

    private string BuildEvidence(LanMultiplayerManager network)
    {
        long cursor = IsHost
            ? GameManager.Instance?.CoreRuntime?.Engine?.Journal.Cursor ?? 0
            : network.LastMatchStateSnapshot?.JournalCursor ?? 0;
        return $"scenario={_scenario}; role={RoleKey}; cursor={cursor}; revision="
            + (IsHost
                ? GameManager.Instance?.CoreRuntime?.Engine?.State.Revision ?? 0
                : network.LastMatchStateSnapshot?.StateRevision ?? 0);
    }

    private int ResolveLocalSeat(LanMultiplayerManager network)
    {
        if (IsSpectator) return 0;
        return network.Players.TryGetValue(network.LocalPeerId, out LanPlayerInfo? player)
            ? player.SeatId
            : IsHost ? 1 : 0;
    }

    private void HandleNetworkError(string message)
    {
        GD.PrintErr($"[{RoleKey}] network: {message}");
        if (IsHost && _scenario == "stale"
            && message.Contains("Choice rejected:", StringComparison.Ordinal)
            && (message.Contains("stale", StringComparison.OrdinalIgnoreCase)
                || message.Contains("mismatch", StringComparison.OrdinalIgnoreCase)))
        {
            _sawExpectedStaleRejection = true;
            return;
        }
        if (_scenario == "reconnect" && message.Contains("Disconnected from", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (message.Contains("rejected", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            Fail($"Unexpected network error: {message}");
        }
    }

    private void MarkPass(string evidence)
    {
        _passed = true;
        File.WriteAllText(PassPath, evidence);
        GD.Print($"[{RoleKey}] PASS {evidence}");
    }

    private void Fail(string message)
    {
        if (_passed && File.Exists(DonePath)) return;
        try
        {
            Directory.CreateDirectory(_syncRoot);
            File.WriteAllText(FailPath, message);
        }
        catch (Exception) { }
        GD.PrintErr($"[{RoleKey}] LAN_V2_ACCEPTANCE_FAILURE {message}");
        GetTree().Quit(1);
    }

    private string GeneralForClient() => _scenario == "replay" ? "funingna" : (_clientIndex % 3) switch
    {
        1 => "hanfeiyang",
        2 => "xingjianya",
        _ => "huanglubaiquan"
    };

    private string PassPath => Path.Combine(_syncRoot, $"{RoleKey}.pass");
    private string FailPath => Path.Combine(_syncRoot, $"{RoleKey}.fail");
    private string LobbyPath => Path.Combine(_syncRoot, $"{RoleKey}.lobby");
    private string DonePath => Path.Combine(_syncRoot, "done.marker");

    private void ParseArguments()
    {
        Dictionary<string, string> arguments = OS.GetCmdlineUserArgs()
            .Where(argument => argument.StartsWith("--", StringComparison.Ordinal) && argument.Contains('='))
            .Select(argument => argument[2..].Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        _role = arguments.GetValueOrDefault("v2-role", _role).ToLowerInvariant();
        _scenario = arguments.GetValueOrDefault("v2-scenario", _scenario).ToLowerInvariant();
        _address = arguments.GetValueOrDefault("address", _address);
        _syncRoot = arguments.GetValueOrDefault("sync-root", Path.Combine(Path.GetTempPath(), $"ciyuansha-v2-{_port}"));
        if (int.TryParse(arguments.GetValueOrDefault("port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)) _port = port;
        if (int.TryParse(arguments.GetValueOrDefault("client-index"), out int clientIndex)) _clientIndex = clientIndex;
        if (int.TryParse(arguments.GetValueOrDefault("expected-processes"), out int expected)) _expectedProcesses = expected;
        if (double.TryParse(arguments.GetValueOrDefault("runner-timeout"), NumberStyles.Float, CultureInfo.InvariantCulture, out double timeout)) _timeoutSeconds = timeout;
    }
}
