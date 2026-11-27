namespace BannerlordCoopLauncher.Core.Support;

public static class BlockedDllNotice
{
    public static bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.Contains("0x80131515", StringComparison.OrdinalIgnoreCase)
            || text.Contains("load an assembly from a network location", StringComparison.OrdinalIgnoreCase))
            return true;

        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            "Cannot load:\\s*(?<path>\\S+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        var path = match.Groups["path"].Value;
        return path.Contains(".dll", StringComparison.OrdinalIgnoreCase)
            && (path.Contains('\\') || path.Contains('/'));
    }

    public static IReadOnlyList<string>? Explain(string? text)
    {
        if (!Matches(text))
            return null;

        return
        [
            "What happened: The server cannot load a DLL. Windows blocked that file because it was downloaded. Unblock DLLs in the client launcher only clears the game Modules folder. It does not clear this server folder.",
            "What to do: Click Unblock DLLs in this server tool. Then press Start server again.",
            "What not to do: Do not use the client launcher button for this server. Do not delete the server folder, the mods, or the save."
        ];
    }
}
