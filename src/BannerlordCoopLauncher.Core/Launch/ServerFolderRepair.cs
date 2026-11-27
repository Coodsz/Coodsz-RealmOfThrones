using BannerlordCoopLauncher.Core.IO;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// Steam often installs a thin DedicatedServer without Start-ModdedServer.ps1,
/// packaged 0Harmony.dll or the launcher submodules (cheats, console queue,
/// player list, bans, battle size). The server tools ship those files beside the
/// exe (Coodsz Host's DedicatedServerRepair folder) and copy any that are missing
/// into the active server folder on Start. Coodsz Host and the Burning Empires
/// server tool both lay the same files over their server folder with this.
/// </summary>
public static class ServerFolderRepair
{
    public const string FolderName = "DedicatedServerRepair";

    /// <summary>The DedicatedServerRepair folder the running tool carries (written out of the exe, or beside it in a dev run).</summary>
    public static string DefaultPayload => ToolPayload.Folder(FolderName);

    /// <summary>Copies what is missing, replaces an older managed script (the old one kept as
    /// .coodsz-old) and turns on the launcher submodules. toolName goes in the message. Null
    /// when nothing changed.</summary>
    public static string? Ensure(string serverRoot, string? payloadRoot, string toolName)
    {
        if (string.IsNullOrWhiteSpace(serverRoot) || !Directory.Exists(serverRoot))
            return null;

        // Only after the server folder check, since DefaultPayload writes the files out of the exe.
        payloadRoot ??= DefaultPayload;

        if (!Directory.Exists(payloadRoot))
            return null;

        var copied = new List<string>();
        var updated = new List<string>();
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(payloadRoot, source);
            if (relative.Contains("..", StringComparison.Ordinal))
                continue;

            var target = Path.Combine(serverRoot, relative);
            if (File.Exists(target))
            {
                // The start scripts and the edited Launcher Battle Size are the tools' own. An
                // older copy in the server folder is replaced so its fixes reach every server.
                if (IsManagedScript(relative) && !SameContent(source, target))
                {
                    try
                    {
                        File.Copy(target, target + ".coodsz-old", overwrite: true);
                        File.Copy(source, target, overwrite: true);
                        updated.Add(relative.Replace('\\', '/'));
                    }
                    catch (IOException)
                    {
                        // In use by a server that is still running; the next Start replaces it.
                    }
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            copied.Add(relative.Replace('\\', '/'));
        }

        // A folder with the same entries many times (ServerSubModuleXml) loads each add-on that often.
        var collapsed = ServerSubModuleXml.Collapse(SubModuleXml(serverRoot));
        var registered = EnsureLauncherSubModules(serverRoot);
        if (copied.Count == 0 && registered.Count == 0 && updated.Count == 0 && collapsed == 0)
            return null;

        var parts = new List<string>();
        if (copied.Count > 0)
            parts.Add($"Restored missing dedicated-server files from {toolName}: {string.Join(", ", copied)}.");
        if (updated.Count > 0)
            parts.Add("Updated " + string.Join(", ", updated) + " in the server folder (the old copy is kept as .coodsz-old).");
        if (registered.Count > 0)
            parts.Add("Turned on " + string.Join(" and ", registered) + " in DedicatedServer.Windows, so Cheats, Console, the player list, bans, pause and battle size work.");
        if (collapsed > 0)
            parts.Add("Removed " + collapsed + " repeated entries in DedicatedServer.Windows, so each server add on loads once.");
        return string.Join(" ", parts);
    }

    // Launcher Battle Size is edited so BANNERLORD_DS_AUTO_PAUSE=1 keeps the game's settlement and battle
    // pause (tools/patches/LauncherBattleSizeAutoPause.cs), so an unedited copy must be replaced too.
    public static readonly string[] ManagedScripts =
    {
        "Start-ModdedServer.ps1",
        "Start-ModdedServer.cmd",
        Path.Combine("engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.LauncherBattleSize.dll")
    };

    private static bool IsManagedScript(string relative) =>
        ManagedScripts.Contains(relative, StringComparer.OrdinalIgnoreCase);

    private static bool SameContent(string a, string b)
    {
        try
        {
            return FileHash.Sha256(a) == FileHash.Sha256(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // The Steam Workshop server ships DedicatedServer.Windows without the
    // launcher submodules. Without "Launcher Cheats" the server never reads
    // server-data\console-queue, never writes live-players.json and ignores
    // steam-bans.json. A Workshop update puts the original SubModule.xml back,
    // so this runs on every Start.
    public static readonly (string Name, string Dll, string Type)[] LauncherSubModules =
    {
        ("Launcher Cheats", "DedicatedServer.LauncherCheats.dll", "DedicatedServer.LauncherCheats.CheatsSubModule"),
        ("Launcher Battle Size", "DedicatedServer.LauncherBattleSize.dll", "DedicatedServer.LauncherBattleSize.BattleSizeSubModule"),
    };

    private static string SubModuleXml(string serverRoot) =>
        Path.Combine(serverRoot, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml");

    // A text insert in the file's own indent, line ends and BOM (ServerSubModuleXml). An XDocument save
    // put these entries on the line of </SubModules>, which made Coop Save Rescue's insert repeat them.
    public static IReadOnlyList<string> EnsureLauncherSubModules(string serverRoot)
    {
        var added = new List<string>();
        var module = Path.Combine(serverRoot, "engine", "Modules", "DedicatedServer.Windows");
        var xmlPath = Path.Combine(module, "SubModule.xml");
        if (!File.Exists(xmlPath))
            return added;

        ServerSubModuleXml.Collapse(xmlPath);
        string text;
        bool bom;
        try
        {
            (text, bom) = ServerSubModuleXml.Read(xmlPath);
            System.Xml.Linq.XDocument.Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return added;
        }

        foreach (var (name, dll, type) in LauncherSubModules)
        {
            if (!File.Exists(Path.Combine(module, "bin", "Win64_Shipping_Server", dll)))
                continue;
            if (ServerSubModuleXml.Has(text, dll, name))
                continue;
            if (ServerSubModuleXml.Insert(text, name, dll, type) is not { } next)
                break;
            text = next;
            added.Add(name);
        }

        if (added.Count == 0)
            return added;

        try
        {
            ServerSubModuleXml.Backup(xmlPath);
            ServerSubModuleXml.Write(xmlPath, text, bom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            added.Clear();
        }

        return added;
    }
}
