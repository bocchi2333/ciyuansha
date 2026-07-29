using System.Collections.Generic;
using System.Linq;
using CiyuanSha.Gameplay.Characters;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Generals;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// 瀵瑰眬寮€濮嬪悗锛屾牴鎹ぇ鍘呯帺瀹跺垪琛ㄨ嚜鍔ㄧ敓鎴愮帺瀹舵鐩樸€?/// </summary>
public partial class MatchBoardManager : Control
{
    [Export]
    public NodePath LocalBoardContainerPath { get; set; } = new NodePath();

    [Export]
    public NodePath RemoteBoardContainerPath { get; set; } = new NodePath();

    [Export]
    public PackedScene? GeneralCardViewScene { get; set; }

    [Export]
    public Vector2 LocalBoardSize { get; set; } = new(220f, 330f);

    [Export]
    public Vector2 RemoteBoardSize { get; set; } = new(180f, 270f);

    private static readonly Vector2 DesignBoardSize = new(240f, 360f);

    private Control? _localBoardContainer;
    private Control? _remoteBoardContainer;
    private readonly List<PlayerCharacter> _spawnedCharacters = new();
    private string _boardRosterSignature = string.Empty;

    public override void _Ready()
    {
        _localBoardContainer = GetNodeOrNull<Control>(LocalBoardContainerPath);
        _remoteBoardContainer = GetNodeOrNull<Control>(RemoteBoardContainerPath);

        if (LanMultiplayerManager.Instance is null)
        {
            GD.PushWarning("MatchBoardManager requires LanMultiplayerManager autoload.");
            return;
        }

        LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
        LanMultiplayerManager.Instance.OnLobbyChanged += HandleLobbyChanged;

        if (LanMultiplayerManager.Instance.SessionState == LanSessionState.InMatch)
        {
            RebuildBoards();
        }
    }

    public override void _ExitTree()
    {
        if (LanMultiplayerManager.Instance is null)
        {
            return;
        }

        LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
        LanMultiplayerManager.Instance.OnLobbyChanged -= HandleLobbyChanged;
    }

    public void RebuildBoards()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        List<LanPlayerInfo> players = network.Players.Values.OrderBy(player => player.PeerId).ToList();
        string rosterSignature = BuildRosterSignature(players);
        if (_spawnedCharacters.Count == players.Count && string.Equals(_boardRosterSignature, rosterSignature, System.StringComparison.Ordinal))
        {
            if (network.LastMatchStateSnapshot is not null && !network.IsHost)
            {
                GameManager.Instance?.ApplyMatchStateSnapshot(network.LastMatchStateSnapshot);
            }

            return;
        }

        ClearSpawnedBoards(resetSignature: false);

        foreach (LanPlayerInfo player in players)
        {
            bool isLocalPlayer = player.PeerId == network.LocalPeerId;
            PlayerResponder characterNode = CreateCharacterNode(player, isLocalPlayer);
            RegisterCharacter(player.PeerId, characterNode);

            Control boardView = CreateBoardView(characterNode, isLocalPlayer);
            Control? targetContainer = isLocalPlayer ? _localBoardContainer : _remoteBoardContainer;
            targetContainer?.AddChild(boardView);
        }

        if (network.LastMatchStateSnapshot is not null && !network.IsHost)
        {
                GameManager.Instance?.ApplyMatchStateSnapshot(network.LastMatchStateSnapshot);
        }

