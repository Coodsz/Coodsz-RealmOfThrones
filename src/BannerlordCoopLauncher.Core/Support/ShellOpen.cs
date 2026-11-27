using System.ComponentModel;
using System.Diagnostics;

namespace BannerlordCoopLauncher.Core.Support;

// Opens a web page or a steam: link through Windows. A PC with a broken or missing steam: link
// handler (or no default browser) makes Process.Start throw, and most callers sit in click
// handlers, so a throw there would close the tool. Callers log a line instead.
public static class ShellOpen
{
    public const string SteamPageProblem = "Could not open the Steam Workshop page. Open Steam and sign in, then try again.";

    public static string SteamWorkshopPage(string id) => "steam://url/CommunityFilePage/" + id;

    public static bool TryOpen(string target, out string? problem)
    {
        problem = null;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            problem = ex.Message;
            return false;
        }
    }
}
