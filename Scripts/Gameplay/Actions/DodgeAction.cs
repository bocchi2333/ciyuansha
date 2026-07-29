using System;
using Godot;

namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// 表示目标打出【闪】来响应一次攻击或伤害指定。
/// 目前为即时完成动作，后续可扩展为播放动画或等待特效。
/// </summary>
public class DodgeAction : GameAction
{
    public Node? Responder { get; }

    public event Action<DodgeAction>? OnDodged;

    public DodgeAction(Node? responder)
    {
        Responder = responder;
    }

    public override void Update()
    {
        if (IsDone)
        {
            return;
        }

        string responderName = Responder?.Name ?? "UnknownResponder";
        GD.Print($"{responderName} played Dodge.");
        OnDodged?.Invoke(this);
        MarkDone();
    }
}
