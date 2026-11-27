using System.Diagnostics;
using BannerlordCoopLauncher.Core.Steam;

namespace RealmOfThrones.Core;

/// <summary>
/// Steam Workshop pages for the Realm of Thrones tools. On a PC with no app for steam: links
/// Process.Start throws, which from a click handler would close the tool, so a failure is only a
/// log line. The tools check SteamRunning before asking Steam for mods, so nothing is subscribed
/// or opened while Steam is closed.
/// </summary>
public static class RotSteamPage
{
    public const string SteamNotRunning = "Steam is not running. Open Steam and sign in, then try again.";

    public const string CouldNotOpen = "Could not open the Steam Workshop page. Open Steam and sign in, then try again.";

    public static bool SteamRunning() => SteamRegistry.IsRunning();

    /// <summary>Opens each Workshop page in Steam, a short pause apart. False when one could not be
    /// opened; that is logged once and the rest are not tried. start is for the tests.</summary>
    public static async Task<bool> OpenAsync(IEnumerable<string> workshopIds, Action<string> log, Action<ProcessStartInfo>? start = null)
    {
        start ??= info => Process.Start(info);
        var first = true;
        foreach (var id in workshopIds)
        {
            if (!first)
                await Task.Delay(400);
            first = false;
            try
            {
                start(new ProcessStartInfo("steam://url/CommunityFilePage/" + id) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                log(CouldNotOpen);
                return false;
            }
        }

        return true;
    }
}
