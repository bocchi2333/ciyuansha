namespace CiyuanSha.Networking;

/// <summary>
/// 局域网联机会话状态。
/// </summary>
public enum LanSessionState
{
    Offline = 0,
    Hosting = 1,
    Joining = 2,
    InLobby = 3,
    InMatch = 4
}
