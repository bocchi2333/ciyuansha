using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Generals;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.Gameplay.Characters;

/// <summary>
/// Player character data and runtime state.
/// </summary>
public partial class PlayerCharacter : Node
{
    public const string DefaultGeneralCardDirectory = "E:\\" + "\u6B21\u5143\u6740" + "\\" + "\u6B66\u5C06\u5361\u724C";

    public int OwnerPeerId { get; set; }

    [Export]
    public string CharacterId { get; set; } = string.Empty;

    [Export]
    public string CharacterName { get; set; } = "Unnamed General";

    [Export]
    public PlayerFaction Faction { get; private set; } = PlayerFaction.Neutral;

    [Export]
    public PlayerGender Gender { get; private set; } = PlayerGender.Unknown;

    [Export]
    public int MaxHealth { get; set; } = 4;

    [Export]
    public int CurrentHealth { get; private set; } = 4;

    [Export]
    public int BaseAttackRange { get; set; } = 1;

    [Export]
    public int HandCardCount { get; private set; }

    [Export]
    public string GeneralCardDirectory { get; set; } = DefaultGeneralCardDirectory;

    [Export]
    public string GeneralCardFileName { get; set; } = string.Empty;

    public string GeneralCardFullPath => GeneralArtRegistry.ResolveImagePath(CharacterId, GeneralCardFileName, GeneralCardDirectory);

    public bool IsDefeated { get; private set; }

    public bool IsAlive => !IsDefeated && CurrentHealth > 0;

    public bool IsDying => !IsDefeated && CurrentHealth <= 0;

    public bool IsChained { get; private set; }

    public CardInstance? EquippedWeapon { get; private set; }

    public CardInstance? EquippedArmor { get; private set; }

    public CardInstance? EquippedOffensiveHorse { get; private set; }

    public CardInstance? EquippedDefensiveHorse { get; private set; }

    public CardInstance? EquippedTreasure { get; private set; }

    public int EffectiveAttackRange => Math.Max(1, BaseAttackRange + (EquippedWeapon?.AttackRangeModifier ?? 0));

    public int AttackDistanceModifier => EquippedOffensiveHorse?.AttackDistanceModifier ?? 0;

    public int DefenseDistanceModifier => EquippedDefensiveHorse?.DefenseDistanceModifier ?? 0;

    public int DamageReductionValue => EquippedArmor?.DamageReductionValue ?? 0;

    public bool CanUseUnlimitedSlash => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.Crossbow;

    public bool IgnoresTargetArmor => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.QinggangSword;

    public bool CanUseFangtianHalberd => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.FangtianHalberd;

    public bool CanUseStoneAxe => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.StoneAxe;

    public bool CanUseKylinBow => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.KylinBow;

    public bool CanUseGreenDragonBlade => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.GreenDragonBlade;

    public bool CanUseDoubleSwords => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.DoubleSwords;

    public bool CanUseSerpentSpear => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.SerpentSpear;

    public bool CanUseIceSword => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.IceSword;

    public bool CanUseGudingBlade => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.GudingBlade;

    public bool CanUseVermilionFan => EquippedWeapon?.EquipmentEffect == EquipmentEffectType.VermilionFan;

    public bool HasVineArmor => EquippedArmor?.EquipmentEffect == EquipmentEffectType.VineArmor;

    public bool HasRenwangShield => EquippedArmor?.EquipmentEffect == EquipmentEffectType.RenwangShield;

    public bool HasSilverLion => EquippedArmor?.EquipmentEffect == EquipmentEffectType.SilverLion;

    public int PendingSlashDamageBonus { get; private set; }

    public IReadOnlyList<CardInstance> HandCards => _handCards;

    public IReadOnlyList<CardInstance> DelayedTricks => _delayedTricks;

    public IReadOnlyList<CharacterSkill> Skills => _skills;

    public event Action<PlayerCharacter>? OnStatsChanged;

    public event Action<PlayerCharacter>? OnDefeated;

    public event Action<PlayerCharacter>? OnHandCardsChanged;

    public event Action<PlayerCharacter, CardInstance>? OnHandCardRemoved;

    public event Action<PlayerCharacter>? OnEquipmentChanged;

    private readonly List<CardInstance> _handCards = new();
    private readonly List<CardInstance> _delayedTricks = new();
    private readonly List<CharacterSkill> _skills = new();
    private Texture2D? _cachedGeneralCardTexture;
    private string _cachedGeneralCardPath = string.Empty;
    private int _generatedCardCounter;

