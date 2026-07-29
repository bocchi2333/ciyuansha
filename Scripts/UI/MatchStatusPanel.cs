using System;
using System.Collections.Generic;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Shows phase, turn owner, pile counts, pending operation, and final result.
/// </summary>
public partial class MatchStatusPanel : Control
{
    [Export]
    public NodePath PhaseLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath TurnLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath PilesLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath SlashLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath ResponseLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath ResultLabelPath { get; set; } = new NodePath();

    private Label? _phaseLabel;
    private Label? _turnLabel;
    private Label? _pilesLabel;
    private Label? _slashLabel;
    private Label? _responseLabel;
    private Label? _resultLabel;
    private ColorRect? _stateLine;

    public override void _Ready()
    {
        _phaseLabel = GetNodeOrNull<Label>(PhaseLabelPath);
        _turnLabel = GetNodeOrNull<Label>(TurnLabelPath);
        _pilesLabel = GetNodeOrNull<Label>(PilesLabelPath);
        _slashLabel = GetNodeOrNull<Label>(SlashLabelPath);
        _responseLabel = GetNodeOrNull<Label>(ResponseLabelPath);
        _resultLabel = GetNodeOrNull<Label>(ResultLabelPath);
        ApplyChrome();

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            GameManager.Instance.OnTurnOwnerChanged += HandleTurnOwnerChanged;
            GameManager.Instance.OnMatchRunningChanged += HandleMatchRunningChanged;
            GameManager.Instance.OnMatchEnded += HandleMatchEnded;
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            GameManager.Instance.OnResponseWindowChanged += HandleResponseWindowChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnLobbyChanged += HandleLobbyChanged;
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleNetworkStateChanged;
        }

