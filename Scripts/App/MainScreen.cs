using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using CiyuanSha.Replay;
using CiyuanSha.UI;
using Godot;

namespace CiyuanSha.App;

/// <summary>
/// Root UI coordinator that switches between the LAN lobby and the in-match board.
/// </summary>
public partial class MainScreen : Control
{
    [Export]
    public NodePath LobbyRootPath { get; set; } = new NodePath();

    [Export]
    public NodePath MatchRootPath { get; set; } = new NodePath();

    [Export]
    public NodePath ReplayRootPath { get; set; } = new NodePath();

    [Export]
    public NodePath ReplayOpenButtonPath { get; set; } = new NodePath();

    private Control? _lobbyRoot;
    private Control? _matchRoot;
    private Control? _replayRoot;
    private ReplayViewerPanel? _replayPanel;
    private Button? _replayOpenButton;
    private ColorRect? _transitionCurtain;
    private Tween? _transitionTween;
    private bool _initialized;
    private bool _showingMatch;
    private bool _showingReplay;

    public override void _Ready()
    {
        _lobbyRoot = GetNodeOrNull<Control>(LobbyRootPath);
        _matchRoot = GetNodeOrNull<Control>(MatchRootPath);
        _replayRoot = GetNodeOrNull<Control>(ReplayRootPath);
        _replayPanel = _replayRoot as ReplayViewerPanel;
        _replayOpenButton = GetNodeOrNull<Button>(ReplayOpenButtonPath);
        _transitionCurtain = GetNodeOrNull<ColorRect>("TransitionCurtain");
        CyberStyle.ApplyRootTheme(this);
        CyberStyle.ApplyButton(_replayOpenButton, CyberButtonKind.Action);
        if (_replayOpenButton is not null)
        {
            _replayOpenButton.Pressed += ShowReplay;
        }
        if (_replayPanel is not null)
        {
            _replayPanel.CloseRequested += HideReplay;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnMatchRunningChanged += HandleMatchRunningChanged;
        }

        RefreshVisibleRoots();
    }

    public override void _ExitTree()
    {
        if (_replayOpenButton is not null)
        {
            _replayOpenButton.Pressed -= ShowReplay;
        }
        if (_replayPanel is not null)
        {
            _replayPanel.CloseRequested -= HideReplay;
        }
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnMatchRunningChanged -= HandleMatchRunningChanged;
        }
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        RefreshVisibleRoots();
    }

    private void HandleMatchRunningChanged(bool isRunning)
    {
        RefreshVisibleRoots();
    }

    private void RefreshVisibleRoots()
    {
        bool matchRunning = GameManager.Instance?.IsMatchRunning == true;
		if (matchRunning)
		{
			_showingReplay = false;
		}
		bool changed = _initialized && matchRunning != _showingMatch;
		_showingMatch = matchRunning;
		_initialized = true;

        if (_lobbyRoot is not null)
        {
            _lobbyRoot.Visible = !matchRunning && !_showingReplay;
        }

        if (_matchRoot is not null)
        {
            _matchRoot.Visible = matchRunning && !_showingReplay;
        }

        if (_replayRoot is not null)
        {
            _replayRoot.Visible = _showingReplay && !matchRunning;
        }

        if (_replayOpenButton is not null)
        {
            _replayOpenButton.Visible = !matchRunning && !_showingReplay;
        }

		if (changed)
		{
			PlaySceneReveal();
		}
    }

	private void PlaySceneReveal()
	{
		if (_transitionCurtain is null || !IsInsideTree())
		{
			return;
		}

		_transitionTween?.Kill();
		_transitionCurtain.Visible = true;
		_transitionCurtain.Modulate = Colors.White;
		_transitionTween = CreateTween();
		_transitionTween.TweenProperty(_transitionCurtain, "modulate", new Color(1f, 1f, 1f, 0f), 0.32f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
		_transitionTween.Finished += () =>
		{
			if (_transitionCurtain is not null)
			{
				_transitionCurtain.Visible = false;
			}
		};
	}

	private void ShowReplay()
	{
		_showingReplay = true;
		RefreshVisibleRoots();
	}

	private void HideReplay()
	{
		_showingReplay = false;
		RefreshVisibleRoots();
	}
}
