namespace CiyuanSha.Gameplay.Actions;

/// <summary>
/// High-level card-use stages aligned with the rules flow.
/// </summary>
public enum CardUseStage
{
    SelectingTargets = 0,
    UsingCard = 1,
    TargetSpecified = 2,
    BecameTarget = 3,
    TargetsConfirmed = 4,
    ResolveCardEffect = 5,
    ResolveFinished = 6
}
