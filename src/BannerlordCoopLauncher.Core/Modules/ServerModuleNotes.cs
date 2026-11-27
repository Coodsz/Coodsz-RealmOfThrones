using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Core.Modules;

public static class ServerModuleNotes
{
    public const string Libraries =
        "Harmony, ButterLib, UIExtenderEx, and Mod Configuration Menu stay on the list because other mods need them. Their menus do not run on the dedicated server.";

    public const string NewCampaign =
        "This new campaign is created with the mods in this setup. default_new_game is only the empty template. Friends join the named save.";

    public static bool IsLibrary(string? moduleId) =>
        moduleId is "Bannerlord.Harmony" or "Bannerlord.ButterLib" or "Bannerlord.UIExtenderEx" or "Bannerlord.MBOptionScreen";

    public static bool KeepsClientCampaign(string? moduleId) =>
        moduleId is "StoryMode" or "CustomBattle";

    // Coop's Server Instructions.md (0.1.6): Coop refuses anyone who has War Sails, Birth and
    // Aging Options or Fast Mode turned on (the game marks them as optional content, and
    // Coop's ModuleValidator says "DLC is not supported"). They stay official modules in the
    // join comparison (CoopJoinRules) and soft dependencies, so a mod that names one, such as
    // TAOM naming BirthAndDeath, still loads without it. These ids are only told to be turned
    // off; nothing here turns them off or blocks a start. Newer Coop runs War Sails on a server
    // set up for it ("warSails" in mod-config.json plus NavalDLC in the module list), and then a
    // player needs it on. These tools can't host that yet, so War Sails stays on this list and
    // the server tools refuse a War Sails start (Server.WarSailsCheck), except the Coop and
    // Friends Server Tool on a Coop nightly server (WarSailsCheck.NightlyServer). This list only
    // feeds the community report filter (CommunityCompat), so no player is told here that War
    // Sails can't be joined.
    public static readonly IReadOnlyList<string> CoopRefused = ["NavalDLC", "BirthAndDeath", "FastMode"];

    public static bool IsCoopRefused(string? moduleId) =>
        moduleId != null && CoopRefused.Contains(moduleId, StringComparer.OrdinalIgnoreCase);

    public static string? SkippedCode(string? displayName, string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || !SkipsDedicatedServer(xml))
            return null;
        // Coop marks its client half as not for the server, but the server runs Coop itself, so it gets no such line.
        if (Parsing.SubModuleXml.TryRead(xml, out var info, out _) && BannerlordCoopModules.IsEither(info?.Id))
            return null;
        var name = string.IsNullOrWhiteSpace(displayName) ? "That mod" : displayName.Trim();
        return name + " stays in the load order and in the save, but the dedicated server does not run its code. Players can keep it. The server world will not use what that code does.";
    }

    /// <summary>
    /// <see cref="SkippedCode"/> for the Start notes of one module. COOP Server Settings Lock gets none: it left the
    /// setup, and RetiredSettingsLock's note tells the host and every player to turn it off, not to keep it.
    /// </summary>
    public static string? SkippedCodeNote(string? moduleId, string? displayName, string? xml) =>
        string.Equals(moduleId, Profile.RetiredSettingsLock.ModuleId, StringComparison.OrdinalIgnoreCase)
            ? null
            : SkippedCode(displayName, xml);

    public static bool SkipsDedicatedServer(string xml) =>
        Regex.IsMatch(xml, "DedicatedServerType\"\\s+value\\s*=\\s*\"none\"", RegexOptions.IgnoreCase)
        || Regex.IsMatch(xml, "DedicatedServerType\\s*=\\s*\"none\"", RegexOptions.IgnoreCase);
}
