using System;
using System.Linq;
using System.Text;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.UI;
using Godot;

namespace CiyuanSha.Replay;

/// <summary>
/// Complete offline replay surface. It deliberately talks only to
/// <see cref="ReplayController"/> and therefore never starts LAN or bot input.
/// </summary>
public partial class ReplayViewerPanel : Control
{
    private ReplayController? _controller;
    private FileDialog? _fileDialog;
    private LineEdit? _pathEdit;
    private Button? _openButton;
    private Button? _loadButton;
    private Button? _closeButton;
    private Button? _playPauseButton;
    private Button? _stepBackButton;
    private Button? _stepForwardButton;
    private OptionButton? _speedOption;
    private OptionButton? _viewOption;
    private HSlider? _timeline;
    private Label? _summaryLabel;
    private Label? _eventLabel;
    private RichTextLabel? _playersLabel;
    private Label? _choiceLabel;
    private bool _updatingControls;

    public event Action? CloseRequested;

    public ReplayPlaybackSession? Session => _controller?.Session;

    public override void _Ready()
    {
        _controller = GetNodeOrNull<ReplayController>("ReplayController");
        _fileDialog = GetNodeOrNull<FileDialog>("ReplayFileDialog");
        _pathEdit = GetNodeOrNull<LineEdit>("Margin/Layout/FileRow/PathEdit");
        _openButton = GetNodeOrNull<Button>("Margin/Layout/FileRow/OpenButton");
        _loadButton = GetNodeOrNull<Button>("Margin/Layout/FileRow/LoadButton");
        _closeButton = GetNodeOrNull<Button>("Margin/Layout/HeaderRow/CloseButton");
        _playPauseButton = GetNodeOrNull<Button>("Margin/Layout/ControlsRow/PlayPauseButton");
        _stepBackButton = GetNodeOrNull<Button>("Margin/Layout/ControlsRow/StepBackButton");
        _stepForwardButton = GetNodeOrNull<Button>("Margin/Layout/ControlsRow/StepForwardButton");
        _speedOption = GetNodeOrNull<OptionButton>("Margin/Layout/ControlsRow/SpeedOption");
        _viewOption = GetNodeOrNull<OptionButton>("Margin/Layout/ControlsRow/ViewOption");
        _timeline = GetNodeOrNull<HSlider>("Margin/Layout/Timeline");
        _summaryLabel = GetNodeOrNull<Label>("Margin/Layout/SummaryLabel");
        _eventLabel = GetNodeOrNull<Label>("Margin/Layout/EventPanel/EventMargin/EventLabel");
        _playersLabel = GetNodeOrNull<RichTextLabel>("Margin/Layout/ContentRow/PlayersPanel/PlayersMargin/PlayersLabel");
        _choiceLabel = GetNodeOrNull<Label>("Margin/Layout/ContentRow/ChoicePanel/ChoiceMargin/ChoiceLabel");

        ConfigureOptions();
        WireSignals();
        ApplyStyle();
        Refresh();
    }

    public override void _ExitTree()
    {
        UnwireSignals();
    }

    private void ConfigureOptions()
    {
        if (_speedOption is not null)
        {
            _speedOption.Clear();
            AddSpeed("0.5 倍", 0.5);
            AddSpeed("1 倍", 1);
            AddSpeed("2 倍", 2);
            AddSpeed("4 倍", 4);
            _speedOption.Select(1);
        }

        if (_fileDialog is not null)
        {
            _fileDialog.Filters = new[] { "*.cysreplay ; 次元杀录像" };
        }
    }

    private void AddSpeed(string text, double value)
    {
        _speedOption!.AddItem(text);
        _speedOption.SetItemMetadata(_speedOption.ItemCount - 1, value);
    }

    private void WireSignals()
    {
        if (_controller is not null)
        {
            _controller.OnReplayChanged += Refresh;
            _controller.OnReplayError += HandleReplayError;
        }
        if (_fileDialog is not null) _fileDialog.FileSelected += HandleFileSelected;
        if (_openButton is not null) _openButton.Pressed += OpenFileDialog;
        if (_loadButton is not null) _loadButton.Pressed += LoadEditedPath;
        if (_closeButton is not null) _closeButton.Pressed += HandleClose;
        if (_playPauseButton is not null) _playPauseButton.Pressed += TogglePlayback;
        if (_stepBackButton is not null) _stepBackButton.Pressed += () => _controller?.StepBackward();
        if (_stepForwardButton is not null) _stepForwardButton.Pressed += () => _controller?.StepForward();
        if (_speedOption is not null) _speedOption.ItemSelected += HandleSpeedSelected;
        if (_viewOption is not null) _viewOption.ItemSelected += HandleViewSelected;
        if (_timeline is not null) _timeline.ValueChanged += HandleTimelineChanged;
    }

