using CiyuanSha.GameCore.Choices;

namespace CiyuanSha.Networking;

/// <summary>Protocol-V2 in-match input. No presentation command is accepted.</summary>
public sealed record SubmitChoiceWirePayloadV2(
    ChoiceResult Result,
    string ReconnectToken);
