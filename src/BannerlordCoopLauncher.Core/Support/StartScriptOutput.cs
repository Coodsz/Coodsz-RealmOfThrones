using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The lines of a PowerShell error record that Start-ModdedServer.ps1 prints when it fails itself: the message
/// ("Exception calling ..."), CategoryInfo, FullyQualifiedErrorId, the At line naming the script and the "+ ~"
/// underline. The server tools' start failure explanations skip them, so the script's own error never blames a
/// mod named near it (Coop #55 blamed No Companion Limit for a blocked file write).
/// </summary>
public static class StartScriptOutput
{
    private static readonly Regex Record = new(
        @"FullyQualifiedErrorId|CategoryInfo|Exception calling|^\s*At\s.+\.ps1:\d+|^\s*\+\s*~",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsPowerShellRecord(string line) => Record.IsMatch(line);
}
