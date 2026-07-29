namespace CiyuanSha.Gameplay.Cards;

/// <summary>
/// Shared card classification helpers.
/// </summary>
public static class CardRules
{
    public static bool IsRedSuit(CardSuit suit)
    {
        return suit is CardSuit.Heart or CardSuit.Diamond;
    }

    public static bool IsBlackSuit(CardSuit suit)
    {
        return suit is CardSuit.Spade or CardSuit.Club;
    }

    public static bool IsSlash(CardType cardType)
    {
        return cardType is CardType.Slash or CardType.FireSlash or CardType.ThunderSlash;
    }

    public static bool IsBasic(CardType cardType)
    {
        return IsSlash(cardType) || cardType is CardType.Dodge or CardType.Peach or CardType.Wine;
    }

    public static bool IsEquipment(CardType cardType)
    {
        return cardType is CardType.Weapon or CardType.Armor or CardType.OffensiveHorse or CardType.DefensiveHorse or CardType.Treasure;
    }

    public static bool IsTrick(CardType cardType)
    {
        return cardType is CardType.Dismantle
            or CardType.Snatch
            or CardType.Duel
            or CardType.ExNihilo
            or CardType.Nullification
            or CardType.Barbarians
            or CardType.ArrowBarrage
            or CardType.PeachGarden
            or CardType.Harvest
            or CardType.Indulgence
            or CardType.SupplyShortage
            or CardType.Lightning
            or CardType.IronChain
            or CardType.FireAttack
            or CardType.BorrowSword;
    }

    public static bool IsResponseOnly(CardType cardType)
    {
        return cardType == CardType.Nullification;
    }

    public static bool IsSelfTargeted(CardType cardType)
    {
        return cardType is CardType.Wine
            or CardType.ExNihilo
            or CardType.Lightning
            || IsEquipment(cardType);
    }

    public static bool NeedsTarget(CardType cardType)
    {
        if (IsResponseOnly(cardType))
        {
            return false;
        }

        return IsSlash(cardType)
            || cardType is CardType.Dismantle
                or CardType.Snatch
                or CardType.Duel
                or CardType.Indulgence
                or CardType.SupplyShortage
                or CardType.IronChain
                or CardType.FireAttack
                or CardType.BorrowSword;
    }

    public static bool RequiresDistanceOne(CardType cardType)
    {
        return cardType is CardType.Snatch or CardType.SupplyShortage;
    }

    public static bool TargetsAllOthers(CardType cardType)
    {
        return cardType is CardType.Barbarians or CardType.ArrowBarrage;
    }

    public static bool TargetsAllAlive(CardType cardType)
    {
        return cardType is CardType.PeachGarden or CardType.Harvest;
    }

    public static bool CanRecast(CardType cardType)
    {
        return cardType == CardType.IronChain;
    }
}
