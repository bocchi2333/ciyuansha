using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Target selection UI for the current local player.
/// Clicking a target tries to use the selected Slash card from the hand zone.
/// </summary>
public partial class TargetSelectionPanel : Control
{
    [Export]
    public NodePath TargetsContainerPath { get; set; } = new NodePath();

    [Export]
    public NodePath HandZonePanelPath { get; set; } = new NodePath();

    [Export]
    public NodePath StatusLabelPath { get; set; } = new NodePath();

    private VBoxContainer? _targetsContainer;
    private HandZonePanel? _handZonePanel;
    private Label? _statusLabel;
    private ColorRect? _targetStateLine;
    private readonly List<int> _selectedMultiTargetPeerIds = new();
    private readonly HashSet<string> _selectedCoreOptions = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        _targetsContainer = GetNodeOrNull<VBoxContainer>(TargetsContainerPath);
        _handZonePanel = GetNodeOrNull<HandZonePanel>(HandZonePanelPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        ApplyChrome();
        if (_handZonePanel is not null)
        {
            _handZonePanel.OnSelectedCardChanged += HandleSelectedCardChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleNetworkChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnStateSyncRequested += HandleGameStateChanged;
            GameManager.Instance.OnTurnOwnerChanged += HandleTurnOwnerChanged;
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            GameManager.Instance.OnCoreEngineAdvanced += HandleCoreAdvanced;
        }

        RefreshTargets();
    }

