using CiyuanSha.Gameplay.Core;

namespace CiyuanSha.Gameplay.Skills;

/// <summary>
/// Minimal active skill sample: once per turn, draw one card during PlayPhase.
/// </summary>
public sealed class ActiveDrawSkill : CharacterSkill
{
    public ActiveDrawSkill()
        : base("active_draw", "Focused Planning", "Once per turn during PlayPhase, draw 1 card.")
    {
    }

    public override SkillUsageScope UsageScope => SkillUsageScope.OncePerTurn;

    public override bool IsActiveSkill => true;

    public override bool CanActivate(GameManager gameManager, SkillActivationContext context, out string reason)
    {
        reason = string.Empty;
        if (Owner is null || context.Source != Owner)
        {
            reason = "Skill owner mismatch.";
            return false;
        }

        if (gameManager.CurrentPhase != TurnPhase.PlayPhase || !gameManager.IsAwaitingPlayerInput)
        {
            reason = $"{DisplayName} can only be activated during your PlayPhase input window.";
            return false;
        }

        if (!CanActivate())
        {
            reason = $"{DisplayName} has already been used this turn.";
            return false;
        }

        return true;
    }

    public override bool Activate(GameManager gameManager, SkillActivationContext context)
    {
        if (Owner is null || !TryConsumeUse(gameManager))
        {
            return false;
        }

        gameManager.DrawCardsForEffect(
            Owner,
            1,
            $"{Owner.CharacterName} activates {DisplayName} and draws {{count}} card(s).",
            $"{Owner.CharacterName} activates {DisplayName}.");
        return true;
    }
}
