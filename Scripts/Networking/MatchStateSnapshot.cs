using System.Collections.Generic;

namespace CiyuanSha.Networking;

/// <summary>
/// Authoritative replicated match state.
/// </summary>
public class MatchStateSnapshot
{
    public bool IsMatchRunning { get; set; }

    public int WinnerPeerId { get; set; }

    public string ResultMessage { get; set; } = string.Empty;

    public bool EnableFactionVictoryRules { get; set; }

    public int CurrentTurnPeerId { get; set; } = 1;

    public int CurrentTurnIndex { get; set; }

    public string CurrentPhase { get; set; } = "TurnStart";

    public int WaitingPeerId { get; set; }

    public string WaitingKind { get; set; } = string.Empty;

    public string WaitingPrompt { get; set; } = string.Empty;

    public List<LanPlayerInfo> LobbyPlayers { get; set; } = new();

    public bool IsAwaitingPlayInput { get; set; }

    public bool IsAwaitingDiscardInput { get; set; }

    public bool HasPendingResponseWindow { get; set; }

    public int PendingResponsePeerId { get; set; }

    public string PendingResponsePrompt { get; set; } = string.Empty;

    public string PendingResponseKind { get; set; } = "None";

    public int PendingHarvestPeerId { get; set; }

    public string PendingHarvestPrompt { get; set; } = string.Empty;

    public List<NetworkHandCardState> HarvestPool { get; set; } = new();

    public int PendingTargetCardSelectionPeerId { get; set; }

    public string PendingTargetCardSelectionPrompt { get; set; } = string.Empty;

    public List<NetworkHandCardState> TargetCardSelectionPool { get; set; } = new();

    public int PendingHandCardSelectionPeerId { get; set; }

    public string PendingHandCardSelectionPrompt { get; set; } = string.Empty;

    public List<NetworkHandCardState> HandCardSelectionPool { get; set; } = new();

    public bool CanDeclineHandCardSelection { get; set; }

    public int SlashUsesThisTurn { get; set; }

    public int WineUsesThisTurn { get; set; }

    public int DrawPileCount { get; set; }

    public int DiscardPileCount { get; set; }

    public List<int> TurnOrder { get; set; } = new();

    public List<NetworkCharacterState> Characters { get; set; } = new();

    public List<NetworkBattleLogEntry> BattleLog { get; set; } = new();

    public List<NetworkPresentationEvent> PresentationEvents { get; set; } = new();
}
