using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CiyuanSha.UI;

internal static class BattleLogTextLocalizer
{
    private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> Patterns = new[]
    {
        MakePattern("^Match started\\.$", "对局开始。"),
        MakePattern("^Turn start: peer (?<peer>\\d+)\\.$", "玩家 ${peer} 的回合开始。"),
        MakePattern("^(?<name>.+?) receives (?<count>\\d+) opening card\\(s\\)\\.$", "${name} 获得 ${count} 张起始手牌。"),
        MakePattern("^(?<name>.+?) draws (?<count>\\d+) card\\(s\\)\\.$", "${name} 摸了 ${count} 张牌。"),
        MakePattern("^(?<name>.+?) recovers (?<count>\\d+) health\\.$", "${name} 回复 ${count} 点体力。"),
        MakePattern("^(?<name>.+?) enters dying state\\.$", "${name} 进入濒死状态。"),
        MakePattern("^(?<name>.+?) is defeated by (?<source>.+?)\\.$", "${name} 被 ${source} 击败。"),
        MakePattern("^(?<name>.+?) skips (?<phase>DrawPhase|PlayPhase|DiscardPhase)\\.$", "${name} 跳过${phase}。"),
        MakePattern("^(?<name>.+?) discards (?<card>.+?)\\.$", "${name} 弃置了【${card}】。"),
        MakePattern("^(?<name>.+?) equips (?<card>.+?)\\.$", "${name} 装备了【${card}】。"),
        MakePattern("^(?<source>.+?) uses (?<card>.+?) on (?<target>.+?)\\.$", "${source} 对 ${target} 使用【${card}】。"),
        MakePattern("^(?<source>.+?) uses (?<card>.+?)\\.$", "${source} 使用了【${card}】。"),
        MakePattern("^(?<name>.+?) manually responds with Dodge\\.$", "${name} 打出【闪】响应。"),
        MakePattern("^(?<name>.+?) responds with (?<card>.+?)\\.$", "${name} 打出【${card}】响应。"),
        MakePattern("^(?<name>.+?) declines to respond\\.$", "${name} 放弃响应。"),
        MakePattern("^(?<name>.+?) has no excess hand cards to discard\\.$", "${name} 无需弃牌。"),
        MakePattern("^The discard pile is reshuffled into the draw pile\\.$", "弃牌堆已重新洗入牌堆。"),
        MakePattern("^(?<name>.+?) recasts (?<card>.+?)\\.$", "${name} 重铸了【${card}】。"),
        MakePattern("^(?<source>.+?) places (?<card>.+?) on (?<target>.+?)\\.$", "${source} 将【${card}】置于 ${target} 的判定区。"),
        MakePattern("^(?<name>.+?) activates (?<skill>.+?)\\.$", "${name} 发动了技能【${skill}】。"),
        MakePattern("^(?<name>.+?) discards (?<count>\\d+) card\\(s\\) on defeat\\.$", "${name} 阵亡并弃置 ${count} 张牌。"),
        MakePattern("^(?<name>.+?) gains the Rebel defeat reward\\.$", "${name} 获得击败反贼的奖励。"),
        MakePattern("^(?<name>.+?) has no cards to lose for defeating a Loyalist\\.$", "${name} 击败忠臣，但没有牌可弃置。"),
        MakePattern("^(?<name>.+?) loses all cards for defeating a Loyalist\\.$", "${name} 因击败忠臣而弃置所有牌。"),
        MakePattern("^(?<name>.+?) must discard (?<count>\\d+) card\\(s\\) down to hand limit (?<limit>\\d+)\\.$", "${name} 需弃置 ${count} 张牌，将手牌降至上限 ${limit}。"),
        MakePattern("^(?<name>.+?) keeps all cards within hand limit (?<limit>\\d+)\\.$", "${name} 的手牌未超过上限 ${limit}。"),
        MakePattern("^(?<name>.+?) has reached hand limit (?<limit>\\d+)\\.$", "${name} 已将手牌弃至上限 ${limit}。"),
        MakePattern("^(?<name>.+?) cannot receive (?<card>.+?)\\.$", "${name} 的判定区无法放置【${card}】。"),
        MakePattern("^(?<name>.+?) has no (?<card>.+?) to respond with\\.$", "${name} 没有可用于响应的【${card}】。"),
        MakePattern("^(?<name>.+?)'s Eight Diagram provides a Dodge\\.$", "${name} 的【八卦阵】判定生效，视为打出【闪】。"),
        MakePattern("^(?<name>.+?) uses Harvest, but the draw pile is empty\\.$", "${name} 使用【五谷丰登】，但牌堆为空。"),
        MakePattern("^(?<name>.+?) reveals (?<count>\\d+) card\\(s\\) for Harvest: (?<cards>.+?)\\.$", "${name} 为【五谷丰登】亮出 ${count} 张牌：${cards}。"),
        MakePattern("^(?<name>.+?) is choosing a card from Harvest\\.$", "${name} 正在从【五谷丰登】中选择一张牌。"),
        MakePattern("^Harvest selection finished\\.$", "【五谷丰登】选牌结束。"),
        MakePattern("^(?<name>.+?) takes (?<card>.+?) from Harvest\\.$", "${name} 从【五谷丰登】中获得【${card}】。"),
        MakePattern("^Choose one card from Harvest\\. Remaining: (?<count>\\d+)\\.$", "请从【五谷丰登】中选择一张牌，剩余 ${count} 张。"),
        MakePattern("^(?<name>.+?) has no selectable card\\.$", "${name} 没有可选择的牌。"),
        MakePattern("^(?<name>.+?) is choosing one card from (?<target>.+?)\\.$", "${name} 正在选择 ${target} 区域内的一张牌。"),
        MakePattern("^(?<name>.+?)'s selected card is no longer available\\.$", "${name} 选择的牌已不可用。"),
        MakePattern("^(?<name>.+?) has no selectable hand card\\.$", "${name} 没有可选择的手牌。"),
        MakePattern("^(?<name>.+?) is choosing one hand card\\.$", "${name} 正在选择一张手牌。"),
        MakePattern("^(?<name>.+?)'s selected hand card is no longer available\\.$", "${name} 选择的手牌已不可用。"),
        MakePattern("^(?<card>.+?) on (?<target>.+?) is cancelled by (?<name>.+?)'s Nullification\\.$", "${name} 使用【无懈可击】，抵消了 ${target} 的【${card}】。"),
        MakePattern("^(?<card>.+?) is cancelled by (?<name>.+?)'s Nullification\\.$", "${name} 使用【无懈可击】，抵消了【${card}】。"),
        MakePattern("^(?<name>.+?)'s Nullification cancels (?<target>.+?)'s Nullification\\.$", "${name} 的【无懈可击】抵消了 ${target} 的【无懈可击】。"),
        MakePattern("^(?<sequence>.+?) response sequence finished\\.$", "${sequence}响应流程结束。"),
        MakePattern("^(?<name>.+?)'s Indulgence judgement succeeds\\. PlayPhase is not skipped\\.$", "${name} 的【乐不思蜀】判定未生效，不跳过出牌阶段。"),
        MakePattern("^(?<name>.+?)'s Indulgence resolves: skip PlayPhase\\.$", "${name} 的【乐不思蜀】生效，跳过出牌阶段。"),
        MakePattern("^(?<name>.+?)'s Supply Shortage judgement succeeds\\. DrawPhase is not skipped\\.$", "${name} 的【兵粮寸断】判定未生效，不跳过摸牌阶段。"),
        MakePattern("^(?<name>.+?)'s Supply Shortage resolves: skip DrawPhase\\.$", "${name} 的【兵粮寸断】生效，跳过摸牌阶段。"),
        MakePattern("^(?<name>.+?)'s Lightning strikes for 3 thunder damage\\.$", "${name} 的【闪电】判定生效，造成 3 点雷电伤害。"),
        MakePattern("^(?<name>.+?)'s Lightning passes to (?<target>.+?)\\.$", "${name} 的【闪电】移动至 ${target} 的判定区。"),
        MakePattern("^(?<name>.+?)'s Lightning dissipates\\.$", "${name} 的【闪电】离开牌局。"),
        MakePattern("^(?<name>.+?)'s (?<reason>.+?) judgement cannot be performed because the draw pile is empty\\.$", "牌堆为空，无法进行 ${name} 的【${reason}】判定。"),
        MakePattern("^(?<name>.+?) judges (?<card>.+?) for (?<reason>.+?): success\\.$", "${name} 为【${reason}】判定【${card}】：成功。"),
        MakePattern("^(?<name>.+?) judges (?<card>.+?) for (?<reason>.+?): failure\\.$", "${name} 为【${reason}】判定【${card}】：失败。"),
        MakePattern("^(?<name>.+?) failed to activate (?<skill>.+?): (?<reason>.+)$", "${name} 无法发动【${skill}】：${reason}"),
        MakePattern("^(?<name>.+?)'s Serpent Spear treats two hand cards as Slash\\.$", "${name} 发动【丈八蛇矛】，将两张手牌当【杀】使用。"),
        MakePattern("^(?<name>.+?)'s Silver Lion leaves equipment area, but they are already at full health\\.$", "${name} 的【白银狮子】离开装备区，但体力已满。"),
        MakePattern("^(?<name>.+?)'s Silver Lion leaves equipment area and heals 1 HP\\.$", "${name} 的【白银狮子】离开装备区，回复 1 点体力。"),
        MakePattern("^(?<name>.+?) has no card to discard\\.$", "${name} 没有牌可弃置。"),
        MakePattern("^(?<source>.+?) discards one card from (?<target>.+?)\\.$", "${source} 弃置 ${target} 区域内的一张牌。"),
        MakePattern("^(?<name>.+?) has no card to steal\\.$", "${name} 没有牌可被获得。"),
        MakePattern("^(?<source>.+?) steals one card from (?<target>.+?)\\.$", "${source} 获得 ${target} 区域内的一张牌。"),
        MakePattern("^(?<source>.+?) steals (?<target>.+?)'s (?<card>.+?)\\.$", "${source} 获得 ${target} 的【${card}】。"),
        MakePattern("^(?<name>.+?)'s damage is prevented\\.$", "对 ${name} 的伤害被防止。"),
        MakePattern("^(?<name>.+?)'s Vine Armor increases fire damage by 1\\.$", "${name} 的【藤甲】令火焰伤害增加 1 点。"),
        MakePattern("^(?<name>.+?)'s Silver Lion reduces damage to 1\\.$", "${name} 的【白银狮子】将伤害减至 1 点。"),
        MakePattern("^(?<name>.+?)'s (?<armor>.+?) reduces damage by (?<count>\\d+)\\.$", "${name} 的【${armor}】令伤害减少 ${count} 点。"),
        MakePattern("^(?<name>.+?)'s armor is ignored\\.$", "${name} 的防具效果被无视。"),
        MakePattern("^(?<name>.+?)'s Vine Armor prevents damage from (?<card>.+?)\\.$", "${name} 的【藤甲】防止了【${card}】造成的伤害。"),
        MakePattern("^(?<name>.+?)'s chain conducts (?<type>.+?) damage\\.$", "${name} 的铁索状态传导了${type}伤害。"),
        MakePattern("^(?<name>.+?) receives chained (?<type>.+?) damage\\.$", "${name} 受到传导的${type}伤害。"),
        MakePattern("^(?<name>.+?)'s Vine Armor makes (?<card>.+?) ineffective\\.$", "${name} 的【藤甲】令【${card}】无效。"),
        MakePattern("^(?<name>.+?)'s Renwang Shield blocks (?<source>.+?)'s Slash\\.$", "${name} 的【仁王盾】抵消了 ${source} 的【杀】。"),
        MakePattern("^(?<name>.+?)'s (?<card>.+?) is cancelled because it has no valid target\\.$", "${name} 的【${card}】因没有合法目标而取消。"),
        MakePattern("^(?<name>.+?) is chained by Iron Chain\\.$", "${name} 被【铁索连环】横置。"),
        MakePattern("^(?<name>.+?) is unchained by Iron Chain\\.$", "${name} 被【铁索连环】重置。"),
        MakePattern("^(?<source>.+?) dismantles (?<card>.+?) from (?<target>.+?)\\.$", "${source} 用【过河拆桥】弃置了 ${target} 的【${card}】。"),
        MakePattern("^(?<source>.+?) snatches (?<card>.+?) from (?<target>.+?)\\.$", "${source} 用【顺手牵羊】获得了 ${target} 的【${card}】。"),
        MakePattern("^(?<name>.+?) reveals (?<card>.+?) \\[(?<suit>.+?)\\] for Fire Attack\\.$", "${name} 为【火攻】展示【${card}】（${suit}）。"),
        MakePattern("^(?<name>.+?) declines to discard for Fire Attack\\. No damage is dealt\\.$", "${name} 放弃为【火攻】弃牌，本次不造成伤害。"),
        MakePattern("^(?<source>.+?) challenges (?<target>.+?) to a Duel\\.$", "${source} 对 ${target} 发起【决斗】。"),
        MakePattern("^Duel ends because one side is no longer alive\\.$", "一方已阵亡，【决斗】结束。"),
        MakePattern("^(?<name>.+?) plays Slash in the Duel\\.$", "${name} 在【决斗】中打出【杀】。"),
        MakePattern("^(?<name>.+?) fails to play Slash in the Duel\\.$", "${name} 未能在【决斗】中打出【杀】。"),
        MakePattern("^(?<name>.+?) uses Wine to recover from dying\\.$", "${name} 使用【酒】脱离濒死。"),
        MakePattern("^(?<name>.+?) uses Wine\\. Their next Slash deals \\+1 damage\\.$", "${name} 使用【酒】，下一张【杀】的伤害 +1。"),
        MakePattern("^(?<name>.+?) wins the match\\.$", "${name} 获得本局胜利。"),
        MakePattern("^The match ended in a draw\\.$", "本局以平局结束。"),
        MakePattern("^The host returned the match to lobby\\.$", "房主已将牌局返回候战大厅。"),
        MakePattern("^Play Dodge for (?<name>.+?) or pass\\.$", "请为 ${name} 打出【闪】，或放弃响应。"),
        MakePattern("^Play Peach or Wine for (?<name>.+?) or pass\\.$", "请为 ${name} 使用【桃】或【酒】，或放弃救援。"),
        MakePattern("^Play Peach to save (?<name>.+?) or pass\\.$", "请使用【桃】救援 ${name}，或放弃救援。"),
        MakePattern("^Play Nullification to cancel (?<subject>.+?), or pass\\.$", "请打出【无懈可击】抵消${subject}，或放弃响应。"),
        MakePattern("^Discard down to hand limit\\.$", "请将手牌弃至当前手牌上限。"),
        MakePattern("^Play a card, activate a skill, or end PlayPhase\\.$", "请出牌、发动技能，或结束出牌阶段。"),
        MakePattern("^Source and target are required\\.$", "需要同时指定来源和目标。"),
        MakePattern("^Source or target is missing\\.$", "来源或目标缺失。"),
        MakePattern("^No match is currently running\\.$", "当前没有正在进行的牌局。"),
        MakePattern("^(?<name>.+?) is defeated\\.$", "${name} 已阵亡。"),
        MakePattern("^(?<name>.+?) is already defeated\\.$", "${name} 已经阵亡。"),
        MakePattern("^Slash must target another character\\.$", "【杀】必须以其他角色为目标。"),
        MakePattern("^Slash can only be used during PlayPhase\\.$", "【杀】只能在出牌阶段使用。"),
        MakePattern("^It is not this character's turn\\.$", "当前不是该角色的回合。"),
        MakePattern("^Target is not part of the active turn order\\.$", "该目标不在当前有效座次中。"),
        MakePattern("^Target is out of range\\. Distance (?<distance>\\d+), range (?<range>\\d+)\\.$", "目标超出攻击范围：距离 ${distance}，范围 ${range}。"),
        MakePattern("^You have already used Slash this turn\\.$", "本回合已经使用过【杀】。"),
        MakePattern("^Target is not alive\\.$", "目标已经阵亡。"),
        MakePattern("^This card does not use a target button\\.$", "这张牌不通过目标按钮使用。"),
        MakePattern("^This card must target another character\\.$", "这张牌必须以其他角色为目标。"),
        MakePattern("^(?<name>.+?) already has this delayed trick\\.$", "${name} 的判定区已有同名延时锦囊。"),
        MakePattern("^(?<name>.+?) has no hand cards for Fire Attack\\.$", "${name} 没有可供【火攻】展示的手牌。"),
        MakePattern("^(?<card>.+?) requires distance 1\\.$", "【${card}】只能对距离 1 的目标使用。"),
        MakePattern("^Skill owner mismatch\\.$", "技能拥有者不匹配。"),
        MakePattern("^This skill cannot be activated manually\\.$", "该技能不能主动发动。"),
        MakePattern("^ActionManager is not ready\\.$", "动作管理器尚未就绪。"),
        MakePattern("^(?<skill>.+?) can only be activated during your PlayPhase input window\\.$", "【${skill}】只能在你的出牌阶段发动。"),
        MakePattern("^(?<skill>.+?) has already been used this turn\\.$", "【${skill}】本回合已经发动过。"),
        MakePattern("^(?<skill>.+?) requires one hand card as cost\\.$", "发动【${skill}】需要弃置一张手牌。"),
        MakePattern("^(?<skill>.+?) requires one other alive target\\.$", "发动【${skill}】需要选择一名其他存活角色。"),
        MakePattern("^(?<skill>.+?) is not an active skill\\.$", "【${skill}】不是主动技能。"),
        MakePattern("^(?<skill>.+?) requires (?<min>\\d+)-(?<max>\\d+) target\\(s\\)\\.$", "【${skill}】需要选择 ${min} 至 ${max} 个目标。"),
        MakePattern("^(?<skill>.+?) requires a hand card cost\\.$", "发动【${skill}】需要支付手牌。")
    };

