using System.Collections.Generic;

namespace CiyuanSha.Gameplay.Cards;

/// <summary>
/// Fixed deck identities for the current ruleset.
/// The list follows the standard deck plus the implemented military-expansion cards where possible.
/// </summary>
public static class StandardDeckDefinition
{
    public static IReadOnlyList<DeckCardDefinition> CreateDeck()
    {
        List<DeckCardDefinition> deck = new(capacity: 161);
        AddStandardBasicCards(deck);
        AddStandardTrickCards(deck);
        AddStandardEquipmentCards(deck);
        AddMilitaryExpansionCards(deck);
        return deck;
    }

    private static void AddStandardBasicCards(List<DeckCardDefinition> deck)
    {
        Add(deck, CardType.Slash, CardSuit.Spade, 7, 8, 8, 9, 9, 10, 10);
        Add(deck, CardType.Slash, CardSuit.Heart, 10, 10, 11);
        Add(deck, CardType.Slash, CardSuit.Club, 2, 3, 4, 5, 6, 7, 8, 8, 9, 9, 10, 10, 11, 11);
        Add(deck, CardType.Slash, CardSuit.Diamond, 6, 7, 8, 9, 10, 13);

        Add(deck, CardType.Dodge, CardSuit.Heart, 2, 2, 13);
        Add(deck, CardType.Dodge, CardSuit.Diamond, 2, 2, 3, 4, 5, 6, 8, 9, 10, 10, 11, 11);

        Add(deck, CardType.Peach, CardSuit.Heart, 3, 4, 7, 8, 9, 12);
        Add(deck, CardType.Peach, CardSuit.Diamond, 2, 12);
    }

    private static void AddStandardTrickCards(List<DeckCardDefinition> deck)
    {
        Add(deck, CardType.Duel, CardSuit.Spade, 1);
        Add(deck, CardType.Lightning, CardSuit.Spade, 1);
        Add(deck, CardType.Snatch, CardSuit.Spade, 3, 4, 11);
        Add(deck, CardType.Dismantle, CardSuit.Spade, 3, 4, 12);
        Add(deck, CardType.Indulgence, CardSuit.Spade, 6);
        Add(deck, CardType.Barbarians, CardSuit.Spade, 7, 13);
        Add(deck, CardType.Nullification, CardSuit.Spade, 11);

        Add(deck, CardType.PeachGarden, CardSuit.Heart, 1);
        Add(deck, CardType.ArrowBarrage, CardSuit.Heart, 1);
        Add(deck, CardType.Harvest, CardSuit.Heart, 3, 4);
        Add(deck, CardType.Indulgence, CardSuit.Heart, 6);
        Add(deck, CardType.ExNihilo, CardSuit.Heart, 7, 8, 9, 11);
        Add(deck, CardType.Dismantle, CardSuit.Heart, 12);
        Add(deck, CardType.Lightning, CardSuit.Heart, 12);

        Add(deck, CardType.Duel, CardSuit.Club, 1);
        Add(deck, CardType.Dismantle, CardSuit.Club, 3, 4);
        Add(deck, CardType.Indulgence, CardSuit.Club, 6);
        Add(deck, CardType.Barbarians, CardSuit.Club, 7);
        Add(deck, CardType.BorrowSword, CardSuit.Club, 12, 13);
        Add(deck, CardType.Nullification, CardSuit.Club, 12, 13);

        Add(deck, CardType.Duel, CardSuit.Diamond, 1);
        Add(deck, CardType.Snatch, CardSuit.Diamond, 3, 4);
        Add(deck, CardType.Nullification, CardSuit.Diamond, 12);
    }

    private static void AddStandardEquipmentCards(List<DeckCardDefinition> deck)
    {
        Weapon(deck, CardSuit.Spade, 2, "Ice Sword", EquipmentEffectType.IceSword, 2);
        Weapon(deck, CardSuit.Spade, 2, "Double Swords", EquipmentEffectType.DoubleSwords, 2);
        Armor(deck, CardSuit.Spade, 2, "Eight Diagram", EquipmentEffectType.EightDiagram);
        Horse(deck, CardSuit.Spade, 5, "Jueying", defensive: true);
        Weapon(deck, CardSuit.Spade, 5, "Green Dragon Blade", EquipmentEffectType.GreenDragonBlade, 3);
        Weapon(deck, CardSuit.Spade, 6, "Qinggang Sword", EquipmentEffectType.QinggangSword, 2);
        Weapon(deck, CardSuit.Spade, 12, "Serpent Spear", EquipmentEffectType.SerpentSpear, 3);
        Horse(deck, CardSuit.Spade, 13, "Dawan", defensive: false);

        Weapon(deck, CardSuit.Heart, 5, "Kylin Bow", EquipmentEffectType.KylinBow, 5);
        Horse(deck, CardSuit.Heart, 5, "Chitu", defensive: false);
        Horse(deck, CardSuit.Heart, 13, "Zhuahuangfeidian", defensive: true);

        Weapon(deck, CardSuit.Club, 1, "Crossbow", EquipmentEffectType.Crossbow, 1);
        Armor(deck, CardSuit.Club, 2, "Eight Diagram", EquipmentEffectType.EightDiagram);
        Armor(deck, CardSuit.Club, 2, "Renwang Shield", EquipmentEffectType.RenwangShield);
        Horse(deck, CardSuit.Club, 5, "Dilu", defensive: true);

        Weapon(deck, CardSuit.Diamond, 1, "Crossbow", EquipmentEffectType.Crossbow, 1);
        Weapon(deck, CardSuit.Diamond, 5, "Stone Axe", EquipmentEffectType.StoneAxe, 3);
        Weapon(deck, CardSuit.Diamond, 12, "Fangtian Halberd", EquipmentEffectType.FangtianHalberd, 4);
        Horse(deck, CardSuit.Diamond, 13, "Zixing", defensive: false);
    }