    private void UnwireSignals()
    {
        if (_controller is not null)
        {
            _controller.OnReplayChanged -= Refresh;
            _controller.OnReplayError -= HandleReplayError;
        }
        if (_fileDialog is not null) _fileDialog.FileSelected -= HandleFileSelected;
        if (_openButton is not null) _openButton.Pressed -= OpenFileDialog;
        if (_loadButton is not null) _loadButton.Pressed -= LoadEditedPath;
        if (_closeButton is not null) _closeButton.Pressed -= HandleClose;
        if (_playPauseButton is not null) _playPauseButton.Pressed -= TogglePlayback;
        if (_speedOption is not null) _speedOption.ItemSelected -= HandleSpeedSelected;
        if (_viewOption is not null) _viewOption.ItemSelected -= HandleViewSelected;
        if (_timeline is not null) _timeline.ValueChanged -= HandleTimelineChanged;
    }

    private void ApplyStyle()
    {
        CyberStyle.ApplyRootTheme(this);
        CyberStyle.ApplyButton(_openButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_loadButton, CyberButtonKind.Primary);
        CyberStyle.ApplyButton(_closeButton, CyberButtonKind.Danger);
        CyberStyle.ApplyButton(_playPauseButton, CyberButtonKind.Primary);
        CyberStyle.ApplyButton(_stepBackButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_stepForwardButton, CyberButtonKind.Action);
        CyberStyle.ApplyOptionButton(_speedOption);
        CyberStyle.ApplyOptionButton(_viewOption);
    }

    private void OpenFileDialog() => _fileDialog?.PopupCenteredRatio(0.72f);

    private void HandleFileSelected(string path)
    {
        if (_pathEdit is not null) _pathEdit.Text = path;
        LoadPath(path);
    }

    private void LoadEditedPath() => LoadPath(_pathEdit?.Text ?? string.Empty);