    private static readonly IReadOnlyDictionary<string, string> Terms = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DrawPhase"] = "摸牌阶段",
        ["PlayPhase"] = "出牌阶段",
        ["DiscardPhase"] = "弃牌阶段",
        ["Dodge"] = "闪",
        ["Slash"] = "杀",
        ["Nullification"] = "无懈可击",
        ["Peach"] = "桃",
        ["Wine"] = "酒",
        ["Lightning"] = "闪电",
        ["Fire Attack"] = "火攻",
        ["FireSlash"] = "火杀",
        ["ThunderSlash"] = "雷杀",
        ["Dismantle"] = "过河拆桥",
        ["Snatch"] = "顺手牵羊",
        ["Duel"] = "决斗",
        ["ExNihilo"] = "无中生有",
        ["Barbarians"] = "南蛮入侵",
        ["ArrowBarrage"] = "万箭齐发",
        ["PeachGarden"] = "桃园结义",
        ["Harvest"] = "五谷丰登",
        ["Indulgence"] = "乐不思蜀",
        ["Supply Shortage"] = "兵粮寸断",
        ["IronChain"] = "铁索连环",
        ["BorrowSword"] = "借刀杀人",
        ["Physical"] = "物理",
        ["Fire"] = "火焰",
        ["Thunder"] = "雷电",
        ["Spade"] = "黑桃",
        ["Heart"] = "红桃",
        ["Club"] = "梅花",
        ["Diamond"] = "方片"
    };

    public static string Localize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        string localized = message;
        foreach ((Regex pattern, string replacement) in Patterns)
        {
            if (!pattern.IsMatch(localized))
            {
                continue;
            }

            localized = pattern.Replace(localized, replacement);
            break;
        }

        foreach ((string english, string chinese) in Terms.OrderByDescending(term => term.Key.Length))
        {
            localized = localized.Replace(english, chinese, StringComparison.Ordinal);
        }

        return localized;
    }

    private static (Regex Pattern, string Replacement) MakePattern(string pattern, string replacement)
    {
        return (new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), replacement);
    }
}