    private static void AddMilitaryExpansionCards(List<DeckCardDefinition> deck)
    {
        Add(deck, CardType.FireSlash, CardSuit.Heart, 4, 7, 10);
        Add(deck, CardType.FireSlash, CardSuit.Diamond, 4, 5);
        Add(deck, CardType.ThunderSlash, CardSuit.Spade, 4, 5, 6, 7, 8);
        Add(deck, CardType.ThunderSlash, CardSuit.Club, 5, 6, 7, 8);
        Add(deck, CardType.Slash, CardSuit.Heart, 8, 9, 11);
        Add(deck, CardType.Slash, CardSuit.Diamond, 12);

        Add(deck, CardType.Dodge, CardSuit.Heart, 12, 13);
        Add(deck, CardType.Dodge, CardSuit.Diamond, 6, 7, 8, 10, 11, 11, 12);
        Add(deck, CardType.Peach, CardSuit.Heart, 5, 6);
        Add(deck, CardType.Peach, CardSuit.Diamond, 2, 3);
        Add(deck, CardType.Wine, CardSuit.Spade, 3, 9);
        Add(deck, CardType.Wine, CardSuit.Club, 3, 9);
        Add(deck, CardType.Wine, CardSuit.Diamond, 9);

        Add(deck, CardType.FireAttack, CardSuit.Heart, 2, 3);
        Add(deck, CardType.FireAttack, CardSuit.Diamond, 12);
        Add(deck, CardType.IronChain, CardSuit.Spade, 11, 12);
        Add(deck, CardType.IronChain, CardSuit.Club, 10, 11, 12, 13);
        Add(deck, CardType.SupplyShortage, CardSuit.Spade, 10);
        Add(deck, CardType.SupplyShortage, CardSuit.Club, 4);

        Weapon(deck, CardSuit.Spade, 1, "Guding Blade", EquipmentEffectType.GudingBlade, 2);
        Weapon(deck, CardSuit.Diamond, 1, "Vermilion Fan", EquipmentEffectType.VermilionFan, 4);
        Armor(deck, CardSuit.Spade, 2, "Vine Armor", EquipmentEffectType.VineArmor);
        Armor(deck, CardSuit.Club, 2, "Vine Armor", EquipmentEffectType.VineArmor);
        Armor(deck, CardSuit.Club, 1, "Silver Lion", EquipmentEffectType.SilverLion);
        Horse(deck, CardSuit.Diamond, 13, "Hualiu", defensive: true);
    }

    private static void Add(List<DeckCardDefinition> deck, CardType cardType, CardSuit suit, params int[] ranks)
    {
        foreach (int rank in ranks)
        {
            deck.Add(new DeckCardDefinition(cardType, suit, rank));
        }
    }

    private static void Weapon(List<DeckCardDefinition> deck, CardSuit suit, int rank, string name, EquipmentEffectType effect, int range)
    {
        deck.Add(new DeckCardDefinition(
            CardType.Weapon,
            suit,
            rank,
            name,
            $"Weapon. Attack range {range}.",
            EquipmentSlotType.Weapon,
            effect,
            AttackRangeModifier: range));
    }

    private static void Armor(List<DeckCardDefinition> deck, CardSuit suit, int rank, string name, EquipmentEffectType effect)
    {
        deck.Add(new DeckCardDefinition(
            CardType.Armor,
            suit,
            rank,
            name,
            "Armor.",
            EquipmentSlotType.Armor,
            effect));
    }

    private static void Horse(List<DeckCardDefinition> deck, CardSuit suit, int rank, string name, bool defensive)
    {
        deck.Add(new DeckCardDefinition(
            defensive ? CardType.DefensiveHorse : CardType.OffensiveHorse,
            suit,
            rank,
            name,
            defensive ? "Defensive horse. Increase distance from attackers by 1." : "Offensive horse. Reduce attack distance by 1.",
            defensive ? EquipmentSlotType.DefensiveHorse : EquipmentSlotType.OffensiveHorse,
            EquipmentEffectType.None,
            AttackDistanceModifier: defensive ? 0 : 1,
            DefenseDistanceModifier: defensive ? 1 : 0));
    }
}
