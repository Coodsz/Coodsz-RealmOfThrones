namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>The one button that fixes a save missing some of its mods, best first.</summary>
public enum SaveModFix
{
    /// <summary>Nothing fixes it in one click (a locked setup, or a tool without that path).</summary>
    None,
    /// <summary>Another saved setup has every mod the save needs: switch to it and start.</summary>
    UseSetup,
    /// <summary>The mods are on the server but turned off in this setup: turn them on and start.</summary>
    TurnOn,
    /// <summary>Some mods are not on the server: open Add mods.</summary>
    AddMods
}

/// <summary>A mod of the card: the friendly name, and whether the server has it at all.</summary>
public sealed record SaveModChip(string Id, string Name, bool Installed)
{
    /// <summary>The hover text: the module id when the name differs, and a note when it is not installed.</summary>
    public string Tip
    {
        get
        {
            var id = Name.Equals(Id, StringComparison.OrdinalIgnoreCase) ? "" : Id;
            var missing = Installed ? "" : "Not on the server yet.";
            return id.Length > 0 && missing.Length > 0 ? id + ". " + missing : id + missing;
        }
    }
}

/// <summary>A saved setup the card can switch to: its name, its save and the mods it loads.</summary>
public sealed record SaveModSetup(string Name, string SaveName, IReadOnlyList<string> Modules);

/// <summary>
/// The card a host sees when a save needs mods the setup leaves out: a short title with the count,
/// the mods as chips with their friendly names, and one main button that fixes it. Used by Coodsz
/// Host (Dashboard and Saves page, AD1259 too) and the TAOM server tool, so they read the same.
/// </summary>
public sealed record SaveModCard(string SaveName, IReadOnlyList<SaveModChip> Mods, SaveModFix Fix, string SetupName, bool Locked)
{
    /// <summary>How many chips show before the +N more toggle.</summary>
    public const int ChipLimit = 8;

    public const string OtherSaveButton = "Use a different save";
    public const string OtherSaveTip = "Opens the save list, so you can pick a save made with this setup.";
    public const string DismissButton = "Dismiss";
    public const string DismissTip = "Hides this card. Start server checks the save again.";
    public const string StartAnywayButton = "Start anyway";
    public const string StartAnywayTip = "Keeps a copy of the save first. Only do this if you are sure.";
    public const string LessButton = "Show less";
    public const string StatusLine = "The server was not started. This save needs mods that are turned off.";

    /// <summary>The name from SubModule.xml (or the tool's mod list), or the id when there is none.</summary>
    public static string FriendlyName(string id, Func<string, string?> nameOf) =>
        nameOf(id) is { } name && name.Trim().Length > 0 ? name.Trim() : id;

    /// <summary>
    /// Builds the card for a refused save. <paramref name="installed"/> says whether the server has the
    /// mod at all; <paramref name="canTurnOn"/> and <paramref name="canAddMods"/> say whether this tool
    /// and setup can do that (a locked setup cannot). A setup is offered when it was made for this save
    /// and loads its mods, or when it loads exactly the mods the save needs.
    /// </summary>
    public static SaveModCard Build(string saveName, IReadOnlyList<string> saveModules, SaveModChange change,
        Func<string, string?> nameOf, Func<string, bool> installed, IEnumerable<SaveModSetup> otherSetups,
        bool canTurnOn, bool canAddMods, bool locked = false)
    {
        // The mods the server does not have come first, so they are never hidden under +N more.
        var chips = change.Removed
            .Select(id => new SaveModChip(id, FriendlyName(id, nameOf), installed(id)))
            .OrderBy(chip => chip.Installed)
            .ToList();
        var setup = MatchingSetup(saveName, saveModules, otherSetups);
        var fix = setup != null ? SaveModFix.UseSetup
            : chips.All(chip => chip.Installed) ? canTurnOn ? SaveModFix.TurnOn : SaveModFix.None
            : canAddMods ? SaveModFix.AddMods : SaveModFix.None;
        return new SaveModCard(saveName, chips, fix, setup?.Name ?? "", locked);
    }

