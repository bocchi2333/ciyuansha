namespace CiyuanSha.Networking;

/// <summary>
/// Supported play commands sent from clients to the host.
/// </summary>
public enum NetworkPlayCommandType
{
    None = 0,
    BasicAttack = 1,
    EndPlayPhase = 2,
    UsePeach = 3,
    UseEquipment = 4,
    RespondDodge = 5,
    DeclineResponse = 6,
    RespondPeach = 7,
    UseCard = 8,
    RespondSlash = 9,
    RespondNullification = 10,
    DiscardCard = 11,
    ChooseHarvestCard = 12,
    ChooseTargetCard = 13,
    RecastCard = 14,
    ActivateSkill = 15,
    ChooseHandCard = 16,
    DeclineHandCardSelection = 17,
    ChooseGeneral = 18,
    ReadyState = 19,
    ReconnectRequest = 20,

    // V2 is the only command transported during a match. Values 1-17 remain
    // local presentation-adapter actions and are never accepted as wire input.
    SubmitChoice = 100,
    JoinSpectator = 101
}
