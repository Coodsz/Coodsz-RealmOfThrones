namespace BannerlordCoopLauncher.Core.Profile;

/// <summary>
/// COOP Server Settings Lock (S1MA$H) left the Coop and Friends setup in v1.3.4. It is gone from the Workshop,
/// Nexus Mods and ModDB, nothing needs it, and Coop checks mods both ways, so a host that still runs it turns
/// away every player without it. A setup that still has it on gets this note in the Server Tool and in the
/// client, once each time the tool starts, until it is turned off. It keeps nothing in a save, so a campaign saved with it still starts (SaveModChange).
/// </summary>
public static class RetiredSettingsLock
{
    public const string ModuleId = "ServerSettingsLock";

    /// <summary>The note in the Coop Server Tool.</summary>
    public const string HostNote = "COOP Server Settings Lock is no longer part of the setup. Turn it off here, and tell every player to turn it off too, since the host and the players must match. Saves made with it still start without it.";

    /// <summary>The note in the Coop client.</summary>
    public const string PlayerNote = "COOP Server Settings Lock is no longer part of the setup. Turn it off here, and ask the host to turn it off too, since the host and the players must match.";

    /// <summary>
    /// True when the note is due for this setup: it still turns the mod on and has not had the note since the
    /// tool started (<paramref name="notedThisRun"/> holds the setup names that had it). A setup that turned it
    /// off leaves that list, so turning it on again brings the note back. Locked setups never list the mod any
    /// more, so only a setup the player or host made can still have it.
    /// </summary>
    public static bool ShowNote(string setupName, bool locked, IEnumerable<string> enabledModuleIds, ISet<string> notedThisRun)
    {
        if (locked || !enabledModuleIds.Contains(ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            notedThisRun.Remove(setupName);
            return false;
        }
        return notedThisRun.Add(setupName);
    }
}
