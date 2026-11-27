namespace BannerlordCoopLauncher.Core.Profile;

// Diplomacy's Send messenger writes Hero.Gold on the client, which Coop blocks, then opens a
// town mission that fails and leaves a client only settlement behind; the game dies soon after.
// Diplomacy.CoopCompat turns messengers off in co op. Without it the tools only warn: nothing is
// turned off or added.
public static class DiplomacyCompatRule
{
    public const string Diplomacy = "Bannerlord.Diplomacy";
    public const string CoopCompat = "Diplomacy.CoopCompat";
    public const string CoopCompatWorkshopId = "3824313421";
    public const string CoopCompatUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + CoopCompatWorkshopId;

    public const string Warning = "Diplomacy needs Diplomacy Coop Compat in co op. Without it messengers and some buttons break the game.";

    public static bool MissingCompat(IEnumerable<string> enabledModuleIds)
    {
        var ids = enabledModuleIds as ICollection<string> ?? enabledModuleIds.ToList();
        return ids.Contains(Diplomacy, StringComparer.OrdinalIgnoreCase)
            && !ids.Contains(CoopCompat, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The warning when Diplomacy is on without Diplomacy.CoopCompat, else null.</summary>
    public static string? WarningFor(IEnumerable<string> enabledModuleIds) =>
        MissingCompat(enabledModuleIds) ? Warning : null;
}
