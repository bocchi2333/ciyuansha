using System.Collections.Generic;
using CiyuanSha.UI;
using Godot;

namespace CiyuanSha.Test;

public partial class TextLocalizationSmokeTest : Node
{
    public override void _Ready()
    {
        List<(string Source, string Expected)> battleCases = new()
        {
            ("Match started.", "对局开始。"),
            ("张辽 draws 2 card(s).", "张辽 摸了 2 张牌。"),
            ("Choose one card from Harvest. Remaining: 3.", "请从【五谷丰登】中选择一张牌，剩余 3 张。"),
            ("Play Dodge for 赵云 or pass.", "请为 赵云 打出【闪】，或放弃响应。"),
            ("Target is out of range. Distance 3, range 1.", "目标超出攻击范围：距离 3，范围 1。"),
            ("司马懿's Lightning strikes for 3 thunder damage.", "司马懿 的【闪电】判定生效，造成 3 点雷电伤害。"),
            ("赵云's Serpent Spear treats two hand cards as Slash.", "赵云 发动【丈八蛇矛】，将两张手牌当【杀】使用。"),
            ("The match ended in a draw.", "本局以平局结束。")
        };

        foreach ((string source, string expected) in battleCases)
        {
            string actual = BattleLogTextLocalizer.Localize(source);
            if (actual != expected)
            {
                Fail($"Battle text mismatch. Source='{source}', Actual='{actual}', Expected='{expected}'.");
                return;
            }
        }

        string networkActual = NetworkTextLocalizer.Localize("Disconnected from the LAN host.");
        if (networkActual != "与局域网房主的连接已中断。")
        {
            Fail($"Network text mismatch: '{networkActual}'.");
            return;
        }

        GD.Print("[text-localization-smoke] PASS: battle, prompt, result, and network text verified.");
        GetTree().Quit(0);
    }

    private void Fail(string message)
    {
        GD.PushError($"[text-localization-smoke] FAIL: {message}");
        GetTree().Quit(1);
    }
}
