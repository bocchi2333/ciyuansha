using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Local hand zone UI.
/// Lets the player select a Slash card and then click a target.
/// </summary>
public partial class HandZonePanel : Control
{
    [Export]
    public NodePath CardsContainerPath { get; set; } = new NodePath();

    [Export]
    public NodePath StatusLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath EndPhaseButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath InputBridgePath { get; set; } = new NodePath();

    private HBoxContainer? _cardsContainer;
    private Label? _statusLabel;
    private Button? _endPhaseButton;
    private PlayPhaseInputBridge? _inputBridge;
    private Panel? _selectedPreviewHost;
    private InkCardButton? _selectedPreviewCard;
    private RichTextLabel? _selectedPreviewText;
    private readonly Dictionary<string, Button> _cardButtons = new();
    private PlayerCharacter? _localCharacter;
    private string _selectedCardInstanceId = string.Empty;
    private string _selectedSkillId = string.Empty;

    public CardInstance? SelectedCard => _localCharacter?.FindHandCard(_selectedCardInstanceId);

    public CharacterSkill? SelectedSkill => _localCharacter?.Skills.FirstOrDefault(skill => skill.SkillId == _selectedSkillId);

    public event Action? OnSelectedCardChanged;

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            LayoutSelectedCardPreview();
        }
    }

    public override void _Ready()
    {
        _cardsContainer = GetNodeOrNull<HBoxContainer>(CardsContainerPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _endPhaseButton = GetNodeOrNull<Button>(EndPhaseButtonPath);
        _inputBridge = GetNodeOrNull<PlayPhaseInputBridge>(InputBridgePath);
        EnsureSelectedCardPreview();
        ApplyChrome();

        if (_endPhaseButton is not null)
        {
            _endPhaseButton.Pressed += HandleEndPhasePressed;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnTurnOwnerChanged += HandleTurnOwnerChanged;
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            GameManager.Instance.OnStateChanged += HandleGameStateChanged;
        }

        ResolveLocalCharacter();
        RefreshUI();
    }

    public override void _ExitTree()
    {
        UnbindLocalCharacter();

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnTurnOwnerChanged -= HandleTurnOwnerChanged;
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            GameManager.Instance.OnStateChanged -= HandleGameStateChanged;
        }
    }

    public bool TryPlaySelectedSlashAt(PlayerCharacter target)
    {
        return TryPlaySelectedCardAtTargets(target is null ? Array.Empty<PlayerCharacter>() : new[] { target });
    }

    public bool TryRecastSelectedCard()
    {
        if (_localCharacter is null || _inputBridge is null)
        {
            return false;
        }

        string cardInstanceId = _selectedCardInstanceId;
        if (string.IsNullOrWhiteSpace(cardInstanceId))
        {
            SetStatus("请先选择一张牌。");
            return false;
        }

        bool recast = _inputBridge.TryRecastCard(_localCharacter, cardInstanceId);
        if (recast)
        {
            SetSelectedCard(string.Empty);
            RefreshUI();
        }

        return recast;
    }

    public bool TryPlaySelectedCardAtTargets(IEnumerable<PlayerCharacter> targets)
    {
        if (SelectedSkill is not null)
        {
            return TryActivateSelectedSkillAtTargets(targets);
        }

        if (_localCharacter is null || _inputBridge is null || targets is null)
        {
            return false;
        }

        string cardInstanceId = _selectedCardInstanceId;
        if (string.IsNullOrWhiteSpace(cardInstanceId))
        {
            SetStatus("请先选择一张牌。");
            return false;
        }

        CardInstance? card = _localCharacter.FindHandCard(cardInstanceId);
        if (card is null)
        {
            SetStatus("选中的牌已失效。");
            return false;
        }

        List<PlayerCharacter> targetList = targets
            .Where(target => target is not null)
            .Distinct()
            .ToList();
        PlayerCharacter? actualTarget = CardRules.IsSelfTargeted(card.CardType)
            || card.CardType == CardType.Peach
            || !CardRules.NeedsTarget(card.CardType)
            ? _localCharacter
            : targetList.FirstOrDefault();
        IEnumerable<PlayerCharacter> actualTargets = actualTarget == _localCharacter
            ? new[] { _localCharacter }
            : targetList;
        bool played = _inputBridge.TryUseCard(_localCharacter, actualTargets, cardInstanceId);
        if (played)
        {
            SetSelectedCard(string.Empty);
            RefreshUI();
        }

        return played;
    }

    public bool TryActivateSelectedSkillAtTargets(IEnumerable<PlayerCharacter>? targets)
    {
        if (_localCharacter is null || _inputBridge is null)
        {
            return false;
        }

        CharacterSkill? skill = SelectedSkill;
        if (skill is null)
        {
            SetStatus("请先选择一个主动技能。");
            return false;
        }

        string costCardInstanceId = skill.RequiresHandCardCost ? _selectedCardInstanceId : string.Empty;
        if (skill.RequiresHandCardCost && string.IsNullOrWhiteSpace(costCardInstanceId))
        {
            SetStatus($"{skill.DisplayName} 需要先选择一张手牌作为消耗。");
            return false;
        }

        List<PlayerCharacter> targetList = (targets ?? Array.Empty<PlayerCharacter>())
            .Where(target => target is not null)
            .Distinct()
            .ToList();
        bool activated = _inputBridge.TryActivateSkill(_localCharacter, skill.SkillId, targetList, costCardInstanceId);
        if (activated)
        {
            SetSelectedSkill(string.Empty);
            SetSelectedCard(string.Empty);
            RefreshUI();
            return true;
        }

        _inputBridge.CanActivateSkill(_localCharacter, skill.SkillId, targetList, costCardInstanceId, out string reason);
        SetStatus(string.IsNullOrWhiteSpace(reason)
            ? $"{skill.DisplayName} 无法发动。"
            : BattleLogTextLocalizer.Localize(reason));
        return false;
    }

    public void SelectCard(string instanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (_localCharacter is not null
            && _inputBridge is not null
            && gameManager?.HasPendingHandCardSelection == true
            && gameManager.PendingHandCardSelectionPeerId == _localCharacter.OwnerPeerId)
        {
            if (_inputBridge.TryChooseHandCard(_localCharacter, instanceId))
            {
                SetSelectedCard(string.Empty);
                RefreshUI();
            }

            return;
        }

        CharacterSkill? selectedSkill = SelectedSkill;
        if (selectedSkill is null || !selectedSkill.RequiresHandCardCost)
        {
            SetSelectedSkill(string.Empty);
        }

        SetSelectedCard(_selectedCardInstanceId == instanceId ? string.Empty : instanceId);
        RefreshUI();
    }

    public void SelectSkill(string skillId)
    {
        CharacterSkill? skill = _localCharacter?.Skills.FirstOrDefault(skill => skill.SkillId == skillId);
        if (skill is null || !skill.IsActiveSkill)
        {
            return;
        }

        SetSelectedSkill(_selectedSkillId == skillId ? string.Empty : skillId);
        if (!skill.RequiresHandCardCost)
        {
            SetSelectedCard(string.Empty);
        }

        RefreshUI();
    }

    private void SetSelectedCard(string instanceId)
    {
        instanceId ??= string.Empty;
        if (_selectedCardInstanceId == instanceId)
        {
            return;
        }

        _selectedCardInstanceId = instanceId;
        OnSelectedCardChanged?.Invoke();
    }

    private void SetSelectedSkill(string skillId)
    {
        skillId ??= string.Empty;
        if (_selectedSkillId == skillId)
        {
            return;
        }

        _selectedSkillId = skillId;
        OnSelectedCardChanged?.Invoke();
    }

    private void ResolveLocalCharacter()
    {
        UnbindLocalCharacter();

        int localPeerId = LanMultiplayerManager.Instance?.IsConnected == true
            ? LanMultiplayerManager.Instance.LocalPeerId
            : GameManager.Instance?.CurrentTurnPeerId ?? 1;

        _localCharacter = GetTree().GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .FirstOrDefault(character => character.OwnerPeerId == localPeerId);

        if (_localCharacter is not null)
        {
            _localCharacter.OnHandCardsChanged += HandleLocalHandChanged;
            _localCharacter.OnStatsChanged += HandleLocalStatsChanged;
        }
    }

    private void RefreshUI()
    {
        if (_localCharacter is null)
        {
            ResolveLocalCharacter();
        }

        RebuildCards();
        UpdateSelectedCardPreview();

        if (_endPhaseButton is not null)
        {
            _endPhaseButton.Disabled = !(_inputBridge?.IsInputEnabled ?? false);
            _endPhaseButton.Text = GameManager.Instance?.CurrentPhase == TurnPhase.DiscardPhase
                ? "弃置选中牌"
                : "结束出牌";
        }

        if (_localCharacter is null)
        {
            SetStatus("等待你的武将载入...");
            return;
        }

        if (!(_inputBridge?.IsInputEnabled ?? false))
        {
            if (GameManager.Instance?.HasPendingResponseWindow == true)
            {
                SetStatus(GameManager.Instance.PendingResponsePeerId == _localCharacter.OwnerPeerId
                    ? "需要操作：请处理中央响应窗口。"
                    : $"等待玩家 {GameManager.Instance.PendingResponsePeerId} 响应。");
                return;
            }

            if (GameManager.Instance?.HasPendingHarvestSelection == true)
            {
                SetStatus(GameManager.Instance.PendingHarvestPeerId == _localCharacter.OwnerPeerId
                    ? $"需要操作：{BattleLogTextLocalizer.Localize(GameManager.Instance.PendingHarvestPrompt)}"
                    : $"等待玩家 {GameManager.Instance.PendingHarvestPeerId} 选择五谷丰登。");
                return;
            }

            if (GameManager.Instance?.HasPendingTargetCardSelection == true)
            {
                SetStatus(GameManager.Instance.PendingTargetCardSelectionPeerId == _localCharacter.OwnerPeerId
                    ? $"需要操作：{BattleLogTextLocalizer.Localize(GameManager.Instance.PendingTargetCardSelectionPrompt)}"
                    : $"等待玩家 {GameManager.Instance.PendingTargetCardSelectionPeerId} 选择目标牌。");
                return;
            }

            if (GameManager.Instance?.HasPendingHandCardSelection == true)
            {
                SetStatus(GameManager.Instance.PendingHandCardSelectionPeerId == _localCharacter.OwnerPeerId
                    ? $"需要操作：{BattleLogTextLocalizer.Localize(GameManager.Instance.PendingHandCardSelectionPrompt)}"
                    : $"等待玩家 {GameManager.Instance.PendingHandCardSelectionPeerId} 选择手牌。");
                return;
            }

            if (GameManager.Instance?.IsAwaitingDiscardInput == true)
            {
                SetStatus($"等待玩家 {GameManager.Instance.CurrentTurnPeerId} 弃牌。");
                return;
            }

            if (GameManager.Instance?.IsAwaitingPlayerInput == true)
            {
                SetStatus($"等待玩家 {GameManager.Instance.CurrentTurnPeerId} 出牌或结束阶段。");
                return;
            }

            SetStatus("当前动作结算中，请稍候。");
            return;
        }

        if (GameManager.Instance?.HasPendingHarvestSelection == true)
        {
            SetStatus(GameManager.Instance.PendingHarvestPeerId == _localCharacter.OwnerPeerId
                ? BattleLogTextLocalizer.Localize(GameManager.Instance.PendingHarvestPrompt)
                : $"等待玩家 {GameManager.Instance.PendingHarvestPeerId} 选择五谷丰登。");
            return;
        }

        if (GameManager.Instance?.HasPendingTargetCardSelection == true)
        {
            SetStatus(GameManager.Instance.PendingTargetCardSelectionPeerId == _localCharacter.OwnerPeerId
                ? BattleLogTextLocalizer.Localize(GameManager.Instance.PendingTargetCardSelectionPrompt)
                : $"等待玩家 {GameManager.Instance.PendingTargetCardSelectionPeerId} 选择目标牌。");
            return;
        }

        if (GameManager.Instance?.HasPendingHandCardSelection == true)
        {
            SetStatus(GameManager.Instance.PendingHandCardSelectionPeerId == _localCharacter.OwnerPeerId
                ? BattleLogTextLocalizer.Localize(GameManager.Instance.PendingHandCardSelectionPrompt)
                : $"等待玩家 {GameManager.Instance.PendingHandCardSelectionPeerId} 选择手牌。");
            return;
        }

        if (GameManager.Instance?.CurrentPhase == TurnPhase.DiscardPhase)
        {
            int handLimit = Math.Max(0, _localCharacter.CurrentHealth);
            if (string.IsNullOrWhiteSpace(_selectedCardInstanceId))
            {
                SetStatus($"弃牌阶段：手牌最多保留 {handLimit} 张。选择一张牌后点击“弃置选中牌”。");
                return;
            }

            CardInstance? selectedDiscardCard = _localCharacter.FindHandCard(_selectedCardInstanceId);
            SetStatus(selectedDiscardCard is null
                ? "选中的牌已失效。"
                : $"准备弃置：{selectedDiscardCard.DisplayName}。点击“弃置选中牌”。");
            return;
        }

        CharacterSkill? selectedSkill = SelectedSkill;
        if (selectedSkill is not null)
        {
            SetStatus(BuildSelectedSkillStatus(selectedSkill));
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedCardInstanceId))
        {
            SetStatus("你的出牌阶段：选择手牌或主动技能；完成后点击“结束出牌”。");
            return;
        }

        CardInstance? card = _localCharacter.FindHandCard(_selectedCardInstanceId);
        if (card is null)
        {
            SetStatus("选中的牌已失效。");
            return;
        }

        if (card.CardType == CardType.Peach || card.CardType == CardType.Wine)
        {
            SetStatus($"已选择：{card.DisplayName}。点击右侧“对自己使用”。");
            return;
        }

        if (CardRules.IsResponseOnly(card.CardType))
        {
            SetStatus($"已选择：{card.DisplayName}。这张牌只能在响应窗口要求时使用。");
            return;
        }

        if (CardRules.IsEquipment(card.CardType))
        {
            SetStatus($"已选择：{card.DisplayName}。点击“对自己使用”来装备。");
            return;
        }

        if (card.CardType == CardType.IronChain)
        {
            SetStatus($"已选择：{card.DisplayName}。可点击一至两个目标，或重铸摸 1 张牌。");
            return;
        }

        if (card.CardType == CardType.BorrowSword)
        {
            SetStatus($"已选择：{card.DisplayName}。先点有武器的角色，再点其出杀目标。");
            return;
        }

        if (CardRules.IsSlash(card.CardType)
            && GameManager.Instance?.GetMaxTargetCount(_localCharacter, card) > 1)
        {
            SetStatus($"已选择：{card.DisplayName}。方天画戟可指定至多 3 个目标。");
            return;
        }

        SetStatus(CardRules.NeedsTarget(card.CardType)
            ? $"已选择：{card.DisplayName}。点击一个目标。"
            : $"已选择：{card.DisplayName}。点击“对自己使用”。");
    }

    private void RebuildCards()
    {
        if (_cardsContainer is null)
        {
            return;
        }

        foreach (Node child in _cardsContainer.GetChildren())
        {
            child.QueueFree();
        }

        _cardButtons.Clear();

        if (_localCharacter is null)
        {
            return;
        }

        RebuildHarvestCards();
        RebuildTargetCardSelectionCards();
        GameManager? gameManager = GameManager.Instance;
        bool hasPendingHandCardSelection = gameManager?.HasPendingHandCardSelection == true;
        bool canChoosePendingHandCard = hasPendingHandCardSelection
            && gameManager?.PendingHandCardSelectionPeerId == _localCharacter.OwnerPeerId;
        HashSet<string> pendingHandCardIds = hasPendingHandCardSelection
            ? (gameManager?.HandCardSelectionPool.Select(card => card.InstanceId).ToHashSet() ?? new HashSet<string>())
            : new HashSet<string>();

        if (!hasPendingHandCardSelection)
        {
            RebuildActiveSkillButtons();
        }
        else if (canChoosePendingHandCard && gameManager?.CanDeclineHandCardSelection == true)
        {
            Button declineButton = new()
            {
                Text = "放弃\n不弃牌",
                CustomMinimumSize = new Vector2(132f, 84f),
                TooltipText = BattleLogTextLocalizer.Localize(gameManager.PendingHandCardSelectionPrompt)
            };
            CyberStyle.ApplyButton(declineButton, CyberButtonKind.Danger);
            CyberStyle.AttachHoverLift(declineButton);
            declineButton.Pressed += () =>
            {
                if (_inputBridge?.TryDeclineHandCardSelection(_localCharacter) == true)
                {
                    SetSelectedCard(string.Empty);
                    RefreshUI();
                }
            };
            _cardsContainer.AddChild(declineButton);
        }

        List<CardInstance> handCards = _localCharacter.HandCards.ToList();
        for (int index = 0; index < handCards.Count; index++)
        {
            CardInstance card = handCards[index];
            bool disabledForPendingChoice = hasPendingHandCardSelection
                && (!canChoosePendingHandCard || !pendingHandCardIds.Contains(card.InstanceId));
            InkCardButton button = new()
            {
                Text = CardDisplayFormatter.FormatCardFace(card),
                CustomMinimumSize = new Vector2(116f, 156f),
                ToggleMode = true,
                ButtonPressed = card.InstanceId == _selectedCardInstanceId,
                Disabled = disabledForPendingChoice,
                TooltipText = hasPendingHandCardSelection
                    ? (canChoosePendingHandCard
                        ? BattleLogTextLocalizer.Localize(gameManager?.PendingHandCardSelectionPrompt ?? card.Description)
                        : "等待其他玩家选择手牌。")
                    : card.Description
            };
            CyberStyle.ApplyCardButton(button, card, card.InstanceId == _selectedCardInstanceId, disabledForPendingChoice);
            button.Configure(card, card.InstanceId == _selectedCardInstanceId, disabledForPendingChoice);
            CyberStyle.AttachHoverLift(button);

            string instanceId = card.InstanceId;
            button.Pressed += () => SelectCard(instanceId);
            AddFannedHandCardButton(button, index, handCards.Count, card.InstanceId == _selectedCardInstanceId);
            _cardButtons[instanceId] = button;
        }
    }

    private void AddFannedHandCardButton(InkCardButton button, int index, int totalCount, bool selected)
    {
        if (_cardsContainer is null)
        {
            return;
        }

        Control host = new()
        {
            CustomMinimumSize = new Vector2(130f, 180f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        float centerOffset = index - (totalCount - 1f) / 2f;
        float rotation = Mathf.Clamp(centerOffset * 1.7f, -5f, 5f);
        float yOffset = Mathf.Abs(centerOffset) * 2.2f + (selected ? -10f : 0f);
        button.SetAnchorsPreset(LayoutPreset.Center);
        button.OffsetLeft = -58f;
        button.OffsetRight = 58f;
        button.OffsetTop = -78f + yOffset;
        button.OffsetBottom = 78f + yOffset;
        button.RotationDegrees = rotation;
        button.PivotOffset = new Vector2(58f, 78f);
        host.AddChild(button);
        _cardsContainer.AddChild(host);
    }

    private void RebuildActiveSkillButtons()
    {
        if (_cardsContainer is null || _localCharacter is null)
        {
            return;
        }

        foreach (CharacterSkill skill in _localCharacter.Skills.Where(skill => skill.IsActiveSkill))
        {
            string reason = string.Empty;
            bool hasRequiredCost = !skill.RequiresHandCardCost || !string.IsNullOrWhiteSpace(_selectedCardInstanceId);
            bool canActivateWithoutTargets = _inputBridge?.CanActivateSkill(_localCharacter, skill.SkillId, Array.Empty<PlayerCharacter>(), skill.RequiresHandCardCost ? _selectedCardInstanceId : string.Empty, out reason) == true;
            bool disableSkill = !(_inputBridge?.IsInputEnabled ?? false)
                || !hasRequiredCost
                || (skill.TargetingMode == SkillTargetingMode.None && !canActivateWithoutTargets)
                || skill.UsesThisTurn > 0 && skill.UsageScope == SkillUsageScope.OncePerTurn;
            Button button = new()
            {
                Text = $"技能\n{skill.DisplayName}\n{FormatSkillUsage(skill)}",
                CustomMinimumSize = new Vector2(124f, 156f),
                ToggleMode = true,
                ButtonPressed = skill.SkillId == _selectedSkillId,
                Disabled = disableSkill && skill.SkillId != _selectedSkillId,
                TooltipText = BuildSkillTooltip(skill, hasRequiredCost, reason)
            };
            CyberStyle.ApplyButton(button, skill.SkillId == _selectedSkillId ? CyberButtonKind.Primary : CyberButtonKind.Action);
            CyberStyle.AttachHoverLift(button);

            string skillId = skill.SkillId;
            button.Pressed += () => SelectSkill(skillId);
            _cardsContainer.AddChild(button);
        }
    }

    private void RebuildHarvestCards()
    {
        GameManager? gameManager = GameManager.Instance;
        if (_cardsContainer is null || _localCharacter is null || _inputBridge is null || gameManager?.HasPendingHarvestSelection != true)
        {
            return;
        }

        bool canChoose = gameManager.PendingHarvestPeerId == _localCharacter.OwnerPeerId;
        foreach (CardInstance card in gameManager.HarvestPool)
        {
            InkCardButton button = new()
            {
                Text = $"五谷\n\n{CardDisplayFormatter.FormatCardFace(card)}",
                CustomMinimumSize = new Vector2(124f, 156f),
                Disabled = !canChoose,
                TooltipText = canChoose
                    ? "从五谷丰登中选择此牌。"
                    : BattleLogTextLocalizer.Localize(gameManager.PendingHarvestPrompt)
            };
            CyberStyle.ApplyCardButton(button, card, false, !canChoose);
            button.Configure(card, selected: false, disabled: !canChoose);
            CyberStyle.AttachHoverLift(button);

            string instanceId = card.InstanceId;
            button.Pressed += () =>
            {
                if (_inputBridge.TryChooseHarvestCard(_localCharacter, instanceId))
                {
                    SetSelectedCard(string.Empty);
                    RefreshUI();
                }
            };
            _cardsContainer.AddChild(button);
        }
    }

    private void RebuildTargetCardSelectionCards()
    {
        GameManager? gameManager = GameManager.Instance;
        if (_cardsContainer is null || _localCharacter is null || _inputBridge is null || gameManager?.HasPendingTargetCardSelection != true)
        {
            return;
        }

        bool canChoose = gameManager.PendingTargetCardSelectionPeerId == _localCharacter.OwnerPeerId;
        foreach (CardInstance card in gameManager.TargetCardSelectionPool)
        {
            string label = card.CardType == CardType.None
                ? "目标\n手牌"
                : $"目标\n{card.DisplayName}\n{CardDisplayFormatter.FormatCardIdentity(card)}";

            InkCardButton button = new()
            {
                Text = label,
                CustomMinimumSize = new Vector2(124f, 156f),
                Disabled = !canChoose,
                TooltipText = canChoose
                    ? "选择这张目标牌。"
                    : BattleLogTextLocalizer.Localize(gameManager.PendingTargetCardSelectionPrompt)
            };
            CyberStyle.ApplyCardButton(button, card, false, !canChoose);
            button.Configure(card, selected: false, disabled: !canChoose);
            CyberStyle.AttachHoverLift(button);

            string instanceId = card.InstanceId;
            button.Pressed += () =>
            {
                if (_inputBridge.TryChooseTargetCard(_localCharacter, instanceId))
                {
                    SetSelectedCard(string.Empty);
                    RefreshUI();
                }
            };
            _cardsContainer.AddChild(button);
        }
    }

    private void UnbindLocalCharacter()
    {
        if (_localCharacter is null)
        {
            return;
        }

        _localCharacter.OnHandCardsChanged -= HandleLocalHandChanged;
        _localCharacter.OnStatsChanged -= HandleLocalStatsChanged;
        _localCharacter = null;
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        ResolveLocalCharacter();
        RefreshUI();
    }

    private void HandleTurnOwnerChanged(int peerId)
    {
        RefreshUI();
    }

    private void HandlePhaseChanged(TurnPhase phase)
    {
        RefreshUI();
    }

    private void HandleGameStateChanged()
    {
        RefreshUI();
    }

    private void HandleLocalHandChanged(PlayerCharacter character)
    {
        if (_localCharacter == character && _localCharacter.FindHandCard(_selectedCardInstanceId) is null)
        {
            SetSelectedCard(string.Empty);
        }

        RefreshUI();
    }

    private void HandleLocalStatsChanged(PlayerCharacter character)
    {
        RefreshUI();
    }

    private void HandleEndPhasePressed()
    {
        if (GameManager.Instance?.CurrentPhase == TurnPhase.DiscardPhase)
        {
            if (_localCharacter is not null && _inputBridge is not null && !string.IsNullOrWhiteSpace(_selectedCardInstanceId))
            {
                if (_inputBridge.TryDiscardCard(_localCharacter, _selectedCardInstanceId))
                {
                    SetSelectedCard(string.Empty);
                }
            }

            RefreshUI();
            return;
        }

        _inputBridge?.RequestEndPlayPhase();
        RefreshUI();
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }

    private void ApplyChrome()
    {
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel"), CyberPanelKind.Strong);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/VBox/TitleLabel"), title: true);
        CyberStyle.ApplyLabel(_statusLabel, color: CyberStyle.MutedText);
        CyberStyle.ApplyPanel(_selectedPreviewHost, CyberPanelKind.Card);
        CyberStyle.ApplyRichText(_selectedPreviewText);
        CyberStyle.ApplyButton(_endPhaseButton, CyberButtonKind.Primary);
        if (_endPhaseButton is not null)
        {
            CyberStyle.AttachHoverLift(_endPhaseButton);
        }
    }

    private void EnsureSelectedCardPreview()
    {
        _selectedPreviewHost = GetNodeOrNull<Panel>("SelectedCardPreviewHost");
        if (_selectedPreviewHost is null)
        {
            _selectedPreviewHost = new Panel
            {
                Name = "SelectedCardPreviewHost",
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = 70,
                Visible = false
            };
            _selectedPreviewHost.AnchorLeft = 0f;
            _selectedPreviewHost.AnchorRight = 0f;
            _selectedPreviewHost.AnchorTop = 0f;
            _selectedPreviewHost.AnchorBottom = 0f;
            _selectedPreviewHost.OffsetLeft = 14f;
            _selectedPreviewHost.OffsetTop = -178f;
            _selectedPreviewHost.OffsetRight = 352f;
            _selectedPreviewHost.OffsetBottom = 4f;
            AddChild(_selectedPreviewHost);
        }
        LayoutSelectedCardPreview();

        HBoxContainer? row = _selectedPreviewHost.GetNodeOrNull<HBoxContainer>("PreviewRow");
        if (row is null)
        {
            row = new HBoxContainer
            {
                Name = "PreviewRow",
                MouseFilter = MouseFilterEnum.Ignore
            };
            row.SetAnchorsPreset(LayoutPreset.FullRect);
            row.OffsetLeft = 10f;
            row.OffsetTop = 10f;
            row.OffsetRight = -10f;
            row.OffsetBottom = -10f;
            row.AddThemeConstantOverride("separation", 10);
            _selectedPreviewHost.AddChild(row);
        }

        _selectedPreviewCard = row.GetNodeOrNull<InkCardButton>("SelectedPreviewCard");
        if (_selectedPreviewCard is null)
        {
            _selectedPreviewCard = new InkCardButton
            {
                Name = "SelectedPreviewCard",
                CustomMinimumSize = new Vector2(118f, 158f),
                MouseFilter = MouseFilterEnum.Ignore,
                FocusMode = FocusModeEnum.None
            };
            row.AddChild(_selectedPreviewCard);
        }

        _selectedPreviewText = row.GetNodeOrNull<RichTextLabel>("SelectedPreviewText");
        if (_selectedPreviewText is null)
        {
            _selectedPreviewText = new RichTextLabel
            {
                Name = "SelectedPreviewText",
                CustomMinimumSize = new Vector2(176f, 148f),
                MouseFilter = MouseFilterEnum.Ignore,
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false
            };
            row.AddChild(_selectedPreviewText);
        }
    }

    private void UpdateSelectedCardPreview()
    {
        if (_selectedPreviewHost is null || _selectedPreviewCard is null || _selectedPreviewText is null)
        {
            return;
        }

        CardInstance? card = SelectedCard;
        if (card is null)
        {
            _selectedPreviewHost.Visible = false;
            return;
        }

        _selectedPreviewHost.Visible = true;
        LayoutSelectedCardPreview();
        _selectedPreviewCard.Text = CardDisplayFormatter.FormatCardFace(card);
        _selectedPreviewCard.TooltipText = card.Description;
        CyberStyle.ApplyCardButton(_selectedPreviewCard, card, selected: true, disabled: false);
        _selectedPreviewCard.Configure(card, selected: true, disabled: false);
        _selectedPreviewText.Text = BuildSelectedPreviewText(card);
    }

    private void LayoutSelectedCardPreview()
    {
        if (_selectedPreviewHost is null)
        {
            return;
        }

        float availableWidth = Size.X > 1f ? Size.X : 360f;
        float previewWidth = Mathf.Clamp(availableWidth - 28f, 286f, 360f);
        _selectedPreviewHost.OffsetLeft = 14f;
        _selectedPreviewHost.OffsetRight = 14f + previewWidth;
        _selectedPreviewHost.OffsetTop = Size.Y < 220f ? -168f : -178f;
        _selectedPreviewHost.OffsetBottom = 4f;

        bool compact = previewWidth < 320f;
        if (_selectedPreviewCard is not null)
        {
            _selectedPreviewCard.CustomMinimumSize = compact
                ? new Vector2(104f, 146f)
                : new Vector2(118f, 158f);
        }

        if (_selectedPreviewText is not null)
        {
            _selectedPreviewText.CustomMinimumSize = compact
                ? new Vector2(146f, 138f)
                : new Vector2(176f, 148f);
        }
    }

    private static string BuildSelectedPreviewText(CardInstance card)
    {
        string category = CardDisplayFormatter.FormatCardCategory(card.CardType);
        string identity = CardDisplayFormatter.FormatCardIdentity(card);
        string description = string.IsNullOrWhiteSpace(card.Description)
            ? "暂无说明。"
            : card.Description;
        return $"[color=#D8B36C]{CardDisplayFormatter.FormatCardDisplayName(card)}[/color]\n[color=#8C7D63]{identity} | {category}[/color]\n\n[color=#CDBF9C]{EscapeBbcode(description)}[/color]";
    }

    private static string EscapeBbcode(string text)
    {
        return text
            .Replace("[", "[lb]", StringComparison.Ordinal)
            .Replace("]", "[rb]", StringComparison.Ordinal);
    }

    private static string FormatCardIdentity(CardInstance card)
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

    private static string FormatHandCardFace(CardInstance card)
    {
        return $"{FormatCardIdentity(card)}\n\n{FormatCardDisplayName(card)}\n\n{FormatCardCategory(card.CardType)}";
    }

    private static string FormatSuitSymbol(CardSuit suit)
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

    private static string FormatCardDisplayName(CardInstance card)
    {
        return card.CardType switch
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
            CardType.Weapon or CardType.Armor or CardType.OffensiveHorse or CardType.DefensiveHorse or CardType.Treasure => card.DisplayName,
            _ => card.DisplayName
        };
    }

    private static string FormatCardCategory(CardType cardType)
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

    private static string FormatSkillUsage(CharacterSkill skill)
    {
        return skill.UsageScope == SkillUsageScope.OncePerTurn
            ? $"{skill.UsesThisTurn}/1"
            : "可用";
    }

    private string BuildSelectedSkillStatus(CharacterSkill skill)
    {
        string costText = skill.RequiresHandCardCost
            ? string.IsNullOrWhiteSpace(_selectedCardInstanceId)
                ? " 请选择一张手牌作为消耗。"
                : " 已选择消耗牌。"
            : string.Empty;
        string targetText = skill.TargetingMode switch
        {
            SkillTargetingMode.None => "点击技能按钮发动。",
            SkillTargetingMode.Self => "点击“对自己使用”。",
            SkillTargetingMode.OtherCharacter => "点击其他角色。",
            SkillTargetingMode.AnyCharacter => "点击一个角色。",
            SkillTargetingMode.MultipleCharacters => $"点击 {skill.MinTargetCount}-{skill.MaxTargetCount} 个目标。",
            _ => "请选择目标。"
        };

        return $"已选择技能：{skill.DisplayName}（{FormatSkillUsage(skill)}）。{costText} {targetText}";
    }

    private static string BuildSkillTooltip(CharacterSkill skill, bool hasRequiredCost, string reason)
    {
        string cost = skill.RequiresHandCardCost ? "需要选择一张手牌作为消耗。" : "无手牌消耗。";
        string targetMode = skill.TargetingMode switch
        {
            SkillTargetingMode.None => "无需目标",
            SkillTargetingMode.Self => "自己",
            SkillTargetingMode.OtherCharacter => "其他角色",
            SkillTargetingMode.AnyCharacter => "任意角色",
            SkillTargetingMode.MultipleCharacters => "多名角色",
            _ => "按技能规则选择"
        };
        string target = $"目标：{targetMode}（{skill.MinTargetCount}-{skill.MaxTargetCount}）。";
        string availability = hasRequiredCost
            ? string.IsNullOrWhiteSpace(reason)
                ? "选择合法目标后可发动。"
                : BattleLogTextLocalizer.Localize(reason)
            : "请先选择一张手牌作为消耗。";
        return $"{skill.Description}\n{cost}\n{target}\n{availability}";
    }
}
