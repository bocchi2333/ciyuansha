using System;
using System.Collections.Generic;
using System.Linq;
using CiyuanSha.GameCore.Choices;
using CiyuanSha.GameCore.Domain;
using CiyuanSha.Gameplay.Core;
using CiyuanSha.Networking;
using Godot;

namespace CiyuanSha.UI;

/// <summary>One renderer for every protocol-V2 choice kind.</summary>
public partial class GenericChoicePanel : Control
{
    [Export]
    public bool PreviewMode { get; set; }

    private Panel? _panel;
    private Label? _prompt;
    private GridContainer? _options;
    private Button? _confirm;
    private Button? _cancel;
    private ChoiceRequest? _request;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        Name = "GenericChoicePanelV2";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorLeft = 0.20f;
        AnchorRight = 0.80f;
        AnchorTop = 0.68f;
        AnchorBottom = 0.98f;
        ZIndex = 80;
        BuildUi();
        if (PreviewMode)
        {
            SetRequest(BuildPreviewRequest());
        }
        else
        {
            Bind();
            Refresh();
        }
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnStateChanged -= Refresh;
            GameManager.Instance.OnCoreEngineAdvanced -= HandleCoreAdvanced;
        }
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged -= HandleSessionChanged;
            LanMultiplayerManager.Instance.OnLobbyChanged -= HandleLobbyChanged;
        }
    }

    private void BuildUi()
    {
        _panel = new Panel { MouseFilter = MouseFilterEnum.Stop };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        CyberStyle.ApplyPanel(_panel, CyberPanelKind.Strong);
        AddChild(_panel);

        VBoxContainer box = new();
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 16;
        box.OffsetTop = 12;
        box.OffsetRight = -16;
        box.OffsetBottom = -12;
        box.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(box);

        _prompt = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        CyberStyle.ApplyLabel(_prompt, title: true);
        box.AddChild(_prompt);

        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(scroll);
        _options = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _options.AddThemeConstantOverride("h_separation", 6);
        _options.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_options);

        HBoxContainer controls = new() { Alignment = BoxContainer.AlignmentMode.Center };
        controls.AddThemeConstantOverride("separation", 10);
        box.AddChild(controls);
        _confirm = CreateButton("确认");
        _confirm.Pressed += Submit;
        controls.AddChild(_confirm);
        _cancel = CreateButton("取消");
        _cancel.Pressed += Cancel;
        controls.AddChild(_cancel);
    }

    private void Bind()
    {
        if (GameManager.Instance is not null)
        {
            GameManager.Instance.OnStateChanged += Refresh;
            GameManager.Instance.OnCoreEngineAdvanced += HandleCoreAdvanced;
        }
        if (LanMultiplayerManager.Instance is not null)
        {
            LanMultiplayerManager.Instance.OnSessionStateChanged += HandleSessionChanged;
            LanMultiplayerManager.Instance.OnLobbyChanged += HandleLobbyChanged;
        }
    }

    private void Refresh()
    {
        LanMultiplayerManager? network = LanMultiplayerManager.Instance;
        if (network is null || network.SessionState != LanSessionState.InMatch || network.JoinAsSpectator)
        {
            SetRequest(null);
            return;
        }

        int localSeatId = network.Players.TryGetValue(network.LocalPeerId, out LanPlayerInfo? player)
            ? player.SeatId
            : network.LocalPeerId;
        ChoiceRequest? request;
        if (network.IsHost && GameManager.Instance?.CoreRuntime?.Engine is not null)
        {
            request = GameManager.Instance.CoreRuntime.BuildView(ViewerContext.ForPlayer(localSeatId)).PendingChoice;
        }
        else
        {
            request = network.LastMatchStateSnapshot?.ActiveChoice;
        }
        request = request?.ActingSeatId == localSeatId ? request : null;
        if (request?.Kind is ChoiceKind.SelectTarget or ChoiceKind.UseOrRespond)
        {
            request = null;
        }
        SetRequest(request);
    }

    private void SetRequest(ChoiceRequest? request)
    {
        if (request is null)
        {
            _request = null;
            _selected.Clear();
            Visible = false;
            return;
        }
        if (_request?.RequestId == request.RequestId && _request.StateRevision == request.StateRevision)
        {
            Visible = true;
            return;
        }

        _request = request;
        _selected.Clear();
        Visible = true;
        if (_prompt is not null)
        {
            _prompt.Text = $"{FormatPrompt(request.PromptKey)} · 选择 {request.MinimumSelections}–{request.MaximumSelections} 项";
        }
        ClearChildren(_options);
        if (_options is not null)
        {
            _options.Columns = request.Options.Count <= 3 ? Math.Max(1, request.Options.Count) : 4;
            foreach (ChoiceOption option in request.Options)
            {
                Button button = CreateButton(FormatOptionLabel(option));
                button.Disabled = !option.IsEnabled;
                button.TooltipText = option.IsEnabled ? option.EntityId : option.DisabledReasonKey;
                button.ToggleMode = request.MaximumSelections > 1;
                string optionId = option.OptionId;
                button.Pressed += () => SelectOption(optionId, button);
                _options.AddChild(button);
            }
        }
        if (_confirm is not null)
        {
            _confirm.Visible = request.MaximumSelections > 1 || request.MinimumSelections == 0;
            UpdateConfirmState();
        }
        if (_cancel is not null)
        {
            _cancel.Visible = request.AllowCancel;
        }
    }

    private void SelectOption(string optionId, Button button)
    {
        if (_request is null)
        {
            return;
        }
        if (_request.MaximumSelections == 1)
        {
            _selected.Clear();
            _selected.Add(optionId);
            Submit();
            return;
        }
        if (!_selected.Add(optionId))
        {
            _selected.Remove(optionId);
        }
        else if (_selected.Count > _request.MaximumSelections)
        {
            _selected.Remove(optionId);
            button.ButtonPressed = false;
        }
        UpdateConfirmState();
    }

    private void Submit()
    {
        if (_request is null
            || _selected.Count < _request.MinimumSelections
            || _selected.Count > _request.MaximumSelections)
        {
            return;
        }
        ChoiceResult result = new(
            _request.RequestId,
            _request.StateRevision,
            _selected.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        LanMultiplayerManager.Instance?.SubmitChoice(result);
    }

    private void Cancel()
    {
        if (_request?.AllowCancel != true)
        {
            return;
        }
        LanMultiplayerManager.Instance?.SubmitChoice(ChoiceResult.Cancel(_request.RequestId, _request.StateRevision));
    }

    private void UpdateConfirmState()
    {
        if (_confirm is not null && _request is not null)
        {
            _confirm.Disabled = _selected.Count < _request.MinimumSelections || _selected.Count > _request.MaximumSelections;
        }
    }

    private static Button CreateButton(string text)
    {
        Button button = new() { Text = text, CustomMinimumSize = new Vector2(112, 38) };
        CyberStyle.ApplyButton(button, CyberButtonKind.Primary);
        return button;
    }

    private static string FormatPrompt(string promptKey) => promptKey switch
    {
        "phase.play.choose_action" => "请选择出牌阶段操作",
        "card.choose_target" => "请选择目标",
        "phase.discard.choose" => "请选择要弃置的牌",
        "response.dodge" => "请使用【闪】或放弃响应",
        "response.nullification" => "请使用【无懈可击】或放弃响应",
        "skill.choose_cost" => "请选择技能费用",
        "skill.choose_target" => "请选择技能目标",
        _ => promptKey
    };

    private static string FormatOptionLabel(ChoiceOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.LabelKey) && !option.LabelKey.Contains('.', StringComparison.Ordinal))
        {
            return option.LabelKey;
        }
        return option.LabelKey switch
        {
            "action.end_play" => "结束出牌",
            "action.recast" => "重铸",
            "target.player" => $"角色 {option.EntityId}",
            "response.decline" => "放弃响应",
            "damage.physical" => "普通伤害",
            "damage.fire" => "火焰伤害",
            "phase.discard.card" => $"弃牌 {option.EntityId}",
            _ => string.IsNullOrWhiteSpace(option.EntityId) ? option.LabelKey : option.EntityId
        };
    }

    private static ChoiceRequest BuildPreviewRequest() => new(
        "preview-choice",
        1,
        1,
        ChoiceKind.SelectAction,
        "phase.play.choose_action",
        new[]
        {
            new ChoiceOption("action:use:slash", ChoiceOptionKind.Card, "杀", "card-0042"),
            new ChoiceOption("action:use:fire_attack", ChoiceOptionKind.Card, "火攻", "card-0088"),
            new ChoiceOption("action:skill:limit_break", ChoiceOptionKind.Skill, "破界", "limit_break"),
            new ChoiceOption("action:recast:iron_chain", ChoiceOptionKind.Action, "action.recast", "card-0101"),
            new ChoiceOption("action:end_play", ChoiceOptionKind.Action, "action.end_play")
        },
        MinimumSelections: 1,
        MaximumSelections: 2,
        AllowCancel: true);

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

    private void HandleCoreAdvanced(CiyuanSha.GameCore.Engine.EngineStepResult _) => Refresh();

    private void HandleSessionChanged(LanSessionState _) => Refresh();

    private void HandleLobbyChanged(IReadOnlyDictionary<int, LanPlayerInfo> _) => Refresh();
}
