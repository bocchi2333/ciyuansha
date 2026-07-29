using System;
using System.Threading.Tasks;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Replay;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Replay;

/// <summary>Godot-facing offline replay controls; no networking or bot policy is started.</summary>
public partial class ReplayController : Node
{
    private double _stepAccumulator;

    public ReplayPlaybackSession? Session { get; private set; }

    public event Action? OnReplayChanged;

    public event Action<string>? OnReplayError;

    public override void _Process(double delta)
    {
        if (Session is null || Session.IsPaused)
        {
            return;
        }
        _stepAccumulator += delta;
        double interval = Session.Speed == 0.5 ? 1.0 : 0.5;
        if (_stepAccumulator < interval)
        {
            return;
        }
        _stepAccumulator = 0;
        if (Session.Tick() > 0)
        {
            OnReplayChanged?.Invoke();
        }
    }

    public bool LoadReplay(string path)
    {
        try
        {
            GameCoreRuntime runtime = GameManager.Instance?.CoreRuntime ?? GameCoreRuntime.LoadDefault();
            ReplayDocument document = Task.Run(() => ReplayArchive.ReadAsync(path)).GetAwaiter().GetResult();
            Session = new ReplayPlaybackSession(document, runtime.Content);
            _stepAccumulator = 0;
            OnReplayChanged?.Invoke();
            return true;
        }
        catch (Exception exception)
        {
            Session = null;
            OnReplayError?.Invoke(exception.Message);
            GD.PushWarning($"Replay load failed: {exception.Message}");
            return false;
        }
    }

    public void Play()
    {
        Session?.Play();
        OnReplayChanged?.Invoke();
    }

    public void Pause()
    {
        Session?.Pause();
        OnReplayChanged?.Invoke();
    }

    public void SetSpeed(double speed)
    {
        Session?.SetSpeed(speed);
        OnReplayChanged?.Invoke();
    }

    public void StepForward()
    {
        Session?.StepForward();
        OnReplayChanged?.Invoke();
    }

    public void StepBackward()
    {
        Session?.StepBackward();
        OnReplayChanged?.Invoke();
    }

    public void Seek(long sequence)
    {
        Session?.Seek(sequence);
        OnReplayChanged?.Invoke();
    }

    public void ShowPublicView() => SetViewer(ViewerContext.Spectator);

    public void ShowOmniscientView() => SetViewer(ViewerContext.OmniscientReplay);

    public void ShowPlayerView(int seatId) => SetViewer(ViewerContext.ForPlayer(seatId));

    private void SetViewer(ViewerContext viewer)
    {
        Session?.SetViewer(viewer);
        OnReplayChanged?.Invoke();
    }
}