    public override void _ExitTree()
    {
        if (_handZonePanel is not null)
        {
            _handZonePanel.OnSelectedCardChanged -= HandleSelectedCardChanged;
        }

        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleNetworkChanged;
        }

        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnStateSyncRequested -= HandleGameStateChanged;
            GameManager.Instance.OnTurnOwnerChanged -= HandleTurnOwnerChanged;
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            GameManager.Instance.OnCoreEngineAdvanced -= HandleCoreAdvanced;
        }
    }

    private void RefreshTargets()
    {
        if (_targetsContainer is null)
        {
            return;
        }

        foreach (Node child in _targetsContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (GameManager.Instance?.IsCoreMatchActive == true)
        {
            RefreshCoreTargetChoice();
            return;
        }

        int localPeerId = LanMultiplayerManager.Instance?.IsConnected == true
            ? LanMultiplayerManager.Instance.LocalPeerId
            : GameManager.Instance?.CurrentTurnPeerId ?? 1;
        GameManager? gameManager = GameManager.Instance;
        bool isTargetingEnabled = gameManager?.CurrentPhase == TurnPhase.PlayPhase
            && gameManager.IsAwaitingPlayerInput
            && gameManager.CurrentTurnPeerId == localPeerId;

        PlayerCharacter? localCharacter = GetTree().GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .FirstOrDefault(character => character.OwnerPeerId == localPeerId);
        CharacterSkill? selectedSkill = _handZonePanel?.SelectedSkill;
        CardInstance? selectedCard = selectedSkill is null ? _handZonePanel?.SelectedCard : null;
        int maxTargets = selectedSkill is not null
            ? selectedSkill.MaxTargetCount
            : selectedCard is not null && gameManager is not null && localCharacter is not null
            ? gameManager.GetMaxTargetCount(localCharacter, selectedCard)
            : 1;
        bool isMultiTargetSelection = (selectedCard is not null || selectedSkill is not null) && maxTargets > 1;
        bool hasPlayableSelection = selectedCard is not null || selectedSkill is not null;
        SetStatus(BuildTargetStatus(gameManager, localCharacter, selectedCard, selectedSkill, isTargetingEnabled));
        UpdateTargetStateLine(isTargetingEnabled, hasPlayableSelection, isMultiTargetSelection, _selectedMultiTargetPeerIds.Count, maxTargets);

        if (!isMultiTargetSelection)
        {
            _selectedMultiTargetPeerIds.Clear();
        }

        foreach (PlayerCharacter target in GetTree().GetNodesInGroup("player_character").OfType<PlayerCharacter>())
        {
            if (!target.IsAlive || ShouldHideTargetButton(target, localPeerId, selectedSkill, isMultiTargetSelection))
            {
                continue;
            }

            int distanceSourcePeerId = selectedCard?.CardType == CardType.BorrowSword && _selectedMultiTargetPeerIds.Count > 0
                ? _selectedMultiTargetPeerIds[0]
                : localPeerId;
            int distance = gameManager?.GetDistanceBetween(distanceSourcePeerId, target.OwnerPeerId) ?? int.MaxValue;
            string reason = string.Empty;
            bool canUseSelection = selectedSkill is not null
                ? CanUseSelectedSkillOnTarget(localCharacter, target, selectedSkill, _handZonePanel?.SelectedCard?.InstanceId ?? string.Empty, out reason)
                : hasPlayableSelection && CanUseSelectedCardOnTarget(localCharacter, target, selectedCard, gameManager, out reason);
            if (!hasPlayableSelection && string.IsNullOrWhiteSpace(reason))
            {
                reason = "请先选择一张手牌或主动技能。";
            }

            bool isSelectedMultiTarget = _selectedMultiTargetPeerIds.Contains(target.OwnerPeerId);
            Button button = new()
            {
                Text = BuildTargetButtonText(target, distance, isSelectedMultiTarget, canUseSelection, reason),
                CustomMinimumSize = new Vector2(190f, 58f),
                Disabled = !isTargetingEnabled || !hasPlayableSelection || (!canUseSelection && !isSelectedMultiTarget),
                TooltipText = string.IsNullOrWhiteSpace(reason)
                    ? $"距离：{distance}"
                    : BattleLogTextLocalizer.Localize(reason)
            };
            CyberStyle.ApplyButton(button, isSelectedMultiTarget ? CyberButtonKind.Primary : canUseSelection ? CyberButtonKind.Action : CyberButtonKind.Neutral);
            CyberStyle.AttachHoverLift(button, 1.04f);

            button.Pressed += () =>
            {
                if (isMultiTargetSelection)
                {
                    ToggleMultiTarget(target.OwnerPeerId, maxTargets);
                    if (_selectedMultiTargetPeerIds.Count >= maxTargets)
                    {
                        SubmitSelectedMultiTargets();
                    }
                    else
                    {
                        RefreshTargets();
                    }

                    return;
                }

                _selectedMultiTargetPeerIds.Clear();
                _handZonePanel?.TryPlaySelectedSlashAt(target);
            };
            _targetsContainer.AddChild(button);
        }

        if (isMultiTargetSelection && _selectedMultiTargetPeerIds.Count > 0)
        {
            string submitReason = string.Empty;
            bool canSubmitSkill = selectedSkill is null
                || localCharacter is not null
                && GameManager.Instance?.CanActivateSkill(
                    localCharacter.OwnerPeerId,
                    selectedSkill.SkillId,
                    _selectedMultiTargetPeerIds,
                    selectedSkill.RequiresHandCardCost ? _handZonePanel?.SelectedCard?.InstanceId ?? string.Empty : string.Empty,
                    out submitReason) == true;
            Button submitTargetsButton = new()
            {
                Text = $"确认目标 ({_selectedMultiTargetPeerIds.Count}/{maxTargets})",
                CustomMinimumSize = new Vector2(190f, 48f),
                Disabled = !isTargetingEnabled || !canSubmitSkill,
                TooltipText = string.IsNullOrWhiteSpace(submitReason)
                    ? "对已选目标使用。"
                    : BattleLogTextLocalizer.Localize(submitReason)
            };
            CyberStyle.ApplyButton(submitTargetsButton, CyberButtonKind.Primary);
            CyberStyle.AttachHoverLift(submitTargetsButton);

            submitTargetsButton.Pressed += SubmitSelectedMultiTargets;
            _targetsContainer.AddChild(submitTargetsButton);
        }

        if (selectedSkill is not null && selectedSkill.TargetingMode == SkillTargetingMode.None)
        {
            string reason = string.Empty;
            bool canUseSkill = localCharacter is not null
                && GameManager.Instance?.CanActivateSkill(localCharacter.OwnerPeerId, selectedSkill.SkillId, Array.Empty<int>(), string.Empty, out reason) == true;
            Button useSkillButton = new()
            {
                Text = "发动技能",
                CustomMinimumSize = new Vector2(190f, 48f),
                Disabled = !isTargetingEnabled || !canUseSkill,
                TooltipText = string.IsNullOrWhiteSpace(reason)
                    ? selectedSkill.Description
                    : BattleLogTextLocalizer.Localize(reason)
            };
            CyberStyle.ApplyButton(useSkillButton, CyberButtonKind.Action);
            CyberStyle.AttachHoverLift(useSkillButton);

            useSkillButton.Pressed += () => _handZonePanel?.TryActivateSelectedSkillAtTargets(Array.Empty<PlayerCharacter>());
            _targetsContainer.AddChild(useSkillButton);
        }

        if (selectedSkill is not null && selectedSkill.TargetingMode == SkillTargetingMode.Self && localCharacter is not null)
        {
            string reason = string.Empty;
            bool canUseSkill = GameManager.Instance?.CanActivateSkill(localCharacter.OwnerPeerId, selectedSkill.SkillId, new[] { localCharacter.OwnerPeerId }, string.Empty, out reason) == true;
            Button selfSkillButton = new()
            {
                Text = "对自己发动",
                CustomMinimumSize = new Vector2(190f, 48f),
                Disabled = !isTargetingEnabled || !canUseSkill,
                TooltipText = string.IsNullOrWhiteSpace(reason)
                    ? selectedSkill.Description
                    : BattleLogTextLocalizer.Localize(reason)
            };
            CyberStyle.ApplyButton(selfSkillButton, CyberButtonKind.Action);
            CyberStyle.AttachHoverLift(selfSkillButton);

            selfSkillButton.Pressed += () => _handZonePanel?.TryActivateSelectedSkillAtTargets(new[] { localCharacter });
            _targetsContainer.AddChild(selfSkillButton);
        }

        if (selectedCard is not null && IsSelfButtonCard(selectedCard) && localCharacter is not null)
        {
            Button selfButton = new()
            {
                Text = "对自己使用",
                CustomMinimumSize = new Vector2(190f, 48f),
                Disabled = !isTargetingEnabled
            };
            CyberStyle.ApplyButton(selfButton, CyberButtonKind.Primary);
            CyberStyle.AttachHoverLift(selfButton);

            selfButton.Pressed += () => _handZonePanel?.TryPlaySelectedSlashAt(localCharacter);
            _targetsContainer.AddChild(selfButton);
        }

        if (selectedCard is not null && CardRules.CanRecast(selectedCard.CardType))
        {
            Button recastButton = new()
            {
                Text = "重铸此牌",
                CustomMinimumSize = new Vector2(190f, 48f),
                Disabled = !isTargetingEnabled
            };
            CyberStyle.ApplyButton(recastButton, CyberButtonKind.Action);
            CyberStyle.AttachHoverLift(recastButton);

            recastButton.Pressed += () =>
            {
                _selectedMultiTargetPeerIds.Clear();
                _handZonePanel?.TryRecastSelectedCard();
                RefreshTargets();
            };
            _targetsContainer.AddChild(recastButton);
        }
    }

    private void RefreshCoreTargetChoice()
    {
        if (_targetsContainer is null) return;
        ChoiceRequest? request = ResolveLocalCoreChoice();
        if (request?.Kind != ChoiceKind.SelectTarget)
        {
            _selectedCoreOptions.Clear();
            SetStatus("目标选择由规则内核发布；当前没有需要你选择的目标。");
            UpdateTargetStateLine(false, false, false, 0, 1);
            return;
        }

        _selectedCoreOptions.RemoveWhere(optionId => request.Options.All(option => option.OptionId != optionId));
        SetStatus($"{request.PromptKey}\n请选择 {request.MinimumSelections}–{request.MaximumSelections} 个合法目标。");
        UpdateTargetStateLine(true, true, request.MaximumSelections > 1, _selectedCoreOptions.Count, request.MaximumSelections);
        foreach (ChoiceOption option in request.Options)
        {
            bool selected = _selectedCoreOptions.Contains(option.OptionId);
            Button button = new()
            {
                Text = selected ? $"✓ {CoreOptionText(option)}" : CoreOptionText(option),
                CustomMinimumSize = new Vector2(190f, 54f),
                Disabled = !option.IsEnabled,
                TooltipText = option.IsEnabled ? option.EntityId : option.DisabledReasonKey,
                ToggleMode = request.MaximumSelections > 1,
                ButtonPressed = selected
            };
            CyberStyle.ApplyButton(button, selected ? CyberButtonKind.Primary : CyberButtonKind.Action);
            CyberStyle.AttachHoverLift(button);
            button.Pressed += () => SelectCoreTarget(request, option.OptionId);
            _targetsContainer.AddChild(button);
        }

        if (request.MaximumSelections > 1)
        {
            Button confirm = new()
            {
                Text = $"确认目标 ({_selectedCoreOptions.Count}/{request.MaximumSelections})",
                CustomMinimumSize = new Vector2(190f, 46f),
                Disabled = _selectedCoreOptions.Count < request.MinimumSelections || _selectedCoreOptions.Count > request.MaximumSelections
            };
            CyberStyle.ApplyButton(confirm, CyberButtonKind.Primary);
            confirm.Pressed += () => SubmitCoreTargets(request);
            _targetsContainer.AddChild(confirm);
        }
        if (request.AllowCancel)
        {
            Button cancel = new() { Text = "取消选择", CustomMinimumSize = new Vector2(190f, 42f) };
            CyberStyle.ApplyButton(cancel, CyberButtonKind.Danger);
            cancel.Pressed += () => LanMultiplayerManager.Instance?.SubmitChoice(ChoiceResult.Cancel(request.RequestId, request.StateRevision));
            _targetsContainer.AddChild(cancel);
        }
    }

    private void SelectCoreTarget(ChoiceRequest request, string optionId)
    {
        if (request.MaximumSelections == 1)
        {
            LanMultiplayerManager.Instance?.SubmitChoice(ChoiceResult.Select(request.RequestId, request.StateRevision, optionId));
            return;
        }
        if (!_selectedCoreOptions.Add(optionId)) _selectedCoreOptions.Remove(optionId);
        else if (_selectedCoreOptions.Count > request.MaximumSelections) _selectedCoreOptions.Remove(optionId);
        RefreshTargets();
    }

    private void SubmitCoreTargets(ChoiceRequest request)
    {
        if (_selectedCoreOptions.Count < request.MinimumSelections || _selectedCoreOptions.Count > request.MaximumSelections) return;
        LanMultiplayerManager.Instance?.SubmitChoice(new ChoiceResult(
            request.RequestId,
            request.StateRevision,
            _selectedCoreOptions.OrderBy(value => value, StringComparer.Ordinal).ToArray()));
    }

    private static string CoreOptionText(ChoiceOption option) => option.LabelKey == "target.player"
        ? $"{option.EntityId} 号位"
        : string.IsNullOrWhiteSpace(option.EntityId) ? option.LabelKey : option.EntityId;

    private static ChoiceRequest? ResolveLocalCoreChoice()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || network.SessionState != LanSessionState.InMatch || network.JoinAsSpectator) return null;
        int seatId = network.Players.TryGetValue(network.LocalPeerId, out LanPlayerInfo? player) ? player.SeatId : 0;
        if (seatId <= 0) return null;
        GameView? view = network.IsHost
            ? GameManager.Instance?.CoreRuntime?.BuildView(ViewerContext.ForPlayer(seatId))
            : network.LastMatchStateSnapshot?.CoreView;
        return view?.PendingChoice?.ActingSeatId == seatId ? view.PendingChoice : null;
    }

    private void HandleCoreAdvanced(CiyuanSha.GameCore.Engine.EngineStepResult _) => RefreshTargets();

    private void ToggleMultiTarget(int peerId, int maxTargets)
    {
        if (_selectedMultiTargetPeerIds.Remove(peerId))
        {
            return;
        }

        if (_selectedMultiTargetPeerIds.Count < maxTargets)
        {
            _selectedMultiTargetPeerIds.Add(peerId);
        }
    }

    private void SubmitSelectedMultiTargets()
    {
        if (_selectedMultiTargetPeerIds.Count == 0)
        {
            return;
        }

        List<PlayerCharacter> sceneCharacters = GetTree().GetNodesInGroup("player_character")
            .OfType<PlayerCharacter>()
            .ToList();
        List<PlayerCharacter> targets = _selectedMultiTargetPeerIds
            .Select(peerId => sceneCharacters.FirstOrDefault(character => character.OwnerPeerId == peerId))
            .OfType<PlayerCharacter>()
            .ToList();
        if (_handZonePanel?.TryPlaySelectedCardAtTargets(targets) == true)
        {
            _selectedMultiTargetPeerIds.Clear();
        }

        RefreshTargets();
    }

    private bool CanUseSelectedCardOnTarget(PlayerCharacter? localCharacter, PlayerCharacter target, CardInstance? selectedCard, GameManager? gameManager, out string reason)
    {
        reason = string.Empty;
        if (selectedCard is null)
        {
            return true;
        }

        if (localCharacter is null || gameManager is null)
        {
            reason = "本地武将或牌局状态尚未载入。";
            return false;
        }

        if (selectedCard.CardType == CardType.BorrowSword)
        {
            if (_selectedMultiTargetPeerIds.Count == 0)
            {
                if (target.OwnerPeerId == localCharacter.OwnerPeerId)
                {
                    reason = "【借刀杀人】第一目标必须是其他有武器的角色。";
                    return false;
                }

                if (target.EquippedWeapon is null)
                {
                    reason = $"{target.CharacterName} 没有武器，不能作为【借刀杀人】第一目标。";
                    return false;
                }

                return true;
            }

            PlayerCharacter? weaponHolder = GetTree().GetNodesInGroup("player_character")
                .OfType<PlayerCharacter>()
                .FirstOrDefault(character => character.OwnerPeerId == _selectedMultiTargetPeerIds[0]);
            if (weaponHolder is null || weaponHolder == target)
            {
                reason = "【借刀杀人】第二目标必须与第一目标不同。";
                return false;
            }

            return gameManager.CanUseSlashOnTarget(weaponHolder, target, out reason);
        }

        return gameManager.CanUseCardOnTarget(localCharacter, target, selectedCard.CardType, out reason);
    }

    private bool CanUseSelectedSkillOnTarget(PlayerCharacter? localCharacter, PlayerCharacter target, CharacterSkill selectedSkill, string costCardInstanceId, out string reason)
    {
        reason = string.Empty;
        if (localCharacter is null || selectedSkill is null)
        {
            reason = "本地武将或已选技能尚未载入。";
            return false;
        }

        if (selectedSkill.TargetingMode == SkillTargetingMode.None)
        {
            reason = "这个技能不需要选择目标。";
            return false;
        }

        if (selectedSkill.TargetingMode == SkillTargetingMode.Self && target != localCharacter)
        {
            reason = "这个技能只能以自己为目标。";
            return false;
        }

        if (selectedSkill.TargetingMode == SkillTargetingMode.OtherCharacter && target == localCharacter)
        {
            reason = "这个技能必须以其他角色为目标。";
            return false;
        }

        return GameManager.Instance?.CanActivateSkill(
            localCharacter.OwnerPeerId,
            selectedSkill.SkillId,
            new[] { target.OwnerPeerId },
            selectedSkill.RequiresHandCardCost ? costCardInstanceId : string.Empty,
            out reason) == true;
    }

    private static bool ShouldHideTargetButton(PlayerCharacter target, int localPeerId, CharacterSkill? selectedSkill, bool isMultiTargetSelection)
    {
        if (selectedSkill is null)
        {
            return target.OwnerPeerId == localPeerId && !isMultiTargetSelection;
        }

        return selectedSkill.TargetingMode switch
        {
            SkillTargetingMode.None => true,
            SkillTargetingMode.Self => target.OwnerPeerId != localPeerId,
            SkillTargetingMode.OtherCharacter => target.OwnerPeerId == localPeerId,
            _ => false
        };
    }

    private static bool IsSelfButtonCard(CardInstance card)
    {
        if (CardRules.IsResponseOnly(card.CardType))
        {
            return false;
        }

        return card.CardType == CardType.Peach
            || CardRules.IsSelfTargeted(card.CardType)
            || !CardRules.NeedsTarget(card.CardType);
    }

    private static string BuildTargetButtonText(PlayerCharacter target, int distance, bool selected, bool canUse, string reason)
    {
        string state = selected
            ? "已选"
            : canUse
                ? "可选"
                : "锁定";
        string distanceText = distance == int.MaxValue ? "距离 -" : $"距离 {distance}";
        string hint = string.IsNullOrWhiteSpace(reason) || canUse || selected
            ? distanceText
            : "查看原因";
        return $"{state}\n{target.CharacterName}  {hint}";
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
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel"), CyberPanelKind.Default);
        EnsureTargetStateLine();
        Label? titleLabel = GetNodeOrNull<Label>("Panel/VBox/TitleLabel");
        if (titleLabel is not null)
        {
            titleLabel.Text = "目标 / 操作";
        }

        CyberStyle.ApplyLabel(titleLabel, title: true);
        CyberStyle.ApplyLabel(_statusLabel, color: CyberStyle.MutedText);
    }

    private void EnsureTargetStateLine()
    {
        Control? panel = GetNodeOrNull<Control>("Panel");
        if (panel is null)
        {
            return;
        }

        _targetStateLine = panel.GetNodeOrNull<ColorRect>("TargetStateLine");
        if (_targetStateLine is not null)
        {
            return;
        }

        _targetStateLine = new ColorRect
        {
            Name = "TargetStateLine",
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(CyberStyle.MutedText.R, CyberStyle.MutedText.G, CyberStyle.MutedText.B, 0.35f)
        };
        _targetStateLine.AnchorLeft = 0.12f;
        _targetStateLine.AnchorRight = 0.88f;
        _targetStateLine.AnchorTop = 0f;
        _targetStateLine.AnchorBottom = 0f;
        _targetStateLine.OffsetTop = 6f;
        _targetStateLine.OffsetBottom = 8f;
        panel.AddChild(_targetStateLine);
        panel.MoveChild(_targetStateLine, 1);
    }

    private void UpdateTargetStateLine(bool isTargetingEnabled, bool hasPlayableSelection, bool isMultiTargetSelection, int selectedCount, int maxTargets)
    {
        if (_targetStateLine is null)
        {
            return;
        }

        if (!isTargetingEnabled)
        {
            _targetStateLine.Color = new Color(CyberStyle.MutedText.R, CyberStyle.MutedText.G, CyberStyle.MutedText.B, 0.28f);
            return;
        }

        if (!hasPlayableSelection)
        {
            _targetStateLine.Color = new Color(CyberStyle.PaperDark.R, CyberStyle.PaperDark.G, CyberStyle.PaperDark.B, 0.46f);
            return;
        }

        if (isMultiTargetSelection && selectedCount > 0 && selectedCount < maxTargets)
        {
            _targetStateLine.Color = new Color(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, 0.58f);
            return;
        }

        _targetStateLine.Color = new Color(CyberStyle.NeonJade.R, CyberStyle.NeonJade.G, CyberStyle.NeonJade.B, 0.52f);
    }

    private static string BuildTargetStatus(GameManager? gameManager, PlayerCharacter? localCharacter, CardInstance? selectedCard, CharacterSkill? selectedSkill, bool isTargetingEnabled)
    {
        if (gameManager is null)
        {
            return "目标区：等待牌局状态。";
        }

        if (localCharacter is null)
        {
            return "目标区：等待你的武将载入。";
        }

        if (!isTargetingEnabled)
        {
            if (gameManager.HasPendingResponseWindow)
            {
                return gameManager.PendingResponsePeerId == localCharacter.OwnerPeerId
                    ? "目标区锁定：请先处理中央响应窗口。"
                    : $"目标区锁定：等待玩家 {gameManager.PendingResponsePeerId} 响应。";
            }

            if (gameManager.IsAwaitingDiscardInput)
            {
                return "目标区锁定：当前是弃牌阶段。";
            }

            return gameManager.CurrentTurnPeerId == localCharacter.OwnerPeerId
                ? "目标区锁定：等待当前动作结算完成。"
                : $"目标区锁定：等待玩家 {gameManager.CurrentTurnPeerId} 操作。";
        }

        if (selectedSkill is not null)
        {
            return selectedSkill.TargetingMode == SkillTargetingMode.None
                ? "已选技能无需目标，点击发动技能。"
                : $"为技能【{selectedSkill.DisplayName}】选择目标。";
        }

        if (selectedCard is null)
        {
            return "先选择一张手牌，再选择目标或自用操作。";
        }

        if (IsSelfButtonCard(selectedCard))
        {
            return $"【{selectedCard.DisplayName}】：点击“对自己使用”。";
        }

        return $"【{selectedCard.DisplayName}】：选择一个合法目标。";
    }

    private void HandleNetworkChanged(LanSessionState state)
    {
        RefreshTargets();
    }

    private void HandleSelectedCardChanged()
    {
        _selectedMultiTargetPeerIds.Clear();
        RefreshTargets();
    }

    private void HandleGameStateChanged()
    {
        RefreshTargets();
    }

    private void HandleTurnOwnerChanged(int peerId)
    {
        RefreshTargets();
    }

    private void HandlePhaseChanged(TurnPhase phase)
    {
        RefreshTargets();
    }
}