        RefreshAll();
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            GameManager.Instance.OnTurnOwnerChanged -= HandleTurnOwnerChanged;
            GameManager.Instance.OnMatchRunningChanged -= HandleMatchRunningChanged;
            GameManager.Instance.OnMatchEnded -= HandleMatchEnded;
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
            GameManager.Instance.OnResponseWindowChanged -= HandleResponseWindowChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnLobbyChanged -= HandleLobbyChanged;
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleNetworkStateChanged;
        }
    }

    private void RefreshAll()
    {
        GameManager? game = GameManager.Instance;
        if (game is null)
        {
            SetStateLine(StatusTone.Waiting);
            SetLabel(_phaseLabel, "阶段：等待牌局");
            SetLabel(_turnLabel, "当前：等待玩家");
            SetLabel(_pilesLabel, "牌堆：-- | 弃牌：--");
            SetLabel(_slashLabel, "本回合杀：--");
            SetLabel(_responseLabel, "等待：牌局状态不可用。");
            SetLabel(_resultLabel, "结果：--");
            return;
        }

        SetPhase(game.CurrentPhase);
        SetTurnOwner(game.CurrentTurnPeerId);
        SetPileCounts(game.DrawPileCount, game.DiscardPileCount);
        SetSlashUsage(game.SlashUsesThisTurn);
        SetWaitingState();
        SetResult(game.IsMatchRunning ? "对局进行中" : BattleLogTextLocalizer.Localize(game.ResultMessage));
    }

    private void HandlePhaseChanged(TurnPhase phase)
    {
        SetPhase(phase);
        SetWaitingState();
    }

    private void HandleTurnOwnerChanged(int peerId)
    {
        SetTurnOwner(peerId);
        SetWaitingState();
    }

    private void HandleMatchRunningChanged(bool isRunning)
    {
        SetResult(isRunning ? "对局进行中" : BattleLogTextLocalizer.Localize(GameManager.Instance?.ResultMessage ?? string.Empty));
        RefreshAll();
    }

    private void HandleMatchEnded(int winnerPeerId, string resultMessage)
    {
        SetResult(string.IsNullOrWhiteSpace(resultMessage)
            ? $"胜者：{FormatPlayerName(winnerPeerId)}"
            : BattleLogTextLocalizer.Localize(resultMessage));
        SetStateLine(StatusTone.Ended);
    }

    private void HandleStateChanged()
    {
        if (GameManager.Instance is null)
        {
            return;
        }

        SetPileCounts(GameManager.Instance.DrawPileCount, GameManager.Instance.DiscardPileCount);
        SetSlashUsage(GameManager.Instance.SlashUsesThisTurn);
        SetWaitingState();
    }

    private void HandleResponseWindowChanged()
    {
        SetWaitingState();
    }

    private void HandleLobbyChanged(IReadOnlyDictionary<int, LanPlayerInfo> players)
    {
        RefreshAll();
    }

    private void HandleNetworkStateChanged(LanSessionState state)
    {
        RefreshAll();
    }

    private void SetPhase(TurnPhase phase)
    {
        SetLabel(_phaseLabel, $"阶段：{FormatPhase(phase)}");
    }

    private void SetTurnOwner(int peerId)
    {
        SetLabel(_turnLabel, peerId <= 0 ? "当前：等待回合开始" : $"当前：{FormatPlayerName(peerId)}");
    }

    private void SetPileCounts(int drawPileCount, int discardPileCount)
    {
        SetLabel(_pilesLabel, $"牌堆：{drawPileCount:D2} | 弃牌：{discardPileCount:D2}");
    }

    private void SetSlashUsage(int slashUsesThisTurn)
    {
        string usage = slashUsesThisTurn <= 0 ? "0/1" : $"{slashUsesThisTurn}/1（已用）";
        SetLabel(_slashLabel, $"本回合杀：{usage}");
    }

    private void SetWaitingState()
    {
        if (_responseLabel is null)
        {
            return;
        }

        GameManager? game = GameManager.Instance;
        if (game is null)
        {
            SetStateLine(StatusTone.Waiting);
            _responseLabel.Text = "等待：牌局状态不可用。";
            return;
        }

        if (!game.IsMatchRunning)
        {
            SetStateLine(StatusTone.Ended);
            _responseLabel.Text = string.IsNullOrWhiteSpace(game.ResultMessage)
                ? "对局结束：等待返回大厅。"
                : $"对局结束：{BattleLogTextLocalizer.Localize(game.ResultMessage)}";
            return;
        }

        int waitingPeerId = 0;
        string action = string.Empty;
        string prompt = string.Empty;
        StatusTone tone = StatusTone.Resolving;

        if (game.HasPendingResponseWindow)
        {
            waitingPeerId = game.PendingResponsePeerId;
            action = FormatWaitingKind(game.PendingResponseKind.ToString());
            prompt = game.PendingResponsePrompt;
            tone = StatusTone.Response;
        }
        else if (game.HasPendingHarvestSelection)
        {
            waitingPeerId = game.PendingHarvestPeerId;
            action = "五谷丰登选牌";
            prompt = game.PendingHarvestPrompt;
            tone = StatusTone.Waiting;
        }
        else if (game.HasPendingTargetCardSelection)
        {
            waitingPeerId = game.PendingTargetCardSelectionPeerId;
            action = "选择目标牌";
            prompt = game.PendingTargetCardSelectionPrompt;
            tone = StatusTone.Waiting;
        }
        else if (game.HasPendingHandCardSelection)
        {
            waitingPeerId = game.PendingHandCardSelectionPeerId;
            action = game.CanDeclineHandCardSelection ? "选择手牌或放弃" : "选择手牌";
            prompt = game.PendingHandCardSelectionPrompt;
            tone = StatusTone.Waiting;
        }
        else if (game.IsAwaitingDiscardInput)
        {
            waitingPeerId = game.CurrentTurnPeerId;
            action = "弃牌阶段";
            prompt = "弃牌至当前手牌上限。";
            tone = StatusTone.Waiting;
        }
        else if (game.IsAwaitingPlayerInput)
        {
            waitingPeerId = game.CurrentTurnPeerId;
            action = "出牌阶段";
            prompt = "出牌、发动技能，或结束出牌阶段。";
            tone = StatusTone.Waiting;
        }

        if (waitingPeerId <= 0)
        {
            SetStateLine(StatusTone.Resolving);
            _responseLabel.Text = "等待：动作结算中。";
            return;
        }

        bool isYou = IsLocalPeer(waitingPeerId);
        SetStateLine(isYou ? StatusTone.YourAction : tone);
        string prefix = isYou ? "需要操作" : "等待";
        string actor = isYou ? "你" : FormatPlayerName(waitingPeerId);
        string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "等待输入。" : BattleLogTextLocalizer.Localize(prompt);
        _responseLabel.Text = $"{prefix}：{actor} | {action} | {safePrompt}";
    }

    private void SetResult(string result)
    {
        SetLabel(_resultLabel, string.IsNullOrWhiteSpace(result) ? "结果：--" : $"结果：{result}");
    }

    private void ApplyChrome()
    {
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel"), CyberPanelKind.Strong);
        EnsureStateLine();
        CyberStyle.ApplyLabel(_phaseLabel, title: true);
        CyberStyle.ApplyLabel(_turnLabel);
        CyberStyle.ApplyLabel(_pilesLabel, color: CyberStyle.MutedText);
        CyberStyle.ApplyLabel(_slashLabel, color: CyberStyle.NeonJade);
        CyberStyle.ApplyLabel(_responseLabel, color: CyberStyle.Gold);
        CyberStyle.ApplyLabel(_resultLabel, color: CyberStyle.MutedText);
    }

    private void EnsureStateLine()
    {
        Control? panel = GetNodeOrNull<Control>("Panel");
        if (panel is null)
        {
            return;
        }

        _stateLine = panel.GetNodeOrNull<ColorRect>("MatchStateLine");
        if (_stateLine is not null)
        {
            return;
        }

        _stateLine = new ColorRect
        {
            Name = "MatchStateLine",
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(CyberStyle.MutedText.R, CyberStyle.MutedText.G, CyberStyle.MutedText.B, 0.30f)
        };
        _stateLine.AnchorLeft = 0.08f;
        _stateLine.AnchorRight = 0.92f;
        _stateLine.AnchorTop = 0f;
        _stateLine.AnchorBottom = 0f;
        _stateLine.OffsetTop = 6f;
        _stateLine.OffsetBottom = 8f;
        panel.AddChild(_stateLine);
        panel.MoveChild(_stateLine, 1);
    }

    private void SetStateLine(StatusTone tone)
    {
        if (_stateLine is null)
        {
            return;
        }

        _stateLine.Color = tone switch
        {
            StatusTone.YourAction => new Color(CyberStyle.NeonJade.R, CyberStyle.NeonJade.G, CyberStyle.NeonJade.B, 0.58f),
            StatusTone.Response => new Color(CyberStyle.Vermilion.R, CyberStyle.Vermilion.G, CyberStyle.Vermilion.B, 0.56f),
            StatusTone.Waiting => new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.42f),
            StatusTone.Ended => new Color(CyberStyle.Thunder.R, CyberStyle.Thunder.G, CyberStyle.Thunder.B, 0.50f),
            _ => new Color(CyberStyle.MutedText.R, CyberStyle.MutedText.G, CyberStyle.MutedText.B, 0.32f)
        };
    }

    private static void SetLabel(Label? label, string text)
    {
        if (label is not null)
        {
            label.Text = text;
        }
    }

    private static string FormatPhase(TurnPhase phase)
    {
        return phase switch
        {
            TurnPhase.TurnStart => "准备阶段",
            TurnPhase.JudgementPhase => "判定阶段",
            TurnPhase.DrawPhase => "摸牌阶段",
            TurnPhase.PlayPhase => "出牌阶段",
            TurnPhase.DiscardPhase => "弃牌阶段",
            TurnPhase.EndPhase => "结束阶段",
            _ => phase.ToString()
        };
    }

    private static string FormatWaitingKind(string kind)
    {
        return kind switch
        {
            "Dodge" => "打出【闪】或放弃",
            "Peach" => "桃 / 酒救援或放弃",
            "Slash" => "打出【杀】或放弃",
            "Nullification" => "无懈或放弃",
            _ => string.IsNullOrWhiteSpace(kind) ? "等待输入" : kind
        };
    }

    private static bool IsLocalPeer(int peerId)
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        return network?.IsConnected == true && network.LocalPeerId == peerId;
    }

    private static string FormatPlayerName(int peerId)
    {
        if (peerId <= 0)
        {
            return "未知玩家";
        }

        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network?.Players.TryGetValue(peerId, out LanPlayerInfo? player) == true)
        {
            string name = string.IsNullOrWhiteSpace(player.PlayerName) ? $"玩家 {peerId}" : player.PlayerName;
            string localMark = IsLocalPeer(peerId) ? "，你" : string.Empty;
            string offlineMark = player.IsConnected ? string.Empty : "，离线";
            return $"{name}（玩家 {peerId}{localMark}{offlineMark}）";
        }

        return $"玩家 {peerId}";
    }

    private enum StatusTone
    {
        Resolving,
        Waiting,
        YourAction,
        Response,
        Ended
    }
}
