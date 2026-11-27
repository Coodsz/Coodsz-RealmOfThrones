namespace BannerlordCoopLauncher.Core.Steam;

// DLLs another mod or an old install left in the game's own bin folder. Bannerlord comes without them
// (the Steam depot lists no Common.dll or 0Harmony.dll there), and the game loads a DLL from its own
// folder before a mod's copy: another Common.dll hides Coop's ("Could not load type
// 'Common.Network.Session.ServerVisibility' from assembly 'Common, Version=3.0.0.0'") and an old
// 0Harmony.dll hides the Harmony mod's ("Method not found ... HarmonyLib.Harmony.PatchCategory(").
// Like NonSteamCopy this only warns in the launcher log before the game starts; it never blocks.
public static class GameBinLeftovers
{
    public static readonly string[] Names = { "Common.dll", "0Harmony.dll" };

    // The leftover DLLs in this game's bin folder, by name. Empty for a clean install or no folder.
    public static IReadOnlyList<string> Find(string? gameRoot)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return found;
        var bin = GameRoot.WorkingDirectory(gameRoot);
        foreach (var name in Names)
            if (File.Exists(Path.Combine(bin, name)))
                found.Add(name);
        return found;
    }

    // One warning per leftover DLL: a short opening line, the step, the file's path on its own line, then
    // the cause and what to do next.
    // Never throws: a game path Windows can't read (bad characters, too long, no access) gives no warning,
    // so this check can never stop Play or Connect.
    public static IReadOnlyList<string> Notes(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return Array.Empty<string>();
        try
        {
            return Notes(Find(gameRoot), GameRoot.WorkingDirectory(gameRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> Notes(IReadOnlyList<string> found, string bin) =>
        found.Select(name => Problem(name) + "\n" + MoveStep + "\n" + Path.Combine(bin, name) + "\n" + Cause).ToList();

    // Each line on its own, so the log box translates it line by line.
    public static string Problem(string name) => "The game's bin folder has a " + name + " that Bannerlord doesn't come with.";

    public const string MoveStep = "Close the game and move this file out to your Desktop.";

    public const string Cause = "Another mod left it there, and the game loads it before the copy Coop needs, so Coop can fail to load. Start the game again once it's moved.";
}
