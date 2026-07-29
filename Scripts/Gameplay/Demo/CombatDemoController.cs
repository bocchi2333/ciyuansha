using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using Godot;

namespace CiyuanSha.Gameplay.Demo;

/// <summary>
/// 鏈€灏忔垬鏂楃ず渚嬫帶鍒跺櫒銆?/// 婕旂ず鈥滄彁浜や激瀹冲姩浣?-> 鐩爣鑷姩鎵撳嚭銆愰棯銆?> 浼ゅ鍙栨秷鈥濈殑娴佺▼銆?/// </summary>
public partial class CombatDemoController : Node
{
	[Export]
	public NodePath SourcePath { get; set; } = new NodePath();

	[Export]
	public NodePath TargetPath { get; set; } = new NodePath();

	public override void _Ready()
	{
		if (GameManager.Instance is null)
		{
			GD.PushWarning("CombatDemoController requires GameManager.Instance.");
		}
	}

	/// <summary>
	/// 鍙敱鎸夐挳鎴栨祴璇曡緭鍏ヨ皟鐢紝鍙戣捣涓€娆″熀纭€鏀诲嚮銆?    /// </summary>
	public void DemoAttack()
	{
		GameManager? gameManager = GameManager.Instance;
		if (gameManager?.ActionManager is null)
		{
			GD.PushWarning("GameManager or ActionManager is not ready.");
			return;
		}

		Node? source = SourcePath.IsEmpty ? this : GetNodeOrNull<Node>(SourcePath);
		Node? target = TargetPath.IsEmpty ? null : GetNodeOrNull<Node>(TargetPath);
		if (target is null)
		{
			GD.PushWarning("CombatDemoController could not resolve a target node.");
			return;
		}

		DamageAction damageAction = new(gameManager.ActionManager, new DamageInfo(source, target, 1, DamageType.Physical));

		if (target is PlayerResponder responder)
		{
			responder.AttachToDamageAction(damageAction);
		}

		bool submitted = gameManager.SubmitPlayPhaseAction(damageAction);
		if (!submitted)
		{
			GD.PushWarning("DemoAttack failed because the game is not accepting play phase actions.");
		}
	}
}