    public override void _Ready()
    {
        AddToGroup("player_character");
        MaxHealth = Math.Max(1, MaxHealth);
        CurrentHealth = Math.Clamp(CurrentHealth, 1, MaxHealth);
    }

    public void InitializeStats(int maxHealth, int currentHealth, int handCardCount = 0)
    {
        MaxHealth = Math.Max(1, maxHealth);
        CurrentHealth = Math.Clamp(currentHealth, 0, MaxHealth);
        IsDefeated = false;
        IsChained = false;
        EquippedWeapon = null;
        EquippedArmor = null;
        EquippedOffensiveHorse = null;
        EquippedDefensiveHorse = null;
        EquippedTreasure = null;
        PendingSlashDamageBonus = 0;
        _delayedTricks.Clear();
        ClearHandCards();

        if (handCardCount > 0)
        {
            AddDemoCards(handCardCount);
        }
        else
        {
            NotifyStatsChanged();
        }
    }

    public void SetFaction(PlayerFaction faction)
    {
        Faction = faction;
        NotifyStatsChanged();
    }

    public void SetGender(PlayerGender gender)
    {
        Gender = gender;
        NotifyStatsChanged();
    }

    public void ApplyGeneralDefinition(GeneralDefinition definition)
    {
        if (definition is null)
        {
            return;
        }

        CharacterId = definition.GeneralId;
        CharacterName = string.IsNullOrWhiteSpace(definition.DisplayName) ? CharacterName : definition.DisplayName;
        Gender = definition.Gender;
        MaxHealth = Math.Max(1, definition.MaxHealth);
        BaseAttackRange = Math.Max(1, BaseAttackRange);
        CurrentHealth = Math.Clamp(CurrentHealth, 0, MaxHealth);
        GeneralCardFileName = definition.CardFileName ?? string.Empty;

        ClearSkills();

        foreach (string skillId in definition.SkillIds)
        {
            CharacterSkill? skill = SkillFactory.Create(skillId);
            if (skill is not null)
            {
                AddSkill(skill);
            }
        }

        NotifyStatsChanged();
    }

    public void TakeDamage(int damageValue)
    {
        if (damageValue <= 0 || !IsAlive)
        {
            return;
        }

        CurrentHealth = Math.Max(0, CurrentHealth - damageValue);
        NotifyStatsChanged();
    }