    public bool OpenReplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            HandleReplayError("请选择 .cysreplay 录像文件。");
            return false;
        }
        if (_pathEdit is not null) _pathEdit.Text = path;
        if (_controller?.LoadReplay(path) == true)
        {
            PopulateViewOptions();
            return true;
        }
        return false;
    }

    private void LoadPath(string path) => OpenReplay(path);

    private void PopulateViewOptions()
    {
        if (_viewOption is null || _controller?.Session is not ReplayPlaybackSession session)
        {
            return;
        }

        _updatingControls = true;
        _viewOption.Clear();
        if (!session.Header.IsOmniscient)
        {
            string label = session.Header.ViewerSeatId > 0
                ? $"玩家 {session.Header.ViewerSeatId}（录像锁定）"
                : "公开视角（录像锁定）";
            _viewOption.AddItem(label);
            _viewOption.SetItemMetadata(0, session.Header.ViewerSeatId > 0 ? $"player:{session.Header.ViewerSeatId}" : "public");
            _viewOption.Disabled = true;
        }
        else
        {
            AddViewOption("全知视角", "omniscient");
            AddViewOption("公开观战视角", "public");
            foreach (ReplayPlayerInfo player in session.Header.Players.OrderBy(player => player.SeatId))
            {
                AddViewOption($"{player.SeatId} 号位 · {player.DisplayName}", $"player:{player.SeatId}");
            }
            _viewOption.Disabled = false;
        }
        _viewOption.Select(0);
        _updatingControls = false;
        Refresh();
    }

    private void AddViewOption(string label, string key)
    {
        _viewOption!.AddItem(label);
        _viewOption.SetItemMetadata(_viewOption.ItemCount - 1, key);
    }

    private void TogglePlayback()
    {
        if (_controller?.Session is not ReplayPlaybackSession session) return;
        if (session.IsPaused) _controller.Play(); else _controller.Pause();
    }

    private void HandleSpeedSelected(long index)
    {
        if (_updatingControls || _speedOption is null) return;
        _controller?.SetSpeed(_speedOption.GetItemMetadata((int)index).AsDouble());
    }

    private void HandleViewSelected(long index)
    {
        if (_updatingControls || _viewOption is null || _controller is null) return;
        string key = _viewOption.GetItemMetadata((int)index).AsString();
        try
        {
            if (key == "omniscient") _controller.ShowOmniscientView();
            else if (key == "public") _controller.ShowPublicView();
            else if (key.StartsWith("player:", StringComparison.Ordinal)
                && int.TryParse(key[7..], out int seatId)) _controller.ShowPlayerView(seatId);
        }
        catch (Exception exception)
        {
            HandleReplayError(exception.Message);
        }
    }

    private void HandleTimelineChanged(double value)
    {
        if (_updatingControls || _controller?.Session is not ReplayPlaybackSession session) return;
        long cursor = (long)Math.Round(value);
        if (cursor != session.Cursor) _controller.Seek(cursor);
    }

    private void HandleReplayError(string message)
    {
        if (_summaryLabel is not null) _summaryLabel.Text = $"录像载入失败：{message}";
    }

    private void HandleClose()
    {
        _controller?.Pause();
        CloseRequested?.Invoke();
    }

    private void Refresh()
    {
        if (_controller?.Session is not ReplayPlaybackSession session)
        {
            SetEmptyState();
            return;
        }

        GameView view = session.CurrentView;
        _updatingControls = true;
        if (_speedOption is not null)
        {
            for (int index = 0; index < _speedOption.ItemCount; index++)
            {
                if (Math.Abs(_speedOption.GetItemMetadata(index).AsDouble() - session.Speed) < 0.001)
                {
                    _speedOption.Select(index);
                    break;
                }
            }
        }
        if (_viewOption is not null && !_viewOption.Disabled)
        {
            string viewerKey = session.Viewer.Role switch
            {
                ViewerRole.OmniscientReplay => "omniscient",
                ViewerRole.Spectator => "public",
                ViewerRole.Player when session.Viewer.SeatId is int seatId => $"player:{seatId}",
                _ => string.Empty
            };
            for (int index = 0; index < _viewOption.ItemCount; index++)
            {
                if (string.Equals(_viewOption.GetItemMetadata(index).AsString(), viewerKey, StringComparison.Ordinal))
                {
                    _viewOption.Select(index);
                    break;
                }
            }
        }
        if (_timeline is not null)
        {
            _timeline.MaxValue = Math.Max(1, session.MaximumCursor);
            _timeline.Value = session.Cursor;
        }
        if (_playPauseButton is not null) _playPauseButton.Text = session.IsPaused ? "播放" : "暂停";
        if (_stepBackButton is not null) _stepBackButton.Disabled = session.Cursor == 0;
        if (_stepForwardButton is not null) _stepForwardButton.Disabled = session.Cursor >= session.MaximumCursor;
        _updatingControls = false;

        string privacy = session.Header.IsOmniscient ? "完整录像" : "有限视角录像";
        if (_summaryLabel is not null)
        {
            _summaryLabel.Text = $"{privacy}　模式 {view.ModeId}　进度 {session.Cursor}/{session.MaximumCursor}　"
                + $"第 {view.RoundNumber} 轮 · {PhaseText(view.Phase)}　当前 {view.CurrentSeatId} 号位　状态 {view.Status}";
        }

        RuleJournalEntry? entry = session.CurrentEntry;
        if (_eventLabel is not null)
        {
            _eventLabel.Text = entry is null
                ? "尚未推进到规则事件。"
                : $"事件 #{entry.Sequence}　{entry.Kind}\n"
                    + (entry.RuleEvent is null
                        ? entry.Message
                        : $"{entry.RuleEvent.Kind} · {entry.RuleEvent.Stage}");
        }

        if (_playersLabel is not null) _playersLabel.Text = BuildPlayersText(view);
        if (_choiceLabel is not null) _choiceLabel.Text = BuildChoiceText(view);
    }

    private void SetEmptyState()
    {
        if (_summaryLabel is not null) _summaryLabel.Text = "打开一个 .cysreplay 文件开始离线回放。";
        if (_eventLabel is not null) _eventLabel.Text = "录像事件将在此显示。";
        if (_playersLabel is not null) _playersLabel.Text = "[b]席位状态[/b]\n尚未载入录像";
        if (_choiceLabel is not null) _choiceLabel.Text = "当前选择\n尚未载入录像";
        if (_playPauseButton is not null) _playPauseButton.Disabled = true;
        if (_stepBackButton is not null) _stepBackButton.Disabled = true;
        if (_stepForwardButton is not null) _stepForwardButton.Disabled = true;
        if (_viewOption is not null) _viewOption.Disabled = true;
    }

    private static string BuildPlayersText(GameView view)
    {
        StringBuilder builder = new("[b]席位状态[/b]\n");
        foreach (PlayerView player in view.Players.OrderBy(player => player.SeatId))
        {
            string role = player.IsRoleVisible ? player.Role.ToString() : "身份隐藏";
            builder.Append(player.SeatId).Append(" 号位　")
                .Append(player.DisplayName).Append(" / ").Append(player.GeneralId).Append('\n')
                .Append("　体力 ").Append(player.Health).Append('/').Append(player.MaxHealth)
                .Append("　手牌 ").Append(player.HandCount)
                .Append("　").Append(role)
                .Append(player.IsAlive ? string.Empty : "　[已阵亡]")
                .Append('\n');
        }
        return builder.ToString();
    }

    private static string BuildChoiceText(GameView view)
    {
        if (view.PendingChoice is null)
        {
            return $"当前选择\n无\n\n牌堆：{view.DrawPileCount}\n弃牌堆：{view.DiscardPileCount}";
        }
        return $"当前选择\n{view.PendingChoice.Kind}\n操作者：{view.PendingChoice.ActingSeatId} 号位\n"
            + $"合法选项：{view.PendingChoice.Options.Count}\n状态版本：{view.PendingChoice.StateRevision}\n\n"
            + $"牌堆：{view.DrawPileCount}\n弃牌堆：{view.DiscardPileCount}";
    }

    private static string PhaseText(GamePhase phase) => phase switch
    {
        GamePhase.Preparation => "准备阶段",
        GamePhase.Judgement => "判定阶段",
        GamePhase.Draw => "摸牌阶段",
        GamePhase.Play => "出牌阶段",
        GamePhase.Discard => "弃牌阶段",
        GamePhase.End => "结束阶段",
        _ => phase.ToString()
    };
}
