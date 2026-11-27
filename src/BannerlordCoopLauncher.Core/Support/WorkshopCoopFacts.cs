using System.Text;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// host/workshop-coop.txt in every server tool's Send my logs pack: what the tool sees of the Workshop Coop
/// (item 3770450698) in each Steam library it searches. Two packs could not tell a download still running
/// from a server program antivirus took (CR-88BP7, CR-P75R3). Only yes and no and the library paths, no
/// Steam ids. LogZipBuilder masks the Windows user name in the paths when it writes the pack.
/// </summary>
public static class WorkshopCoopFacts
{
    public const string Entry = "host/workshop-coop.txt";

    /// <summary>The file for a log pack: the given libraries, else the ones find returns. Finding Steam
    /// or reading a library never fails the pack; the file then says why it is short.</summary>
    public static string ForPack(IReadOnlyList<string>? steamLibraries, Func<IReadOnlyList<string>> find)
    {
        try
        {
            return Text(steamLibraries ?? find());
        }
        catch (Exception ex) when (AntivirusWatch.Harmless(ex))
        {
            var text = new StringBuilder();
            FactLine(text, "Bannerlord Coop on the Steam Workshop could not be checked: " + ex.GetType().Name);
            return text.ToString();
        }
    }

    public static string Text(IReadOnlyList<string> steamLibraries)
    {
        var text = new StringBuilder();
        FactLine(text, "Bannerlord Coop on the Steam Workshop (item " + AntivirusFiles.CoopWorkshopId + "), checked when the logs were packed.");
        FactLine(text, "Steam libraries searched: " + steamLibraries.Count + (steamLibraries.Count == 0 ? " (Steam was not found)" : ""));
        foreach (var library in steamLibraries)
        {
            FactLine(text, "");
            FactLine(text, library);
            try
            {
                var workshop = Path.Combine(library, "steamapps", "workshop");
                var coop = Path.Combine(workshop, "content", "261550", AntivirusFiles.CoopWorkshopId);
                var server = Path.Combine(coop, "DedicatedServer");
                FactLine(text, "  content folder: " + YesNo(Directory.Exists(coop)));
                FactLine(text, "  SubModule.xml: " + YesNo(File.Exists(Path.Combine(coop, "SubModule.xml"))));
                FactLine(text, "  DedicatedServer\\" + AntivirusFiles.ServerExeName + ": " + YesNo(File.Exists(Path.Combine(server, AntivirusFiles.ServerExeName))));
                FactLine(text, "  DedicatedServer\\engine\\dotnet\\dotnet.exe: " + YesNo(File.Exists(DedicatedEngineLaunch.Dotnet(server))));
                FactLine(text, "  DedicatedServer starter DLL: " + YesNo(File.Exists(DedicatedEngineLaunch.Starter(server))));
                FactLine(text, "  download folder (workshop\\downloads\\261550\\" + AntivirusFiles.CoopWorkshopId + "): "
                    + YesNo(Directory.Exists(Path.Combine(workshop, "downloads", "261550", AntivirusFiles.CoopWorkshopId))));
                var acf = Path.Combine(workshop, "appworkshop_261550.acf");
                if (File.Exists(acf))
                {
                    var list = File.ReadAllText(acf);
                    FactLine(text, "  appworkshop_261550.acf names Coop: " + YesNo(list.Contains("\"" + AntivirusFiles.CoopWorkshopId + "\"", StringComparison.Ordinal))
                        + ", Coop's own entry says a download or update is waiting: " + YesNo(SteamWorkshopAcf.PendingIn(list, AntivirusFiles.CoopWorkshopId)));
                }
                else
                {
                    FactLine(text, "  appworkshop_261550.acf: no");
                }
            }
            catch (Exception ex) when (AntivirusWatch.Harmless(ex))
            {
                FactLine(text, "  could not be read: " + ex.GetType().Name);
            }
        }

        FactLine(text, "");
        FactLine(text, "Steam still downloading Coop: " + YesNo(AntivirusFiles.CoopDownloading(steamLibraries)));
        return text.ToString();
    }

    // Every line of the file goes through here, so tools/ExtractUiText leaves them out: the log pack stays English.
    private static void FactLine(StringBuilder text, string line) => text.AppendLine(line);

    private static string YesNo(bool value) => value ? "yes" : "no";
}