        _boardRosterSignature = rosterSignature;
    }

    private PlayerResponder CreateCharacterNode(LanPlayerInfo playerInfo, bool isLocalPlayer)
    {
        GeneralDefinition definition = GeneralCatalog.Resolve(playerInfo.CharacterId, playerInfo.PlayerName);
        bool autoDodgeEnabled = LanMultiplayerManager.Instance?.IsConnected == true
            ? false
            : !isLocalPlayer;

        PlayerResponder characterNode = new()
        {
            Name = $"PlayerCharacter_{playerInfo.PeerId}",
            OwnerPeerId = playerInfo.PeerId,
            CharacterId = definition.GeneralId,
            CharacterName = definition.DisplayName,
            GeneralCardFileName = NormalizeGeneralCardFileName(definition.CardFileName),
            AutoDodgeEnabled = autoDodgeEnabled,
            ConsumeAutoDodgeAfterUse = false
        };

        characterNode.InitializeStats(definition.MaxHealth, definition.MaxHealth, 0);
        characterNode.ApplyGeneralDefinition(definition);
        _spawnedCharacters.Add(characterNode);
        return characterNode;
    }

    private void RegisterCharacter(int peerId, PlayerCharacter characterNode)
    {
        AddChild(characterNode);
        GameManager.Instance?.RegisterPeerCharacter(peerId, characterNode);
    }

    private Control CreateBoardView(PlayerCharacter characterNode, bool isLocalPlayer)
    {
        GeneralCardView cardView = GeneralCardViewScene?.Instantiate<GeneralCardView>() ?? new GeneralCardView();
        cardView.Name = $"BoardView_{characterNode.OwnerPeerId}";
        cardView.CustomMinimumSize = DesignBoardSize;
        cardView.Size = DesignBoardSize;
        cardView.BindCharacter(characterNode);
        return WrapBoardView(cardView, characterNode, isLocalPlayer ? LocalBoardSize : RemoteBoardSize);
    }

    private Control WrapBoardView(GeneralCardView cardView, PlayerCharacter characterNode, Vector2 targetSize)
    {
        float scale = Mathf.Min(targetSize.X / DesignBoardSize.X, targetSize.Y / DesignBoardSize.Y);
        Vector2 displayedSize = DesignBoardSize * scale;
        MarginContainer root = new()
        {
            Name = $"BoardSlot_{characterNode.OwnerPeerId}",
            CustomMinimumSize = displayedSize,
            MouseFilter = MouseFilterEnum.Ignore
        };

        cardView.SetAnchorsPreset(LayoutPreset.TopLeft);
        cardView.OffsetLeft = 0f;
        cardView.OffsetTop = 0f;
        cardView.OffsetRight = DesignBoardSize.X;
        cardView.OffsetBottom = DesignBoardSize.Y;
        cardView.Scale = new Vector2(scale, scale);
        cardView.MouseFilter = MouseFilterEnum.Ignore;
        root.AddChild(cardView);
        return root;
    }

    private void ClearSpawnedBoards(bool resetSignature = true)
    {
        foreach (PlayerCharacter character in _spawnedCharacters)
        {
            if (character.OwnerPeerId > 0)
            {
                GameManager.Instance?.UnregisterPeerCharacter(character.OwnerPeerId);
            }

            character.RemoveFromGroup("player_character");
            if (character.GetParent() == this)
            {
                RemoveChild(character);
            }

            character.QueueFree();
        }

        _spawnedCharacters.Clear();
        ClearChildren(_localBoardContainer);
        ClearChildren(_remoteBoardContainer);
        if (resetSignature)
        {
            _boardRosterSignature = string.Empty;
        }
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        if (state == LanSessionState.InMatch)
        {
            RebuildBoards();
            return;
        }

        if (state == LanSessionState.Offline || state == LanSessionState.InLobby)
        {
            ClearSpawnedBoards();
        }
    }

    private void HandleLobbyChanged(IReadOnlyDictionary<int, LanPlayerInfo> players)
    {
        if (LanMultiplayerManager.Instance?.SessionState == LanSessionState.InMatch)
        {
            RebuildBoards();
        }
    }

    private static void ClearChildren(Node? node)
    {
        if (node is null)
        {
            return;
        }

        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string BuildRosterSignature(IEnumerable<LanPlayerInfo> players)
    {
        return string.Join("|", players
            .OrderBy(player => player.PeerId)
            .Select(player => $"{player.PeerId}:{player.CharacterId}:{player.PlayerName}"));
    }

    private static string NormalizeGeneralCardFileName(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return string.Empty;
        }

        string trimmed = characterId.Trim();
        if (trimmed.Contains('.'))
        {
            return trimmed;
        }

        return $"{trimmed}.png";
    }
}
