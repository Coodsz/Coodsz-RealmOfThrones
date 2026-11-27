namespace BannerlordCoopLauncher.Core.Modules;

public enum SaveModAction
{
    /// <summary>The save remembers the same mods (or none), so it starts as it is.</summary>
    Start,
    /// <summary>The server only adds mods. Bannerlord loads that, so the host is asked once.</summary>
    AskToAdd,
    /// <summary>A mod the save was made with is missing. Removing a mod can break the campaign.</summary>
    Refuse
}

/// <summary>
/// What starting a campaign with a different mod list means. Bannerlord loads a save with mods
/// added (they start fresh in that campaign); a save missing a mod it was made with is the
/// dangerous case. Uses the same rules as Start-ModdedServer.ps1: the official modules and
/// DedicatedServer.* are left out, and so are the Coodsz modules that store nothing in a save.
/// </summary>
public sealed record SaveModChange(SaveModAction Action, IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    // Start-ModdedServer.ps1 $nonSaveModuleIds: they only patch code and keep nothing in a save.
    // Harmony and UIExtenderEx are code libraries with no save types or campaign behaviors.
    // ButterLib (ButterLibSaveableTypeDefiner) and MCM (PerSaveCampaignBehavior) do store data, so they stay.
    // ServerSettingsLock left the setup in v1.3.4; it only patches MCM values, so saves made with it still start.
    public static readonly IReadOnlyCollection<string> NoSaveDataModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Diplomacy.CoopCompat", "StuckBattleFix", "Limitless.CoopCompat", "Bannerlord.Harmony", "Bannerlord.UIExtenderEx", "ServerSettingsLock"
    };

    public const string AskTitle = "Add mods to this campaign";
    public const string AskButton = "Add mods";
    public const string AskButtonTip = "Makes a backup of the save, then starts it with these mods added.";
    public const string AskBackupNote = "A backup of the save is made first.";

    public static SaveModChange Check(IReadOnlyList<string> saveModules, IEnumerable<string> server)
    {
        if (saveModules.Count == 0)
            return new SaveModChange(SaveModAction.Start, Array.Empty<string>(), Array.Empty<string>());
        var save = SaveCompared(saveModules);
        var loaded = SaveCompared(server);
        var removed = save.Where(id => !loaded.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var added = loaded.Where(id => !save.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var action = removed.Count > 0 ? SaveModAction.Refuse
            : added.Count > 0 ? SaveModAction.AskToAdd
            : SaveModAction.Start;
        return new SaveModChange(action, added, removed);
    }

    /// <summary>The one line question above the list of added mods.</summary>
    public string AskQuestion => Added.Count == 1
        ? "This campaign was made without this mod. Add it?"
        : "This campaign was made without these mods. Add them?";

    /// <summary>The added mods as (name, id) rows; the name comes from the tool's module list (SubModule.xml).</summary>
    public IReadOnlyList<(string Name, string Id)> AddedRows(Func<string, string?> nameOf) =>
        Added.Select(id => (SaveModCard.FriendlyName(id, nameOf), id)).ToList();

    public string AskWarning => Added.Count == 1
        ? "Removing it later can break the save."
        : "Removing them later can break the save.";

    /// <summary>The host said No to adding the mods.</summary>
    public string DeclinedText =>
        "The server was not started. To play this campaign as it was, turn off " + string.Join(", ", Added) + ". Or make a new campaign save.";

    /// <summary>
    /// The same for the status box, in three lines instead of one long run of mod ids: what happened,
    /// the mods by name, and what to do.
    /// </summary>
    public IReadOnlyList<string> DeclinedLines(Func<string, string?> nameOf) =>
    [
        Added.Count == 1
            ? "The server was not started. This campaign was made without this mod:"
            : "The server was not started. This campaign was made without these " + Added.Count + " mods:",
        string.Join("   ·   ", AddedRows(nameOf).Select(row => row.Name)),
        Added.Count == 1
            ? "To play it as it was, turn it off on the Mods page, or make a new campaign save."
            : "To play it as it was, turn them off on the Mods page, or make a new campaign save."
    ];

    /// <summary>
    /// True when the mods to add include the setup's campaign map mod. A campaign made on another map has
    /// none of that map's settlements and kingdoms, so it never loads on it (Tatache's vanilla save with
    /// AD1259 added, AD1259 crash report #9).
    /// </summary>
    public bool AddsMap(string? campaignMap) =>
        Action == SaveModAction.AskToAdd && !string.IsNullOrWhiteSpace(campaignMap)
        && Added.Contains(campaignMap.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Why a campaign made on another map is not started with a map mod added. English, like the other
    /// refusals: the logs keep it that way and the window translates it when it shows it.
    /// </summary>
    public static string MapRefuseText(string mapName) =>
        "The server was not started. This campaign was made on another map, without " + mapName
        + ". A map mod can't be added to a campaign that already started, the save would not load. Make a new campaign save to play on " + mapName + ".";

    /// <summary>Why a save missing one of its mods is not started, with every id, for the logs (the windows show <see cref="SaveModCard"/>).</summary>
    public string RefuseText =>
        "This campaign was made with " + string.Join(", ", Removed) + ", and this setup does not load them. Removing mods from a running campaign can break it. Turn them back on, pick the setup the save was made with, or make a new campaign save.";

    private static List<string> SaveCompared(IEnumerable<string> modules) =>
        CoopJoinRules.Compared(modules).Where(id => !NoSaveDataModules.Contains(id)).ToList();
}
