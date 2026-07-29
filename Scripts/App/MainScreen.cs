using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
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

    private Control? _lobbyRoot;
    private Control? _matchRoot;
    private ColorRect? _transitionCurtain;
    private Tween? _transitionTween;
    private bool _initialized;
    private bool _showingMatch;

    public override void _Ready()
    {
        _lobbyRoot = GetNodeOrNull<Control>(LobbyRootPath);
        _matchRoot = GetNodeOrNull<Control>(MatchRootPath);
        _transitionCurtain = GetNodeOrNull<ColorRect>("TransitionCurtain");
        CyberStyle.ApplyRootTheme(this);

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
		bool changed = _initialized && matchRunning != _showingMatch;
		_showingMatch = matchRunning;
		_initialized = true;

        if (_lobbyRoot is not null)
        {
            _lobbyRoot.Visible = !matchRunning;
        }

        if (_matchRoot is not null)
        {
            _matchRoot.Visible = matchRunning;
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
}
