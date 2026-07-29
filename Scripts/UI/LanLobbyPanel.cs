using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Gameplay.Generals;
using CiyuanSha.Gameplay.Skills;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.GameCore.Modes;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// LAN lobby UI binding script.
/// Supports host, join, ready, start, and sample general selection.
/// </summary>
public partial class LanLobbyPanel : Control
{
    [Export]
    public NodePath PlayerNameLineEditPath { get; set; } = new NodePath();

    [Export]
    public NodePath AddressLineEditPath { get; set; } = new NodePath();

    [Export]
    public NodePath CharacterIdLineEditPath { get; set; } = new NodePath();

    [Export]
    public NodePath CharacterOptionButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath CharacterDescriptionLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath StatusLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath LanAddressLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath PlayersLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath HostButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath JoinButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath ReadyButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath StartButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath LeaveButtonPath { get; set; } = new NodePath();

    private LineEdit? _playerNameLineEdit;
    private LineEdit? _addressLineEdit;
    private LineEdit? _characterIdLineEdit;
    private OptionButton? _characterOptionButton;
    private Label? _characterDescriptionLabel;
    private Label? _statusLabel;
    private Label? _lanAddressLabel;
    private RichTextLabel? _playersLabel;
    private Button? _hostButton;
    private Button? _joinButton;
    private Button? _readyButton;
    private Button? _startButton;
    private Button? _leaveButton;
    private OptionButton? _modeOptionButton;
    private OptionButton? _botDifficultyOptionButton;
    private Button? _addBotButton;
    private Button? _removeBotButton;
    private TextureRect? _generalPortrait;
    private Label? _generalNameLabel;
    private bool _localReady;
    private bool _quickLaunchApplied;
    private bool _autoReadyAfterQuickLaunch;
    private bool _autoStartWhenReady;
    private bool _quickLaunchStartedMatch;

    public override void _Ready()
    {
        CacheNodes();
        WireButtons();
        PopulateGeneralOptions();
        PopulateModeOptions();
        ApplyChrome();

        if (LanMultiplayerManager.Instance is null)
        {
            SetStatus("联机管理器未加载，请重新启动游戏。 ");
            return;
        }

        LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionStateChanged;
        LanMultiplayerManager.Instance.OnLobbyChanged += HandleLobbyChanged;
        LanMultiplayerManager.Instance.OnNetworkMessage += HandleNetworkMessage;
        LanMultiplayerManager.Instance.OnNetworkError += HandleNetworkError;

        RefreshLanAddresses();
        RefreshCharacterDescription();
        RefreshUI();
        HandleLobbyChanged(LanMultiplayerManager.Instance.Players);
        CallDeferred(nameof(ApplyQuickLaunchFromArgs));
    }

    public override void _ExitTree()
    {
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionStateChanged;
            LanMultiplayerManager.Instance.OnLobbyChanged -= HandleLobbyChanged;
            LanMultiplayerManager.Instance.OnNetworkMessage -= HandleNetworkMessage;
            LanMultiplayerManager.Instance.OnNetworkError -= HandleNetworkError;
        }

