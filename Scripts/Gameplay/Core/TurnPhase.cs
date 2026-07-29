namespace CiyuanSha.Gameplay.Core;

/// <summary>
/// 回合中的基础阶段。
/// </summary>
public enum TurnPhase
{
    /// <summary>标准六阶段中的准备阶段；TurnStart 为兼容旧调用的同值别名。</summary>
    PreparationPhase = 0,
    TurnStart = PreparationPhase,
    JudgementPhase = 1,
    DrawPhase = 2,
    PlayPhase = 3,
    DiscardPhase = 4,
    EndPhase = 5
}
