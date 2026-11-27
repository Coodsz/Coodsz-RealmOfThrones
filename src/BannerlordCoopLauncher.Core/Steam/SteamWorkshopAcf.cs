using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Core.Steam;

/// <summary>
/// Whether Steam still has work to do on one Bannerlord Workshop item, read per item from
/// steamapps\workshop\appworkshop_261550.acf. NeedsUpdate and NeedsDownload sit on the whole AppWorkshop
/// block, so any other mod's pending update sets them; this looks at the item's own entries only.
/// </summary>
public static class SteamWorkshopAcf
{
    public const string CoopWorkshopId = "3770450698";

    /// <summary>Pending(workshopFolder, CoopWorkshopId) for Bannerlord Coop.</summary>
    public static bool CoopPending(string workshopFolder) => Pending(workshopFolder, CoopWorkshopId);

    /// <summary>
    /// True when the item's download folder (workshop\downloads\261550\id) is there, when its installed
    /// manifest (WorkshopItemsInstalled, else the WorkshopItemDetails one) is not its latest_manifest, or when it is in
    /// WorkshopItemDetails but not in WorkshopItemsInstalled (subscribed, not downloaded yet). False
    /// otherwise, and when the acf is missing or can't be read.
    /// </summary>
    public static bool Pending(string workshopFolder, string itemId)
    {
        try
        {
            if (Directory.Exists(Path.Combine(workshopFolder, "downloads", SteamInstallFinder.BannerlordAppId.ToString(), itemId)))
                return true;
            var acf = Path.Combine(workshopFolder, "appworkshop_" + SteamInstallFinder.BannerlordAppId + ".acf");
            return File.Exists(acf) && PendingIn(File.ReadAllText(acf), itemId);
        }
        catch (Exception ex) when (AntivirusWatch.Harmless(ex))
        {
            return false;
        }
    }

    /// <summary>The acf part of Pending, on the file's text. Only the item's manifest keys are read.</summary>
    public static bool PendingIn(string acfText, string itemId)
    {
        VdfNode root;
        try
        {
            root = Vdf.Parse(acfText);
        }
        catch (VdfException)
        {
            return false;
        }

        if (!string.Equals(root.Key, "AppWorkshop", StringComparison.OrdinalIgnoreCase))
            return false;
        var details = root.Child("WorkshopItemDetails")?.Child(itemId);
        if (details == null || details.Value != null)
            return false;
        if (root.Child("WorkshopItemsInstalled")?.Child(itemId) is not { Value: null } installed)
            return true;
        // The installed manifest is what is on disk (as SteamInstallFinder.ReadInstalledManifests reads it).
        var manifest = installed.Child("manifest")?.Value?.Trim();
        if (string.IsNullOrEmpty(manifest))
            manifest = details.Child("manifest")?.Value?.Trim();
        var latest = details.Child("latest_manifest")?.Value?.Trim();
        return !string.IsNullOrEmpty(manifest) && !string.IsNullOrEmpty(latest) && !string.Equals(manifest, latest, StringComparison.Ordinal);
    }
}
