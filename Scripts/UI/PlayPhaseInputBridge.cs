using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Actions;
using CiyuanSha.Gameplay.Battle;
using CiyuanSha.Gameplay.Cards;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// 连接 UI 与 GameManager 的轻量桥接脚本。
/// 负责在出牌阶段接收玩家输入并转换为动作。
/// </summary>
public partial class PlayPhaseInputBridge : Node
{
    public bool IsInputEnabled { get; private set; }

    public override void _Ready()
    {
        if (GameManager.Instance is null)
        {
            GD.PushWarning("PlayPhaseInputBridge could not find GameManager.Instance in _Ready.");
            return;
        }

        GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        GameManager.Instance.OnPlayPhaseInputRequested += HandlePlayPhaseInputRequested;
        GameManager.Instance.OnDiscardPhaseInputRequested += HandleDiscardPhaseInputRequested;
        SyncInputState();
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is null)
        {
            return;
        }

        GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        GameManager.Instance.OnPlayPhaseInputRequested -= HandlePlayPhaseInputRequested;
        GameManager.Instance.OnDiscardPhaseInputRequested -= HandleDiscardPhaseInputRequested;
    }

    /// <summary>
    /// 由 UI 层调用，将一次基础伤害行为转换为 DamageAction。
    /// </summary>
    public bool TrySubmitDamageAction(Node? source, Node? target, int damageValue, DamageType damageType = DamageType.Physical)
    {
        CardInstance? slashCard = null;
        if (source is PlayerCharacter sourceCharacter)
        {
            slashCard = sourceCharacter.HandCards.FirstOrDefault(card => CardRules.IsSlash(card.CardType));
        }

        return TryPlaySlashCard(source as PlayerCharacter, target as PlayerCharacter, slashCard?.InstanceId ?? string.Empty, damageType, damageValue);
    }

    public bool TryPlaySlashCard(PlayerCharacter? source, PlayerCharacter? target, string cardInstanceId, DamageType damageType = DamageType.Physical, int damageValueOverride = 1)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager?.ActionManager is null)
        {
            GD.PushWarning("GameManager or ActionManager is not ready.");
            return false;
        }

        if (!IsInputEnabled)
        {
            return false;
        }

        if (target is null)
        {
            GD.PushWarning("Target is required when submitting a damage action.");
            return false;
        }

        if (damageValueOverride <= 0)
        {
            GD.PushWarning("Damage value must be greater than 0.");
            return false;
        }

        if (source is not null && !gameManager.CanUseSlashOnTarget(source, target, out string reason))
        {
            GD.PushWarning(reason);
            return false;
        }

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            if (gameManager.CurrentTurnPeerId != networkManager.LocalPeerId)
            {
                GD.PushWarning("It is not the local player's turn.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(cardInstanceId))
            {
                GD.PushWarning("Network Slash play requires a valid card instance id.");
                return false;
            }

            NetworkPlayCommand command = new()
            {
                CommandType = NetworkPlayCommandType.BasicAttack,
                TargetPeerId = target.OwnerPeerId,
                DamageValue = damageValueOverride,
                DamageType = damageType.ToString(),
                CardInstanceId = cardInstanceId
            };

            bool sent = networkManager.SubmitPlayCommand(command);
            if (sent)
            {
                IsInputEnabled = false;
            }

            return sent;
        }

        if (source is not null && !string.IsNullOrWhiteSpace(cardInstanceId))
        {
            bool submittedCommand = gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.BasicAttack,
                TargetPeerId = target.OwnerPeerId,
                DamageValue = damageValueOverride,
                DamageType = damageType.ToString(),
                CardInstanceId = cardInstanceId
            });

            if (submittedCommand)
            {
                IsInputEnabled = gameManager.IsAwaitingPlayerInput && IsLocalTurn();
            }

            return submittedCommand;
        }

        DamageInfo damageInfo = new(source, target, damageValueOverride, damageType);
        DamageAction damageAction = new(gameManager.ActionManager, damageInfo);
        bool submitted = gameManager.SubmitPlayPhaseAction(damageAction);
        if (submitted)
        {
            IsInputEnabled = false;
        }
        return submitted;
    }

    public bool TryPlayPeachCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager?.ActionManager is null || source is null || !IsInputEnabled)
        {
            return false;
        }

        if (source.CurrentHealth >= source.MaxHealth)
        {
            GD.PushWarning($"{source.CharacterName} cannot use Peach at full health.");
            return false;
        }

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            if (gameManager.CurrentTurnPeerId != networkManager.LocalPeerId)
            {
                GD.PushWarning("It is not the local player's turn.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(cardInstanceId))
            {
                GD.PushWarning("Network Peach play requires a valid card instance id.");
                return false;
            }

            bool sent = networkManager.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.UsePeach,
                CardInstanceId = cardInstanceId
            });

            if (sent)
            {
                IsInputEnabled = false;
            }

            return sent;
        }

        bool submitted = gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, new NetworkPlayCommand
        {
            CommandType = NetworkPlayCommandType.UsePeach,
            CardInstanceId = cardInstanceId
        });
        if (submitted)
        {
            IsInputEnabled = false;
        }

        return submitted;
    }

    public bool TryEquipCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || !IsInputEnabled)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.UseEquipment,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            if (gameManager.CurrentTurnPeerId != networkManager.LocalPeerId)
            {
                GD.PushWarning("It is not the local player's turn.");
                return false;
            }

            bool sent = networkManager.SubmitPlayCommand(command);
            if (sent)
            {
                IsInputEnabled = false;
            }

            return sent;
        }

        bool submitted = gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
        if (submitted)
        {
            IsInputEnabled = false;
        }

        return submitted;
    }

    public bool TryUseCard(PlayerCharacter? source, PlayerCharacter? target, string cardInstanceId)
    {
        return TryUseCard(source, target is null ? Array.Empty<PlayerCharacter>() : new[] { target }, cardInstanceId);
    }

    public bool TryUseCard(PlayerCharacter? source, IEnumerable<PlayerCharacter> targets, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || !IsInputEnabled)
        {
            return false;
        }

        CardInstance? card = source.FindHandCard(cardInstanceId);
        if (card is null)
        {
            return false;
        }

        if (CardRules.IsSlash(card.CardType))
        {
            List<PlayerCharacter> slashTargets = targets
                .Where(target => target is not null)
                .Distinct()
                .ToList();
            if (slashTargets.Count <= 1)
            {
                return TryPlaySlashCard(source, slashTargets.FirstOrDefault(), cardInstanceId, damageValueOverride: card.DamageValue);
            }

            List<int> slashTargetPeerIds = slashTargets
                .Select(target => target.OwnerPeerId)
                .Distinct()
                .ToList();
            NetworkPlayCommand slashCommand = new()
            {
                CommandType = NetworkPlayCommandType.UseCard,
                CardInstanceId = cardInstanceId,
                TargetPeerId = slashTargetPeerIds.FirstOrDefault(),
                TargetPeerIds = slashTargetPeerIds,
                DamageValue = card.DamageValue,
                DamageType = DamageType.Physical.ToString()
            };

            LanMultiplayerManager? slashNetworkManager = LanMultiplayerManager.Instance;
            bool slashSubmitted = slashNetworkManager?.IsConnected == true
                ? slashNetworkManager.SubmitPlayCommand(slashCommand)
                : gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, slashCommand);
            if (slashSubmitted)
            {
                IsInputEnabled = slashNetworkManager?.IsConnected == true
                    ? false
                    : gameManager.IsAwaitingPlayerInput && IsLocalTurn();
            }

            return slashSubmitted;
        }

        if (card.CardType == CardType.Peach)
        {
            return TryPlayPeachCard(source, cardInstanceId);
        }

        if (CardRules.IsEquipment(card.CardType))
        {
            return TryEquipCard(source, cardInstanceId);
        }

        List<int> targetPeerIds = targets
            .Where(target => target is not null)
            .Select(target => target.OwnerPeerId)
            .Distinct()
            .ToList();
        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.UseCard,
            CardInstanceId = cardInstanceId,
            TargetPeerId = targetPeerIds.FirstOrDefault(),
            TargetPeerIds = targetPeerIds
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        bool submitted;
        if (networkManager?.IsConnected == true)
        {
            submitted = networkManager.SubmitPlayCommand(command);
            if (submitted)
            {
                IsInputEnabled = false;
            }

            return submitted;
        }

        submitted = gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
        if (submitted)
        {
            IsInputEnabled = false;
        }
        return submitted;
    }

    public bool TryRecastCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || !IsInputEnabled)
        {
            return false;
        }

        CardInstance? card = source.FindHandCard(cardInstanceId);
        if (card is null || !CardRules.CanRecast(card.CardType))
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.RecastCard,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        bool submitted = networkManager?.IsConnected == true
            ? networkManager.SubmitPlayCommand(command)
            : gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
        if (submitted)
        {
            IsInputEnabled = networkManager?.IsConnected == true
                ? false
                : gameManager.IsAwaitingPlayerInput && IsLocalTurn();
        }

        return submitted;
    }

    public bool TryDiscardCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || !IsInputEnabled || gameManager.CurrentPhase != TurnPhase.DiscardPhase)
        {
            return false;
        }

        if (source.FindHandCard(cardInstanceId) is null)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.DiscardCard,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            bool sent = networkManager.SubmitPlayCommand(command);
            if (sent)
            {
                IsInputEnabled = false;
            }

            return sent;
        }

        bool submitted = gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
        if (submitted)
        {
            IsInputEnabled = gameManager.IsAwaitingDiscardInput && IsLocalTurn();
        }
        return submitted;
    }

    public bool TryChooseHarvestCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || gameManager.PendingHarvestPeerId != source.OwnerPeerId)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.ChooseHarvestCard,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            return networkManager.SubmitPlayCommand(command);
        }

        return gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
    }

    public bool TryChooseTargetCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || gameManager.PendingTargetCardSelectionPeerId != source.OwnerPeerId)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.ChooseTargetCard,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            return networkManager.SubmitPlayCommand(command);
        }

        return gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
    }

    public bool TryChooseHandCard(PlayerCharacter? source, string cardInstanceId)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || gameManager.PendingHandCardSelectionPeerId != source.OwnerPeerId)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.ChooseHandCard,
            CardInstanceId = cardInstanceId
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            return networkManager.SubmitPlayCommand(command);
        }

        return gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
    }

    public bool TryDeclineHandCardSelection(PlayerCharacter? source)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null
            || source is null
            || gameManager.PendingHandCardSelectionPeerId != source.OwnerPeerId
            || !gameManager.CanDeclineHandCardSelection)
        {
            return false;
        }

        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.DeclineHandCardSelection
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            return networkManager.SubmitPlayCommand(command);
        }

        return gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
    }

    public bool TryActivateSkill(PlayerCharacter? source, string skillId, IEnumerable<PlayerCharacter>? targets = null, string cardInstanceId = "")
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || !IsInputEnabled || string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        List<int> targetPeerIds = (targets ?? Array.Empty<PlayerCharacter>())
            .Where(target => target is not null)
            .Select(target => target.OwnerPeerId)
            .Distinct()
            .ToList();
        NetworkPlayCommand command = new()
        {
            CommandType = NetworkPlayCommandType.ActivateSkill,
            SkillId = skillId,
            CardInstanceId = cardInstanceId ?? string.Empty,
            TargetPeerId = targetPeerIds.FirstOrDefault(),
            TargetPeerIds = targetPeerIds
        };

        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected != true
            && !gameManager.CanActivateSkill(source.OwnerPeerId, skillId, targetPeerIds, cardInstanceId ?? string.Empty, out string reason))
        {
            GD.PushWarning(reason);
            return false;
        }

        bool submitted = networkManager?.IsConnected == true
            ? networkManager.SubmitPlayCommand(command)
            : gameManager.TryHandleNetworkPlayCommand(source.OwnerPeerId, command);
        if (submitted)
        {
            IsInputEnabled = false;
        }

        return submitted;
    }

    public bool CanActivateSkill(PlayerCharacter? source, string skillId, IEnumerable<PlayerCharacter>? targets, string cardInstanceId, out string reason)
    {
        reason = string.Empty;
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || source is null || string.IsNullOrWhiteSpace(skillId))
        {
            reason = "GameManager, source, or skill id is missing.";
            return false;
        }

        List<int> targetPeerIds = (targets ?? Array.Empty<PlayerCharacter>())
            .Where(target => target is not null)
            .Select(target => target.OwnerPeerId)
            .Distinct()
            .ToList();
        return gameManager.CanActivateSkill(source.OwnerPeerId, skillId, targetPeerIds, cardInstanceId, out reason);
    }

    /// <summary>
    /// 由 UI 层调用，直接提交任意自定义动作。
    /// </summary>
    public bool TrySubmitAction(GameAction action)
    {
        GameManager? gameManager = GameManager.Instance;
        if (gameManager is null || !IsInputEnabled)
        {
            return false;
        }

        if (LanMultiplayerManager.Instance?.IsConnected == true)
        {
            GD.PushWarning("TrySubmitAction does not support arbitrary network actions yet. Use specific network commands instead.");
            return false;
        }

        bool submitted = gameManager.SubmitPlayPhaseAction(action);
        if (submitted)
        {
            IsInputEnabled = false;
        }

        return submitted;
    }

    /// <summary>
    /// 由 UI 层调用，请求结束当前出牌阶段。
    /// </summary>
    public void RequestEndPlayPhase()
    {
        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;
        if (networkManager?.IsConnected == true)
        {
            networkManager.SubmitPlayCommand(new NetworkPlayCommand
            {
                CommandType = NetworkPlayCommandType.EndPlayPhase
            });
            return;
        }

        GameManager.Instance?.EndPlayPhase();
    }

    private void HandlePhaseChanged(TurnPhase phase)
    {
        IsInputEnabled = IsLocalTurn()
            && ((phase == TurnPhase.PlayPhase && GameManager.Instance?.IsAwaitingPlayerInput == true)
                || (phase == TurnPhase.DiscardPhase && GameManager.Instance?.IsAwaitingDiscardInput == true));
    }

    private void HandlePlayPhaseInputRequested()
    {
        IsInputEnabled = IsLocalTurn();
    }

    private void HandleDiscardPhaseInputRequested()
    {
        IsInputEnabled = IsLocalTurn();
    }

    private void SyncInputState()
    {
        IsInputEnabled = IsLocalTurn()
            && (GameManager.Instance?.IsAwaitingPlayerInput == true
                || GameManager.Instance?.IsAwaitingDiscardInput == true);
    }

    private static bool IsLocalTurn()
    {
        GameManager? gameManager = GameManager.Instance;
        LanMultiplayerManager? networkManager = LanMultiplayerManager.Instance;

        if (gameManager is null)
        {
            return false;
        }

        if (networkManager?.IsConnected == true)
        {
            return gameManager.CurrentTurnPeerId == networkManager.LocalPeerId;
        }

        return true;
    }
}
