namespace BannerlordCoopLauncher.Core.Profile;

/// <summary>
/// The small locked note Coop and Friends and Coodsz Host show next to a locked setup, as the
/// TAOM server tool does. Both tools only let an unlocked setup be copied, so the way to change
/// the mods of a locked one is New setup (the same default mods, unlocked).
/// </summary>
public static class LockedSetupNote
{
    public const string Tag = "Locked";

    /// <summary>The line under the setup name when the picked setup is locked.</summary>
    public static string Line(string setupName) =>
        setupName + " is locked. Click New setup to change mods or their order.";

    /// <summary>The tooltip of a button that is off because the setup is locked.</summary>
    public const string ButtonTip = "This setup is locked, so this is off. Click New setup to make a setup you can change.";
}