    /// <summary>
    /// The setup made for this save that loads its mods, else one that loads exactly them: first one
    /// with no save of its own, then one made for another save (switching to it picks this save again).
    /// </summary>
    public static SaveModSetup? MatchingSetup(string saveName, IReadOnlyList<string> saveModules, IEnumerable<SaveModSetup> setups)
    {
        var list = setups.ToList();
        var exact = list.Where(setup => SaveModChange.Check(saveModules, setup.Modules).Action == SaveModAction.Start).ToList();
        return list.FirstOrDefault(setup => saveName.Length > 0 && setup.SaveName.Equals(saveName, StringComparison.OrdinalIgnoreCase)
                                            && SaveModChange.Check(saveModules, setup.Modules).Action != SaveModAction.Refuse)
               ?? exact.FirstOrDefault(setup => setup.SaveName.Trim().Length == 0)
               ?? exact.FirstOrDefault();
    }

    /// <summary>
    /// After Use the setup this save was made with switched setups: null when the server may start,
    /// else the short line that says what to do. It starts only when the setup really is the card's,
    /// the save box shows the card's save and the setup is not set to make a new campaign.
    /// </summary>
    public static string? UseSetupBlock(string cardSave, string cardSetup, string currentSetup, string? selectedSave, bool newCampaign)
    {
        if (!currentSetup.Equals(cardSetup, StringComparison.Ordinal))
            return "Could not switch to " + cardSetup + ". Pick it in the setup list, then click Start server.";
        if (!string.Equals(selectedSave, cardSave, StringComparison.OrdinalIgnoreCase))
            return "Switched to " + cardSetup + ". Pick " + cardSave + " in the save list, then click Start server.";
        if (newCampaign)
            return cardSetup + " is set to start a new campaign, so the server was not started. Pick " + cardSave + " in the save list, then click Start server.";
        return null;
    }

    public int Count => Mods.Count;

    public int MissingCount => Mods.Count(chip => !chip.Installed);

    public string Title => Count == 1
        ? "This save needs a mod that is turned off"
        : "This save needs " + Count + " mods that are turned off";

    public string Why => Count == 1
        ? "Starting without it can break the campaign."
        : "Starting without them can break the campaign.";

    /// <summary>A plain line about mods the server does not have, or a locked setup; empty when neither.</summary>
    public string Note => MissingCount == 0
        ? Locked && Fix == SaveModFix.None ? "This setup is locked, so its mods stay as they are. Copy setup makes one you can change." : ""
        : Count == 1 ? "This mod is not on the server yet."
        : MissingCount == 1 ? "One of these is not on the server yet."
        : MissingCount == Count ? "None of these are on the server yet."
        : MissingCount + " of these are not on the server yet.";

    /// <summary>The main button's text; empty when there is none.</summary>
    public string FixButton => Fix switch
    {
        SaveModFix.UseSetup => "Use the setup this save was made with",
        SaveModFix.TurnOn => Count == 1 ? "Turn this mod back on" : "Turn these mods back on",
        SaveModFix.AddMods => MissingCount == 1 ? "Add the missing mod" : "Add the missing mods",
        _ => ""
    };

    public string FixTip => Fix switch
    {
        SaveModFix.UseSetup => "Switches to " + SetupName + ", which has every mod this save needs, then starts the server.",
        SaveModFix.TurnOn => "Turns them on in this setup, saves it and starts the server.",
        SaveModFix.AddMods => "Opens Add mods, which copies mods from Steam Workshop or the game Modules folder into the server.",
        _ => ""
    };

    /// <summary>The chips that show: the first <see cref="ChipLimit"/>, or all of them when expanded.</summary>
    public IReadOnlyList<SaveModChip> Shown(bool expanded) =>
        expanded || Mods.Count <= ChipLimit ? Mods : Mods.Take(ChipLimit).ToList();

    /// <summary>Whether the card needs the +N more toggle at all.</summary>
    public bool HasMore => Mods.Count > ChipLimit;

    /// <summary>How many chips +N more hides (0 when expanded or when they all fit).</summary>
    public int MoreCount(bool expanded) => expanded ? 0 : Math.Max(0, Mods.Count - ChipLimit);

    public string MoreButton(bool expanded) => expanded ? LessButton : "+" + MoreCount(false) + " more";

    public string MoreTip(bool expanded) => expanded ? "Shows only the first mods again." : "Shows every mod this save needs.";
}
