using CiyuanSha.Gameplay.Cards;

namespace CiyuanSha.UI;

public static class CardDisplayFormatter
{
	public static string FormatCardFace(CardInstance card)
	{
		return $"{FormatCardIdentity(card)}\n\n{FormatCardDisplayName(card)}\n\n{FormatCardCategory(card.CardType)}";
	}

	public static string FormatVirtualCardFace(CardType cardType, string displayName, string subtitle)
	{
		return $"视为【{FormatCardTypeName(cardType)}】\n\n{displayName}\n\n{subtitle}";
	}

	public static string FormatCardIdentity(CardInstance card)
	{
		string rank = card.Rank switch
		{
			1 => "A",
			11 => "J",
			12 => "Q",
			13 => "K",
			<= 0 => "?",
			_ => card.Rank.ToString()
		};
		return $"{FormatSuitSymbol(card.Suit)} {rank}";
	}

	public static string FormatSuitSymbol(CardSuit suit)
	{
		return suit switch
		{
			CardSuit.Spade => "♠",
			CardSuit.Heart => "♥",
			CardSuit.Club => "♣",
			CardSuit.Diamond => "♦",
			_ => "?"
		};
	}

	public static string FormatCardDisplayName(CardInstance card)
	{
		return FormatCardTypeName(card.CardType, card.DisplayName);
	}

	public static string FormatCardTypeName(CardType cardType, string fallback = "")
	{
		return cardType switch
		{
			CardType.Slash => "杀",
			CardType.FireSlash => "火杀",
			CardType.ThunderSlash => "雷杀",
			CardType.Dodge => "闪",
			CardType.Peach => "桃",
			CardType.Wine => "酒",
			CardType.Dismantle => "过河拆桥",
			CardType.Snatch => "顺手牵羊",
			CardType.Duel => "决斗",
			CardType.ExNihilo => "无中生有",
			CardType.Nullification => "无懈可击",
			CardType.Barbarians => "南蛮入侵",
			CardType.ArrowBarrage => "万箭齐发",
			CardType.PeachGarden => "桃园结义",
			CardType.Harvest => "五谷丰登",
			CardType.Indulgence => "乐不思蜀",
			CardType.SupplyShortage => "兵粮寸断",
			CardType.Lightning => "闪电",
			CardType.IronChain => "铁索连环",
			CardType.FireAttack => "火攻",
			CardType.BorrowSword => "借刀杀人",
			CardType.Weapon or CardType.Armor or CardType.OffensiveHorse or CardType.DefensiveHorse or CardType.Treasure => fallback,
			_ => string.IsNullOrWhiteSpace(fallback) ? cardType.ToString() : fallback
		};
	}

	public static string FormatCardCategory(CardType cardType)
	{
		if (CardRules.IsEquipment(cardType))
		{
			return "装备牌";
		}

		if (CardRules.IsSlash(cardType) || cardType is CardType.Dodge or CardType.Peach or CardType.Wine)
		{
			return "基本牌";
		}

		if (cardType is CardType.Indulgence or CardType.SupplyShortage or CardType.Lightning)
		{
			return "延时锦囊";
		}

		if (cardType == CardType.None)
		{
			return "未知";
		}

		return "锦囊牌";
	}
}
