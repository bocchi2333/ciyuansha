using CiyuanSha.GameCore.Choices;

namespace CiyuanSha.Networking;

/// <summary>
/// Protocol-V2 SubmitChoice payload. PresentationCommand exists only while the
/// existing Godot controls are migrated to emit ChoiceResult directly; it is
/// validated by the authoritative host and is never trusted as state.
/// </summary>
public sealed record SubmitChoiceWirePayloadV2(
    ChoiceResult? Result,
    string ReconnectToken,
    NetworkPlayCommand? PresentationCommand = null);
