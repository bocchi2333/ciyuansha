using System;
using System.Text.RegularExpressions;

namespace CiyuanSha.UI;

internal static class NetworkTextLocalizer
{
    public static string Localize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "未知网络错误。";
        }

        if (message.Equals("Connected to LAN host.", StringComparison.Ordinal))
        {
            return "已连接到局域网房主。";
        }

        if (message.Equals("Failed to connect to the LAN host.", StringComparison.Ordinal))
        {
            return "无法连接到局域网房主。";
        }

        if (message.Equals("Disconnected from the LAN host.", StringComparison.Ordinal))
        {
            return "与局域网房主的连接已中断。";
        }

        if (message.Equals("Cannot start match until every player is ready.", StringComparison.Ordinal))
        {
            return "仍有玩家尚未准备，暂时无法开始对局。";
        }

        Match joinFailure = Regex.Match(
            message,
            "^Failed to join LAN session at (?<address>.+?):(?<port>\\d+): (?<error>.+)\\.$",
            RegexOptions.CultureInvariant);
        if (joinFailure.Success)
        {
            return $"连接 {joinFailure.Groups["address"].Value}:{joinFailure.Groups["port"].Value} 失败（{joinFailure.Groups["error"].Value}）。";
        }

        return message;
    }
}