        if (_characterOptionButton is not null)
        {
            _characterOptionButton.ItemSelected -= HandleGeneralSelected;
        }
        if (_modeOptionButton is not null)
        {
            _modeOptionButton.ItemSelected -= HandleModeSelected;
        }
        if (_botDifficultyOptionButton is not null)
        {
            _botDifficultyOptionButton.ItemSelected -= HandleBotDifficultySelected;
        }
    }

    public void HostLobby()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        _localReady = false;
        network.HostGame(GetPlayerName(), GetCharacterId());
        RefreshUI();
    }

    public void JoinLobby()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        string address = _addressLineEdit?.Text?.Trim() ?? "127.0.0.1";
        address = ParseAddressAndApplyPort(network, address);
        _localReady = false;
        network.JoinGame(address, GetPlayerName(), GetCharacterId());
        RefreshUI();
    }

    public void ToggleReady()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || !network.IsConnected)
        {
            return;
        }

        SubmitReadyState(!_localReady);
        RefreshUI();
    }

    public void StartMatch()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        if (IsMatchEndedInSession(network))
        {
            network.ReturnToLobby();
            _localReady = false;
            RefreshUI();
            return;
        }

        network.StartMatch();
        RefreshUI();
    }

    public void LeaveLobby()
    {
        _localReady = false;
        LanMultiplayerManager.Instance?.LeaveSession();
        RefreshUI();
    }

    private void CacheNodes()
    {
        _playerNameLineEdit = GetNodeOrNull<LineEdit>(PlayerNameLineEditPath);
        _addressLineEdit = GetNodeOrNull<LineEdit>(AddressLineEditPath);
        _characterIdLineEdit = GetNodeOrNull<LineEdit>(CharacterIdLineEditPath);
        _characterOptionButton = GetNodeOrNull<OptionButton>(CharacterOptionButtonPath);
        _characterDescriptionLabel = GetNodeOrNull<Label>(CharacterDescriptionLabelPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _lanAddressLabel = GetNodeOrNull<Label>(LanAddressLabelPath);
        _playersLabel = GetNodeOrNull<RichTextLabel>(PlayersLabelPath);
        _hostButton = GetNodeOrNull<Button>(HostButtonPath);
        _joinButton = GetNodeOrNull<Button>(JoinButtonPath);
        _readyButton = GetNodeOrNull<Button>(ReadyButtonPath);
        _startButton = GetNodeOrNull<Button>(StartButtonPath);
        _leaveButton = GetNodeOrNull<Button>(LeaveButtonPath);
        _modeOptionButton = GetNodeOrNull<OptionButton>("Panel/ModeOptionButton");
        _botDifficultyOptionButton = GetNodeOrNull<OptionButton>("Panel/BotDifficultyOptionButton");
        _addBotButton = GetNodeOrNull<Button>("Panel/AddBotButton");
        _removeBotButton = GetNodeOrNull<Button>("Panel/RemoveBotButton");
        _generalPortrait = GetNodeOrNull<TextureRect>("Panel/GeneralPortrait");
        _generalNameLabel = GetNodeOrNull<Label>("Panel/GeneralNameLabel");
    }

    private void WireButtons()
    {
        if (_hostButton is not null)
        {
            _hostButton.Pressed += HostLobby;
        }

        if (_joinButton is not null)
        {
            _joinButton.Pressed += JoinLobby;
        }

        if (_readyButton is not null)
        {
            _readyButton.Pressed += ToggleReady;
        }

        if (_startButton is not null)
        {
            _startButton.Pressed += StartMatch;
        }

        if (_leaveButton is not null)
        {
            _leaveButton.Pressed += LeaveLobby;
        }

        if (_characterOptionButton is not null)
        {
            _characterOptionButton.ItemSelected += HandleGeneralSelected;
        }
        if (_modeOptionButton is not null)
        {
            _modeOptionButton.ItemSelected += HandleModeSelected;
        }
        if (_botDifficultyOptionButton is not null)
        {
            _botDifficultyOptionButton.ItemSelected += HandleBotDifficultySelected;
        }
        if (_addBotButton is not null)
        {
            _addBotButton.Pressed += AddBot;
        }
        if (_removeBotButton is not null)
        {
            _removeBotButton.Pressed += RemoveLastBot;
        }
    }

    private void PopulateModeOptions()
    {
        if (_modeOptionButton is not null)
        {
            _modeOptionButton.Clear();
            AddModeOption("单挑", BuiltInModeIds.Duel);
            AddModeOption("四人身份", BuiltInModeIds.Identity);
            AddModeOption("四人 2v2", BuiltInModeIds.TeamTwoVersusTwo);
            AddModeOption("Boss / PvE", BuiltInModeIds.Boss);
        }
        if (_botDifficultyOptionButton is not null)
        {
            _botDifficultyOptionButton.Clear();
            foreach (BotDifficulty difficulty in System.Enum.GetValues<BotDifficulty>())
            {
                _botDifficultyOptionButton.AddItem(difficulty switch
                {
                    BotDifficulty.Easy => "简单",
                    BotDifficulty.Standard => "标准",
                    _ => "困难"
                }, (int)difficulty);
            }
            _botDifficultyOptionButton.Select((int)BotDifficulty.Standard);
        }
    }

    private void AddModeOption(string label, string modeId)
    {
        _modeOptionButton!.AddItem(label);
        _modeOptionButton.SetItemMetadata(_modeOptionButton.ItemCount - 1, modeId);
    }

    private void PopulateGeneralOptions()
    {
        if (_characterOptionButton is null)
        {
            return;
        }

        _characterOptionButton.Clear();

        foreach (GeneralDefinition definition in GeneralCatalog.All.OrderBy(definition => definition.DisplayName))
        {
            _characterOptionButton.AddItem(definition.DisplayName);
            int index = _characterOptionButton.ItemCount - 1;
            _characterOptionButton.SetItemMetadata(index, definition.GeneralId);
        }

        if (_characterOptionButton.ItemCount > 0)
        {
            _characterOptionButton.Select(0);
            ApplySelectedGeneralToField();
        }
    }

    private void RefreshLanAddresses()
    {
        if (_lanAddressLabel is null)
        {
            return;
        }

        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        string[] addresses = (network?.GetLocalLanAddresses() ?? System.Array.Empty<string>())
            .Where(IsUsefulLanAddress)
            .Take(2)
            .ToArray();
        if (addresses.Length == 0)
        {
            addresses = new[] { "不可用" };
        }
        int port = network?.Port ?? 24567;
        _lanAddressLabel.Text = $"本机房间地址  {string.Join(", ", addresses)}  ·  端口 {port}";
    }

    private void RefreshUI()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null)
        {
            return;
        }

        bool connected = network.IsConnected;
        bool matchEnded = IsMatchEndedInSession(network);

        if (_hostButton is not null)
        {
            _hostButton.Disabled = connected;
        }

        if (_joinButton is not null)
        {
            _joinButton.Disabled = connected;
        }

        if (_readyButton is not null)
        {
            bool hostInLobby = network.IsHost && network.SessionState == LanSessionState.InLobby;
            _readyButton.Disabled = hostInLobby || !connected || network.SessionState == LanSessionState.InMatch;
            _readyButton.Text = hostInLobby ? "房主已就绪" : _localReady ? "取消准备" : "准备迎战";
        }

        if (_startButton is not null)
        {
            _startButton.Text = matchEnded ? "返回候战大厅" : "开始对局";
            _startButton.Disabled = matchEnded
                ? !network.IsHost
                : !network.IsHost || !network.CanStartMatch();
        }

        if (_leaveButton is not null)
        {
            _leaveButton.Disabled = !connected;
        }

        bool canConfigure = network.IsHost && network.SessionState == LanSessionState.InLobby;
        if (_modeOptionButton is not null)
        {
            _modeOptionButton.Disabled = !canConfigure;
            for (int index = 0; index < _modeOptionButton.ItemCount; index++)
            {
                if (_modeOptionButton.GetItemMetadata(index).AsString() == network.SelectedModeId)
                {
                    _modeOptionButton.Select(index);
                    break;
                }
            }
        }
        if (_botDifficultyOptionButton is not null)
        {
            _botDifficultyOptionButton.Disabled = !canConfigure;
        }
        if (_addBotButton is not null)
        {
            _addBotButton.Disabled = !canConfigure;
        }
        if (_removeBotButton is not null)
        {
            _removeBotButton.Disabled = !canConfigure || !network.Players.Values.Any(player => player.IsBot && !player.IsBoss);
        }

        SetStatus(BuildStatusText(network, matchEnded));
    }

    private void HandleSessionStateChanged(LanSessionState state)
    {
        if (state == LanSessionState.InLobby && _autoReadyAfterQuickLaunch && !_localReady)
        {
            SubmitReadyState(true);
        }

        RefreshUI();
    }

    private void HandleLobbyChanged(System.Collections.Generic.IReadOnlyDictionary<int, LanPlayerInfo> players)
    {
        if (_playersLabel is null)
        {
            return;
        }

        StringBuilder builder = new();
        foreach (LanPlayerInfo player in players.Values.OrderBy(player => player.PeerId))
        {
            string hostMark = player.IsHost ? "[color=#D7AD68]房主[/color]" : "[color=#625A4C]座席[/color]";
            string readyMark = player.IsHost || player.IsReady
                ? "[color=#65A487]已就绪[/color]"
                : "[color=#9C8870]待准备[/color]";
            string connectionMark = player.IsConnected
                ? "[color=#BEB099]在线[/color]"
                : "[color=#B13B2C]离线[/color]";
            string character = string.IsNullOrWhiteSpace(player.CharacterId) ? "-" : player.CharacterId;
            builder.AppendLine($"{hostMark}  [font_size=17]{player.PlayerName}[/font_size]");
            builder.AppendLine($"       {connectionMark}  ·  {readyMark}  ·  武将 {character}");
            builder.AppendLine();
        }

        if (builder.Length == 0)
        {
            builder.Append("[color=#8C7D63]等待玩家加入房间……[/color]");
        }

        _playersLabel.Text = builder.ToString();

        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is not null && network.Players.TryGetValue(network.LocalPeerId, out LanPlayerInfo? localPlayer))
        {
            _localReady = localPlayer.IsReady;
        }

        if (_autoStartWhenReady
            && !_quickLaunchStartedMatch
            && network?.IsHost == true
            && network.CanStartMatch())
        {
            _quickLaunchStartedMatch = true;
            network.StartMatch();
        }

        RefreshUI();
    }

    private void HandleNetworkError(string message)
    {
        SetStatus(NetworkTextLocalizer.Localize(message));
    }

    private void HandleNetworkMessage(string message)
    {
        RefreshUI();
    }

    private void ApplyChrome()
    {
        CyberStyle.ApplyRootTheme(this);
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel"), CyberPanelKind.Overlay);
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel/NetworkFrame"), CyberPanelKind.Recessed);
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel/GeneralPortraitFrame"), CyberPanelKind.Card);
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel/GeneralInfoFrame"), CyberPanelKind.Recessed);
        CyberStyle.ApplyPanel(GetNodeOrNull<Control>("Panel/PlayersFrame"), CyberPanelKind.Recessed);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("BrandTitle"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("BrandSubtitle"), color: CyberStyle.MutedText);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/TitleLabel"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/LobbyKickerLabel"), color: CyberStyle.Gold);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/NetworkSectionLabel"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/GeneralSectionLabel"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/PlayersSectionLabel"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/NameFieldLabel"), color: CyberStyle.MutedText);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/AddressFieldLabel"), color: CyberStyle.MutedText);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/GeneralNameLabel"), title: true);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/CharacterDescriptionLabel"), color: CyberStyle.Paper);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/LanAddressLabel"), color: CyberStyle.NeonJade);
        CyberStyle.ApplyLabel(GetNodeOrNull<Label>("Panel/StatusLabel"), color: CyberStyle.Paper);
		GetNodeOrNull<Label>("BrandTitle")?.AddThemeFontSizeOverride("font_size", 46);
		GetNodeOrNull<Label>("Panel/TitleLabel")?.AddThemeFontSizeOverride("font_size", 30);
		GetNodeOrNull<Label>("Panel/NetworkSectionLabel")?.AddThemeFontSizeOverride("font_size", 21);
		GetNodeOrNull<Label>("Panel/GeneralSectionLabel")?.AddThemeFontSizeOverride("font_size", 21);
		GetNodeOrNull<Label>("Panel/PlayersSectionLabel")?.AddThemeFontSizeOverride("font_size", 21);
		GetNodeOrNull<Label>("Panel/GeneralNameLabel")?.AddThemeFontSizeOverride("font_size", 23);
		GetNodeOrNull<Label>("Panel/CharacterDescriptionLabel")?.AddThemeFontSizeOverride("font_size", 13);
        CyberStyle.ApplyLineEdit(_playerNameLineEdit);
        CyberStyle.ApplyLineEdit(_addressLineEdit);
        CyberStyle.ApplyOptionButton(_characterOptionButton);
        CyberStyle.ApplyOptionButton(_modeOptionButton);
        CyberStyle.ApplyOptionButton(_botDifficultyOptionButton);
        CyberStyle.ApplyRichText(_playersLabel);
        CyberStyle.ApplyButton(_hostButton, CyberButtonKind.Primary);
        CyberStyle.ApplyButton(_joinButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_readyButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_startButton, CyberButtonKind.Primary);
        CyberStyle.ApplyButton(_leaveButton, CyberButtonKind.Danger);
        CyberStyle.ApplyButton(_addBotButton, CyberButtonKind.Action);
        CyberStyle.ApplyButton(_removeBotButton, CyberButtonKind.Danger);

        foreach (Button? button in new[] { _hostButton, _joinButton, _readyButton, _startButton, _leaveButton })
        {
            if (button is not null)
            {
                CyberStyle.AttachHoverLift(button, 1.04f);
            }
        }
    }

    private void HandleGeneralSelected(long index)
    {
        ApplySelectedGeneralToField();
        RefreshCharacterDescription();
        SubmitSelectedGeneralIfConnected(resetReady: true);
    }

    private void HandleModeSelected(long index)
    {
        if (_modeOptionButton is null || index < 0 || index >= _modeOptionButton.ItemCount)
        {
            return;
        }
        LanMultiplayerManager.Instance?.SetMode(_modeOptionButton.GetItemMetadata((int)index).AsString());
        RefreshUI();
    }

    private void HandleBotDifficultySelected(long index)
    {
        if (_botDifficultyOptionButton is null || index < 0 || index >= _botDifficultyOptionButton.ItemCount)
        {
            return;
        }
        LanMultiplayerManager.Instance?.SetBotDifficulty((BotDifficulty)_botDifficultyOptionButton.GetItemId((int)index));
    }

    private void AddBot()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        network?.AddBot(GetCharacterId(), network.SelectedBotDifficulty);
    }

    private void RemoveLastBot()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        int botPeerId = network?.Players.Values.Where(player => player.IsBot && !player.IsBoss).Select(player => player.PeerId).LastOrDefault() ?? 0;
        if (botPeerId > 0)
        {
            network?.RemoveBot(botPeerId);
        }
    }

    private void SubmitSelectedGeneralIfConnected(bool resetReady)
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || !network.IsConnected || network.SessionState == LanSessionState.InMatch)
        {
            return;
        }

        network.SelectLocalCharacter(GetCharacterId());
        if (resetReady && _localReady)
        {
            _localReady = false;
            network.SetLocalReady(false);
        }

        RefreshUI();
    }

    private void SubmitReadyState(bool isReady)
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || !network.IsConnected)
        {
            return;
        }

        _localReady = isReady;
        network.SelectLocalCharacter(GetCharacterId());
        network.SetLocalReady(_localReady);
    }

    private void ApplySelectedGeneralToField()
    {
        if (_characterOptionButton is null || _characterIdLineEdit is null || _characterOptionButton.ItemCount == 0)
        {
            return;
        }

        Variant metadata = _characterOptionButton.GetItemMetadata(_characterOptionButton.Selected);
        _characterIdLineEdit.Text = metadata.AsString();
    }

    private void RefreshCharacterDescription()
    {
        if (_characterDescriptionLabel is null)
        {
            return;
        }

        GeneralDefinition definition = GeneralCatalog.Resolve(GetCharacterId(), "未知武将");
        IReadOnlyList<SkillDefinition> skills = SkillRegistry.ResolveMany(definition.SkillIds);
        string skillSummary = skills.Count == 0
            ? "技能尚待配置"
            : string.Join(" / ", skills.Select(skill => skill.DisplayName));
        string effectiveArtFile = GeneralArtRegistry.ResolveFileName(definition.GeneralId, definition.CardFileName);
        string summary = CompactText(definition.Summary, 64);
        _characterDescriptionLabel.Text = $"{CompactText(definition.Title, 24)}\n体力上限  {definition.MaxHealth}\n\n{summary}\n\n技能\n{CompactText(skillSummary, 34)}";
        if (_generalNameLabel is not null)
        {
            _generalNameLabel.Text = definition.DisplayName;
        }

        UpdateGeneralPortrait(definition, effectiveArtFile);
    }

    private void UpdateGeneralPortrait(GeneralDefinition definition, string effectiveArtFile)
    {
        if (_generalPortrait is null)
        {
            return;
        }

        string imagePath = GeneralArtRegistry.ResolveImagePath(definition.GeneralId, effectiveArtFile);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            _generalPortrait.Texture = null;
            return;
        }

        Image image = Image.LoadFromFile(imagePath);
        _generalPortrait.Texture = image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
    }

    private void SetStatus(string message)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = message;
        }
    }

    private void ApplyQuickLaunchFromArgs()
    {
        if (_quickLaunchApplied)
        {
            return;
        }

        _quickLaunchApplied = true;
        Dictionary<string, string> args = ParseUserArgs();
        if (!args.TryGetValue("lan-role", out string? role))
        {
            return;
        }

        if (args.TryGetValue("player-name", out string? playerName) && !string.IsNullOrWhiteSpace(playerName) && _playerNameLineEdit is not null)
        {
            _playerNameLineEdit.Text = playerName.Trim();
        }

        if (args.TryGetValue("general", out string? generalId) && !string.IsNullOrWhiteSpace(generalId))
        {
            SelectGeneralById(generalId.Trim());
        }

        if (args.TryGetValue("port", out string? portText)
            && int.TryParse(portText, out int port)
            && port > 0
            && LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.Port = port;
            RefreshLanAddresses();
        }

        _autoReadyAfterQuickLaunch = IsTruthy(args.GetValueOrDefault("auto-ready"));
        _autoStartWhenReady = IsTruthy(args.GetValueOrDefault("auto-start"));

        if (string.Equals(role, "host", System.StringComparison.OrdinalIgnoreCase))
        {
            HostLobby();
            if (_autoReadyAfterQuickLaunch)
            {
                SubmitReadyState(true);
            }

            return;
        }

        if (!string.Equals(role, "client", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(role, "join", System.StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"Unknown lan-role '{role}'. Use host or client.");
            return;
        }

        string address = args.GetValueOrDefault("address", "127.0.0.1");
        if (_addressLineEdit is not null)
        {
            _addressLineEdit.Text = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        }

        JoinLobby();
    }

    private void SelectGeneralById(string generalId)
    {
        if (_characterIdLineEdit is not null)
        {
            _characterIdLineEdit.Text = generalId;
        }

        if (_characterOptionButton is not null)
        {
            for (int index = 0; index < _characterOptionButton.ItemCount; index++)
            {
                if (string.Equals(_characterOptionButton.GetItemMetadata(index).AsString(), generalId, System.StringComparison.OrdinalIgnoreCase))
                {
                    _characterOptionButton.Select(index);
                    break;
                }
            }
        }

        RefreshCharacterDescription();
    }

    private static Dictionary<string, string> ParseUserArgs()
    {
        Dictionary<string, string> args = new(System.StringComparer.OrdinalIgnoreCase);
        foreach (string rawArg in OS.GetCmdlineUserArgs())
        {
            if (string.IsNullOrWhiteSpace(rawArg) || !rawArg.StartsWith("--", System.StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = rawArg[2..].Split('=', 2);
            args[parts[0]] = parts.Length == 2 ? parts[1] : "true";
        }

        return args;
    }

    private static string ParseAddressAndApplyPort(LanMultiplayerManager network, string address)
    {
        string trimmed = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        int separatorIndex = trimmed.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex != trimmed.IndexOf(':'))
        {
            return trimmed;
        }

        string host = trimmed[..separatorIndex].Trim();
        string portText = trimmed[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portText, out int port) || port <= 0)
        {
            return trimmed;
        }

        network.Port = port;
        return host;
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsefulLanAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address)
            && !address.StartsWith("127.", System.StringComparison.Ordinal)
            && !address.StartsWith("169.254.", System.StringComparison.Ordinal);
    }

    private static string CompactText(string? text, int maxLength)
    {
        string normalized = string.IsNullOrWhiteSpace(text)
            ? "暂无说明"
            : string.Join(" ", text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : $"{normalized[..maxLength]}…";
    }

    private static bool IsMatchEndedInSession(LanMultiplayerManager network)
    {
        return network.SessionState == LanSessionState.InMatch
            && GameManager.Instance is { IsMatchRunning: false }
            && (GameManager.Instance.WinnerPeerId > 0 || !string.IsNullOrWhiteSpace(GameManager.Instance.ResultMessage));
    }

    private static string BuildStatusText(LanMultiplayerManager network, bool matchEnded)
    {
        if (!matchEnded || GameManager.Instance is null)
        {
            return network.SessionState switch
            {
                LanSessionState.Offline => AppendNetworkMessage(network, "尚未连接：创建房间，或输入朋友的局域网地址加入。"),
                LanSessionState.Hosting => AppendNetworkMessage(network, "正在创建局域网房间……"),
                LanSessionState.Joining => AppendNetworkMessage(network, "正在连接房主……"),
                LanSessionState.InLobby => network.IsHost
                    ? network.CanStartMatch()
                        ? AppendNetworkMessage(network, "全员就绪，可以开始对局。")
                        : AppendNetworkMessage(network, $"候战中：{BuildStartWaitReason(network)}。")
                    : AppendNetworkMessage(network, "选择武将并准备，房主将在全员就绪后开始。"),
                LanSessionState.InMatch => AppendNetworkMessage(network, "对局正在进行。"),
                _ => AppendNetworkMessage(network, $"当前状态：{network.SessionState}")
            };
        }

        string result = string.IsNullOrWhiteSpace(GameManager.Instance.ResultMessage)
            ? $"胜者座席：{GameManager.Instance.WinnerPeerId}"
            : BattleLogTextLocalizer.Localize(GameManager.Instance.ResultMessage);
        return network.IsHost
            ? $"对局结束：{result}\n可以返回候战大厅开启下一局。"
            : $"对局结束：{result}\n等待房主返回大厅。";
    }

    private static string AppendNetworkMessage(LanMultiplayerManager network, string status)
    {
        return string.IsNullOrWhiteSpace(network.LastNetworkMessage)
            ? status
            : $"{status}\n网络消息：{NetworkTextLocalizer.Localize(network.LastNetworkMessage)}";
    }

    private static string BuildStartWaitReason(LanMultiplayerManager network)
    {
        List<LanPlayerInfo> connectedPlayers = network.Players.Values
            .Where(player => player.IsConnected)
            .OrderBy(player => player.PeerId)
            .ToList();
        if (connectedPlayers.Count < 2)
        {
            return "至少还需要一名朋友加入";
        }

        List<string> notReadyNames = connectedPlayers
            .Where(player => !player.IsHost && !player.IsReady)
            .Select(player => string.IsNullOrWhiteSpace(player.PlayerName) ? $"座席 {player.PeerId}" : player.PlayerName)
            .ToList();
        return notReadyNames.Count == 0
            ? "等待开始按钮可用"
            : $"等待 {string.Join("、", notReadyNames)} 准备";
    }

    private string GetPlayerName()
    {
        return _playerNameLineEdit?.Text?.Trim() switch
        {
            null or "" => "玩家",
            string value => value
        };
    }

    private string GetCharacterId()
    {
        return _characterIdLineEdit?.Text?.Trim() ?? string.Empty;
    }
}