    public void Heal(int healValue)
    {
        if (healValue <= 0 || IsDefeated)
        {
            return;
        }

        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + healValue);
        NotifyStatsChanged();
    }

    public void MarkDefeated()
    {
        if (IsDefeated)
        {
            return;
        }

        IsDefeated = true;
        IsChained = false;
        NotifyStatsChanged();
        OnDefeated?.Invoke(this);
    }

    public void SetChained(bool isChained)
    {
        if (IsDefeated || IsChained == isChained)
        {
            return;
        }

        IsChained = isChained;
        NotifyStatsChanged();
    }

    public void ToggleChained()
    {
        SetChained(!IsChained);
    }

    public IReadOnlyList<CardInstance> RemoveAllCardsAndEquipment()
    {
        List<CardInstance> removedCards = new();
        removedCards.AddRange(_handCards.Select(card => card.Clone()));
        removedCards.AddRange(_delayedTricks.Select(card => card.Clone()));

        if (EquippedWeapon is not null)
        {
            removedCards.Add(EquippedWeapon.Clone());
            EquippedWeapon = null;
        }

        if (EquippedArmor is not null)
        {
            removedCards.Add(EquippedArmor.Clone());
            EquippedArmor = null;
        }

        if (EquippedOffensiveHorse is not null)
        {
            removedCards.Add(EquippedOffensiveHorse.Clone());
            EquippedOffensiveHorse = null;
        }

        if (EquippedDefensiveHorse is not null)
        {
            removedCards.Add(EquippedDefensiveHorse.Clone());
            EquippedDefensiveHorse = null;
        }

        if (EquippedTreasure is not null)
        {
            removedCards.Add(EquippedTreasure.Clone());
            EquippedTreasure = null;
        }

        _handCards.Clear();
        _delayedTricks.Clear();
        HandCardCount = 0;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
        OnEquipmentChanged?.Invoke(this);
        return removedCards;
    }

    public bool TakeHandCardByInstanceId(string instanceId, out CardInstance? movedCard)
    {
        movedCard = null;

        CardInstance? card = FindHandCard(instanceId);
        if (card is null)
        {
            return false;
        }

        _handCards.Remove(card);
        HandCardCount = _handCards.Count;
        movedCard = card;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
        return true;
    }

    public bool TryEquipCard(CardInstance? equipmentCard, out CardInstance? replacedCard)
    {
        replacedCard = null;
        if (equipmentCard is null)
        {
            return false;
        }

        switch (equipmentCard.EquipmentSlot)
        {
            case EquipmentSlotType.Weapon:
                replacedCard = EquippedWeapon;
                EquippedWeapon = equipmentCard;
                break;

            case EquipmentSlotType.Armor:
                replacedCard = EquippedArmor;
                EquippedArmor = equipmentCard;
                break;

            case EquipmentSlotType.OffensiveHorse:
                replacedCard = EquippedOffensiveHorse;
                EquippedOffensiveHorse = equipmentCard;
                break;

            case EquipmentSlotType.DefensiveHorse:
                replacedCard = EquippedDefensiveHorse;
                EquippedDefensiveHorse = equipmentCard;
                break;

            case EquipmentSlotType.Treasure:
                replacedCard = EquippedTreasure;
                EquippedTreasure = equipmentCard;
                break;

            default:
                return false;
        }

        NotifyStatsChanged();
        OnEquipmentChanged?.Invoke(this);
        return true;
    }

    public bool HasDelayedTrick(CardType cardType)
    {
        return _delayedTricks.Any(card => card.CardType == cardType);
    }

    public bool AddDelayedTrick(CardInstance? delayedTrick)
    {
        if (delayedTrick is null || !delayedTrick.IsDelayedTrick || HasDelayedTrick(delayedTrick.CardType))
        {
            return false;
        }

        _delayedTricks.Add(delayedTrick);
        NotifyStatsChanged();
        return true;
    }

    public bool TryPopFirstDelayedTrick(out CardInstance? delayedTrick)
    {
        delayedTrick = null;
        if (_delayedTricks.Count == 0)
        {
            return false;
        }

        delayedTrick = _delayedTricks[0];
        _delayedTricks.RemoveAt(0);
        NotifyStatsChanged();
        return true;
    }

    public int GetDamageReduction(DamageType damageType)
    {
        if (IsDefeated || EquippedArmor is null)
        {
            return 0;
        }

        if (HasVineArmor)
        {
            return 0;
        }

        if (damageType != DamageType.Physical)
        {
            return 0;
        }

        return Math.Max(0, EquippedArmor.DamageReductionValue);
    }

    public void AddPendingSlashDamageBonus(int bonus)
    {
        PendingSlashDamageBonus = Math.Max(0, PendingSlashDamageBonus + bonus);
        NotifyStatsChanged();
    }

    public int ConsumePendingSlashDamageBonus()
    {
        int bonus = PendingSlashDamageBonus;
        PendingSlashDamageBonus = 0;
        if (bonus > 0)
        {
            NotifyStatsChanged();
        }

        return bonus;
    }

    public void ClearPendingSlashDamageBonus()
    {
        if (PendingSlashDamageBonus <= 0)
        {
            return;
        }

        PendingSlashDamageBonus = 0;
        NotifyStatsChanged();
    }

    public bool TryRemoveOneCardOrEquipment(out CardInstance? removedCard)
    {
        removedCard = null;

        if (_handCards.Count > 0)
        {
            removedCard = _handCards[0];
            _handCards.RemoveAt(0);
            HandCardCount = _handCards.Count;
            NotifyStatsChanged();
            NotifyHandCardsChanged();
            return true;
        }

        if (EquippedWeapon is not null)
        {
            removedCard = EquippedWeapon;
            EquippedWeapon = null;
        }
        else if (EquippedArmor is not null)
        {
            removedCard = EquippedArmor;
            EquippedArmor = null;
        }
        else if (EquippedOffensiveHorse is not null)
        {
            removedCard = EquippedOffensiveHorse;
            EquippedOffensiveHorse = null;
        }
        else if (EquippedDefensiveHorse is not null)
        {
            removedCard = EquippedDefensiveHorse;
            EquippedDefensiveHorse = null;
        }
        else if (EquippedTreasure is not null)
        {
            removedCard = EquippedTreasure;
            EquippedTreasure = null;
        }

        if (removedCard is null)
        {
            return false;
        }

        NotifyStatsChanged();
        OnEquipmentChanged?.Invoke(this);
        return true;
    }

    public bool TryRemoveCardOrEquipmentByInstanceId(string instanceId, out CardInstance? removedCard)
    {
        removedCard = null;
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        if (TakeHandCardByInstanceId(instanceId, out removedCard))
        {
            return true;
        }

        if (EquippedWeapon?.InstanceId == instanceId)
        {
            removedCard = EquippedWeapon;
            EquippedWeapon = null;
        }
        else if (EquippedArmor?.InstanceId == instanceId)
        {
            removedCard = EquippedArmor;
            EquippedArmor = null;
        }
        else if (EquippedOffensiveHorse?.InstanceId == instanceId)
        {
            removedCard = EquippedOffensiveHorse;
            EquippedOffensiveHorse = null;
        }
        else if (EquippedDefensiveHorse?.InstanceId == instanceId)
        {
            removedCard = EquippedDefensiveHorse;
            EquippedDefensiveHorse = null;
        }
        else if (EquippedTreasure?.InstanceId == instanceId)
        {
            removedCard = EquippedTreasure;
            EquippedTreasure = null;
        }
        else
        {
            CardInstance? delayedTrick = _delayedTricks.FirstOrDefault(card => card.InstanceId == instanceId);
            if (delayedTrick is not null)
            {
                removedCard = delayedTrick;
                _delayedTricks.Remove(delayedTrick);
            }
        }

        if (removedCard is null)
        {
            return false;
        }

        NotifyStatsChanged();
        OnEquipmentChanged?.Invoke(this);
        return true;
    }

    public bool TryRemoveEquipmentBySlot(EquipmentSlotType slot, out CardInstance? removedCard)
    {
        removedCard = null;
        switch (slot)
        {
            case EquipmentSlotType.Weapon:
                removedCard = EquippedWeapon;
                EquippedWeapon = null;
                break;

            case EquipmentSlotType.Armor:
                removedCard = EquippedArmor;
                EquippedArmor = null;
                break;

            case EquipmentSlotType.OffensiveHorse:
                removedCard = EquippedOffensiveHorse;
                EquippedOffensiveHorse = null;
                break;

            case EquipmentSlotType.DefensiveHorse:
                removedCard = EquippedDefensiveHorse;
                EquippedDefensiveHorse = null;
                break;

            case EquipmentSlotType.Treasure:
                removedCard = EquippedTreasure;
                EquippedTreasure = null;
                break;
        }

        if (removedCard is null)
        {
            return false;
        }

        NotifyStatsChanged();
        OnEquipmentChanged?.Invoke(this);
        return true;
    }

    public bool TryRemoveOneHorse(out CardInstance? removedCard)
    {
        removedCard = null;
        if (EquippedDefensiveHorse is not null)
        {
            removedCard = EquippedDefensiveHorse;
            EquippedDefensiveHorse = null;
        }
        else if (EquippedOffensiveHorse is not null)
        {
            removedCard = EquippedOffensiveHorse;
            EquippedOffensiveHorse = null;
        }

        if (removedCard is null)
        {
            return false;
        }

        NotifyStatsChanged();
        OnEquipmentChanged?.Invoke(this);
        return true;
    }

    public void DrawCards(int count)
    {
        AddDemoCards(count);
    }

    public void AddDemoCards(int count)
    {
        if (count <= 0)
        {
            return;
        }

        List<CardInstance> cards = new();
        for (int i = 0; i < count; i++)
        {
            int cardIndex = (_generatedCardCounter + i) % 4;
            CardType cardType = cardIndex switch
            {
                2 => CardType.Dodge,
                3 => CardType.Peach,
                _ => CardType.Slash
            };

            cards.Add(CreateDemoCard(cardType));
        }

        _generatedCardCounter += count;
        AddCards(cards);
    }

    public bool TryConsumeHandCard(int count = 1)
    {
        if (count <= 0 || HandCardCount < count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (_handCards.Count == 0)
            {
                break;
            }

            CardInstance card = _handCards[0];
            _handCards.RemoveAt(0);
            OnHandCardRemoved?.Invoke(this, card);
        }

        HandCardCount = _handCards.Count;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
        return true;
    }

    public CardInstance? FindHandCard(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return _handCards.FirstOrDefault(card => card.InstanceId == instanceId);
    }

    public CardInstance? FindFirstHandCardOfType(CardType cardType)
    {
        return _handCards.FirstOrDefault(card => card.CardType == cardType);
    }

    public bool TryConsumeHandCardByInstanceId(string instanceId, out CardInstance? removedCard)
    {
        removedCard = null;

        CardInstance? card = FindHandCard(instanceId);
        if (card is null)
        {
            return false;
        }

        _handCards.Remove(card);
        HandCardCount = _handCards.Count;
        removedCard = card;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
        OnHandCardRemoved?.Invoke(this, card);
        return true;
    }

    public bool TryConsumeFirstHandCardOfType(CardType cardType, out CardInstance? removedCard)
    {
        removedCard = null;

        CardInstance? card = FindFirstHandCardOfType(cardType);
        if (card is null)
        {
            return false;
        }

        return TryConsumeHandCardByInstanceId(card.InstanceId, out removedCard);
    }

    public void AddCard(CardInstance? card)
    {
        if (card is null)
        {
            return;
        }

        _handCards.Add(card);
        HandCardCount = _handCards.Count;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
    }

    public void AddCards(IEnumerable<CardInstance>? cards)
    {
        if (cards is null)
        {
            return;
        }

        bool addedAny = false;
        foreach (CardInstance card in cards)
        {
            _handCards.Add(card);
            addedAny = true;
        }

        if (!addedAny)
        {
            return;
        }

        HandCardCount = _handCards.Count;
        NotifyStatsChanged();
        NotifyHandCardsChanged();
    }

    public IReadOnlyList<CardInstance> DiscardDownToLimit(int handLimit)
    {
        List<CardInstance> discardedCards = new();
        int safeLimit = Math.Max(0, handLimit);

        while (_handCards.Count > safeLimit)
        {
            CardInstance card = _handCards[0];
            if (!TryConsumeHandCardByInstanceId(card.InstanceId, out CardInstance? removedCard) || removedCard is null)
            {
                break;
            }

            discardedCards.Add(removedCard);
        }

        return discardedCards;
    }

    public void AddSkill(CharacterSkill? skill)
    {
        if (skill is null)
        {
            return;
        }

        skill.Attach(this);
        _skills.Add(skill);
    }

    public void ClearSkills()
    {
        _skills.Clear();
    }

    private void ApplyNetworkSkillStates(IEnumerable<NetworkSkillState>? skillStates)
    {
        if (skillStates is null)
        {
            return;
        }

        List<NetworkSkillState> states = skillStates
            .Where(skillState => skillState is not null && !string.IsNullOrWhiteSpace(skillState.SkillId))
            .ToList();

        _skills.Clear();
        foreach (NetworkSkillState skillState in states)
        {
            CharacterSkill? skill = SkillFactory.Create(skillState.SkillId);
            if (skill is null)
            {
                continue;
            }

            AddSkill(skill);
            skill.SyncUsesThisTurn(skillState.UsesThisTurn);
        }
    }

    public virtual bool TryRespondToDamageTargeting(DamageTargetingEventArgs args)
    {
        foreach (CharacterSkill skill in _skills
            .Where(skill => (skill.TriggerTimings & SkillTriggerTiming.DamageTargeting) != 0)
            .OrderByDescending(skill => skill.TriggerPriority))
        {
            if (skill.TryRespondToDamageTargeting(args))
            {
                return true;
            }
        }

        return false;
    }

    public Texture2D? LoadGeneralCardTexture()
    {
        string fullPath = GeneralCardFullPath;
        if (string.Equals(_cachedGeneralCardPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedGeneralCardTexture;
        }

        _cachedGeneralCardPath = fullPath;
        _cachedGeneralCardTexture = null;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            GD.PushWarning($"{Name}: No mapped general art for {CharacterId}.");
            return null;
        }

        if (!System.IO.File.Exists(fullPath))
        {
            GD.PushWarning($"{Name}: General card image was not found at {fullPath}.");
            return null;
        }

        Image image = Image.LoadFromFile(fullPath);
        if (image.IsEmpty())
        {
            GD.PushWarning($"{Name}: Failed to load general card image at {fullPath}.");
            return null;
        }

        _cachedGeneralCardTexture = ImageTexture.CreateFromImage(image);
        return _cachedGeneralCardTexture;
    }

    public NetworkCharacterState ToNetworkState(bool revealHandCards = true)
    {
        return new NetworkCharacterState
        {
            PeerId = OwnerPeerId,
            CharacterId = CharacterId,
            CharacterName = CharacterName,
            Faction = Faction.ToString(),
            Gender = Gender.ToString(),
            MaxHealth = MaxHealth,
            CurrentHealth = CurrentHealth,
            IsDefeated = IsDefeated,
            IsChained = IsChained,
            EffectiveAttackRange = EffectiveAttackRange,
            HandCardCount = HandCardCount,
            GeneralCardFileName = GeneralCardFileName,
            EquippedWeaponName = EquippedWeapon?.DisplayName ?? string.Empty,
            EquippedArmorName = EquippedArmor?.DisplayName ?? string.Empty,
            EquippedOffensiveHorseName = EquippedOffensiveHorse?.DisplayName ?? string.Empty,
            EquippedDefensiveHorseName = EquippedDefensiveHorse?.DisplayName ?? string.Empty,
            EquippedTreasureName = EquippedTreasure?.DisplayName ?? string.Empty,
            Skills = _skills.Select(skill => new NetworkSkillState
            {
                SkillId = skill.SkillId,
                DisplayName = skill.DisplayName,
                Description = skill.Description,
                UsageScope = skill.UsageScope.ToString(),
                TriggerPriority = skill.TriggerPriority.ToString(),
                TriggerTimings = skill.TriggerTimings.ToString(),
                IsActiveSkill = skill.IsActiveSkill,
                TargetingMode = skill.TargetingMode.ToString(),
                MinTargetCount = skill.MinTargetCount,
                MaxTargetCount = skill.MaxTargetCount,
                RequiresHandCardCost = skill.RequiresHandCardCost,
                UsesThisTurn = skill.UsesThisTurn
            }).ToList(),
            DelayedTricks = _delayedTricks.Select(card => new NetworkHandCardState
            {
                InstanceId = card.InstanceId,
                CardType = card.CardType.ToString(),
                DisplayName = card.DisplayName,
                Description = card.Description,
                Suit = card.Suit.ToString(),
                Rank = card.Rank,
                DamageValue = card.DamageValue,
                EquipmentSlot = card.EquipmentSlot.ToString(),
                EquipmentEffect = card.EquipmentEffect.ToString(),
                AttackRangeModifier = card.AttackRangeModifier,
                DamageReductionValue = card.DamageReductionValue,
                AttackDistanceModifier = card.AttackDistanceModifier,
                DefenseDistanceModifier = card.DefenseDistanceModifier,
                IsDelayedTrick = card.IsDelayedTrick
            }).ToList(),
            HandCards = revealHandCards
                ? _handCards.Select(card => new NetworkHandCardState
                {
                    InstanceId = card.InstanceId,
                    CardType = card.CardType.ToString(),
                    DisplayName = card.DisplayName,
                    Description = card.Description,
                    Suit = card.Suit.ToString(),
                    Rank = card.Rank,
                    DamageValue = card.DamageValue,
                    EquipmentSlot = card.EquipmentSlot.ToString(),
                    EquipmentEffect = card.EquipmentEffect.ToString(),
                    AttackRangeModifier = card.AttackRangeModifier,
                    DamageReductionValue = card.DamageReductionValue,
                    AttackDistanceModifier = card.AttackDistanceModifier,
                    DefenseDistanceModifier = card.DefenseDistanceModifier,
                    IsDelayedTrick = card.IsDelayedTrick
                }).ToList()
                : new List<NetworkHandCardState>()
        };
    }

    public void ApplyNetworkState(NetworkCharacterState state)
    {
        if (state is null)
        {
            return;
        }

        OwnerPeerId = state.PeerId;
        CharacterId = state.CharacterId ?? string.Empty;
        CharacterName = string.IsNullOrWhiteSpace(state.CharacterName) ? CharacterName : state.CharacterName;
        Faction = Enum.TryParse(state.Faction, true, out PlayerFaction parsedFaction) ? parsedFaction : PlayerFaction.Neutral;
        Gender = Enum.TryParse(state.Gender, true, out PlayerGender parsedGender) ? parsedGender : PlayerGender.Unknown;
        ApplyNetworkSkillStates(state.Skills);
        GeneralCardFileName = state.GeneralCardFileName ?? string.Empty;
        MaxHealth = Math.Max(1, state.MaxHealth);
        CurrentHealth = Math.Clamp(state.CurrentHealth, 0, MaxHealth);
        IsDefeated = state.IsDefeated;
        IsChained = state.IsChained && !IsDefeated;
        EquippedWeapon = string.IsNullOrWhiteSpace(state.EquippedWeaponName)
            ? null
            : new CardInstance
            {
                CardType = CardType.Weapon,
                EquipmentSlot = EquipmentSlotType.Weapon,
                DisplayName = state.EquippedWeaponName,
                EquipmentEffect = InferEquipmentEffect(state.EquippedWeaponName),
                AttackRangeModifier = Math.Max(0, state.EffectiveAttackRange - BaseAttackRange)
            };
        EquippedArmor = string.IsNullOrWhiteSpace(state.EquippedArmorName)
            ? null
            : new CardInstance
            {
                CardType = CardType.Armor,
                EquipmentSlot = EquipmentSlotType.Armor,
                DisplayName = state.EquippedArmorName,
                EquipmentEffect = InferEquipmentEffect(state.EquippedArmorName),
                DamageReductionValue = 1
            };
        EquippedOffensiveHorse = string.IsNullOrWhiteSpace(state.EquippedOffensiveHorseName)
            ? null
            : new CardInstance
            {
                CardType = CardType.OffensiveHorse,
                EquipmentSlot = EquipmentSlotType.OffensiveHorse,
                DisplayName = state.EquippedOffensiveHorseName,
                AttackDistanceModifier = 1
            };
        EquippedDefensiveHorse = string.IsNullOrWhiteSpace(state.EquippedDefensiveHorseName)
            ? null
            : new CardInstance
            {
                CardType = CardType.DefensiveHorse,
                EquipmentSlot = EquipmentSlotType.DefensiveHorse,
                DisplayName = state.EquippedDefensiveHorseName,
                DefenseDistanceModifier = 1
            };
        EquippedTreasure = string.IsNullOrWhiteSpace(state.EquippedTreasureName)
            ? null
            : new CardInstance
            {
                CardType = CardType.Treasure,
                EquipmentSlot = EquipmentSlotType.Treasure,
                DisplayName = state.EquippedTreasureName,
                EquipmentEffect = InferEquipmentEffect(state.EquippedTreasureName)
            };
        _delayedTricks.Clear();

        foreach (NetworkHandCardState delayedTrickState in state.DelayedTricks ?? Enumerable.Empty<NetworkHandCardState>())
        {
            _delayedTricks.Add(new CardInstance
            {
                InstanceId = delayedTrickState.InstanceId ?? string.Empty,
                CardType = Enum.TryParse(delayedTrickState.CardType, true, out CardType parsedDelayedType) ? parsedDelayedType : CardType.None,
                DisplayName = delayedTrickState.DisplayName ?? string.Empty,
                Description = delayedTrickState.Description ?? string.Empty,
                Suit = Enum.TryParse(delayedTrickState.Suit, true, out CardSuit parsedDelayedSuit) ? parsedDelayedSuit : CardSuit.None,
                Rank = Math.Clamp(delayedTrickState.Rank, 0, 13),
                DamageValue = Math.Max(0, delayedTrickState.DamageValue),
                EquipmentSlot = Enum.TryParse(delayedTrickState.EquipmentSlot, true, out EquipmentSlotType parsedDelayedSlot) ? parsedDelayedSlot : EquipmentSlotType.None,
                EquipmentEffect = Enum.TryParse(delayedTrickState.EquipmentEffect, true, out EquipmentEffectType parsedDelayedEffect) ? parsedDelayedEffect : EquipmentEffectType.None,
                AttackRangeModifier = delayedTrickState.AttackRangeModifier,
                DamageReductionValue = delayedTrickState.DamageReductionValue,
                AttackDistanceModifier = delayedTrickState.AttackDistanceModifier,
                DefenseDistanceModifier = delayedTrickState.DefenseDistanceModifier,
                IsDelayedTrick = delayedTrickState.IsDelayedTrick
            });
        }

        _handCards.Clear();

        foreach (NetworkHandCardState handCardState in state.HandCards)
        {
            _handCards.Add(new CardInstance
            {
                InstanceId = handCardState.InstanceId ?? string.Empty,
                CardType = Enum.TryParse(handCardState.CardType, true, out CardType parsedCardType) ? parsedCardType : CardType.None,
                DisplayName = handCardState.DisplayName ?? string.Empty,
                Description = handCardState.Description ?? string.Empty,
                Suit = Enum.TryParse(handCardState.Suit, true, out CardSuit parsedSuit) ? parsedSuit : CardSuit.None,
                Rank = Math.Clamp(handCardState.Rank, 0, 13),
                DamageValue = Math.Max(0, handCardState.DamageValue),
                EquipmentSlot = Enum.TryParse(handCardState.EquipmentSlot, true, out EquipmentSlotType parsedSlot) ? parsedSlot : EquipmentSlotType.None,
                EquipmentEffect = Enum.TryParse(handCardState.EquipmentEffect, true, out EquipmentEffectType parsedEffect) ? parsedEffect : EquipmentEffectType.None,
                AttackRangeModifier = handCardState.AttackRangeModifier,
                DamageReductionValue = handCardState.DamageReductionValue,
                AttackDistanceModifier = handCardState.AttackDistanceModifier,
                DefenseDistanceModifier = handCardState.DefenseDistanceModifier,
                IsDelayedTrick = handCardState.IsDelayedTrick
            });
        }

        HandCardCount = state.HandCards.Count > 0 ? _handCards.Count : Math.Max(_handCards.Count, state.HandCardCount);
        if (state.HandCards.Count == 0)
        {
            HandCardCount = state.HandCardCount;
        }

        NotifyStatsChanged();
        NotifyHandCardsChanged();
        OnEquipmentChanged?.Invoke(this);
    }

    private void NotifyStatsChanged()
    {
        OnStatsChanged?.Invoke(this);
    }

    private static EquipmentEffectType InferEquipmentEffect(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return EquipmentEffectType.None;
        }

        if (displayName.Contains("Crossbow", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.Crossbow;
        }

        if (displayName.Contains("Qinggang", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.QinggangSword;
        }

        if (displayName.Contains("Fangtian", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Halberd", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.FangtianHalberd;
        }

        if (displayName.Contains("Stone Axe", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Guanshi", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.StoneAxe;
        }

        if (displayName.Contains("Kylin", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Qilin", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.KylinBow;
        }

        if (displayName.Contains("Green Dragon", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Qinglong", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.GreenDragonBlade;
        }

        if (displayName.Contains("Double Swords", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Yinyang", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.DoubleSwords;
        }

        if (displayName.Contains("Serpent Spear", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Zhangba", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.SerpentSpear;
        }

        if (displayName.Contains("Ice Sword", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Hanbing", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.IceSword;
        }

        if (displayName.Contains("Guding", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.GudingBlade;
        }

        if (displayName.Contains("Vermilion", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Zhuque", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.VermilionFan;
        }

        if (displayName.Contains("Eight Diagram", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.EightDiagram;
        }

        if (displayName.Contains("Vine", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.VineArmor;
        }

        if (displayName.Contains("Renwang", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.RenwangShield;
        }

        if (displayName.Contains("Silver Lion", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.SilverLion;
        }

        if (displayName.Contains("Imperial Seal", StringComparison.OrdinalIgnoreCase))
        {
            return EquipmentEffectType.ImperialSeal;
        }

        return EquipmentEffectType.None;
    }

    private void NotifyHandCardsChanged()
    {
        OnHandCardsChanged?.Invoke(this);
    }

    private void ClearHandCards()
    {
        _handCards.Clear();
        HandCardCount = 0;
        NotifyHandCardsChanged();
    }

    private CardInstance CreateDemoCard(CardType cardType)
    {
        string instanceId = $"{OwnerPeerId}-{_generatedCardCounter + _handCards.Count + 1:D4}";

        return cardType switch
        {
            CardType.Dodge => new CardInstance
            {
                InstanceId = instanceId,
                CardType = CardType.Dodge,
                DisplayName = "Dodge",
                Description = "Respond to one attack.",
                DamageValue = 0
            },
            CardType.Peach => new CardInstance
            {
                InstanceId = instanceId,
                CardType = CardType.Peach,
                DisplayName = "Peach",
                Description = "Heal yourself for 1.",
                DamageValue = 1
            },
            _ => new CardInstance
            {
                InstanceId = instanceId,
                CardType = CardType.Slash,
                DisplayName = "Slash",
                Description = "Deal 1 damage to one target.",
                DamageValue = 1
            }
        };
    }
}
