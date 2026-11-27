using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Launch;

namespace TaomCoop.Core;

/// <summary>
/// Extra SubModule entries in the TAOM server's DedicatedServer.Windows module (always loaded). The TAOM host
/// only loads its sealed module list, so Coodsz's server parts ride in there: the module list stays the same,
/// the save loads without a module mismatch, and Coop's join check never sees them. Used by Coop Save Rescue
/// and Coodsz War Sync.
/// </summary>
public static class ServerWindowsSubModules
{
    public static string ModuleDirectory(string serverRoot) =>
        Path.Combine(serverRoot, "engine", "Modules", "DedicatedServer.Windows");

    public static string BinDirectory(string serverRoot) => Path.Combine(ModuleDirectory(serverRoot), "bin", "Win64_Shipping_Server");

    /// <summary>The entry is in SubModule.xml and every one of <paramref name="dllNames"/> is in the bin folder.</summary>
    public static bool IsInstalled(string serverRoot, string subModuleName, params string[] dllNames)
    {
        try
        {
            var xml = Path.Combine(ModuleDirectory(serverRoot), "SubModule.xml");
            return File.Exists(xml) && dllNames.Length > 0 && ServerSubModuleXml.Has(ServerSubModuleXml.Read(xml).Text, dllNames[0], subModuleName)
                && dllNames.All(dll => File.Exists(Path.Combine(BinDirectory(serverRoot), dll)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            // Only asked to word a log line; a locked file counts as not there.
            return false;
        }
    }

    /// <summary>Copies each DLL when it is missing or different. Throws with a player facing reason when the server is not prepared.</summary>
    public static void CopyDlls(string serverRoot, IEnumerable<string> sources)
    {
        if (!File.Exists(Path.Combine(ModuleDirectory(serverRoot), "SubModule.xml")))
            throw new InvalidOperationException("DedicatedServer.Windows was not found in the server folder. Prepare the server first.");
        var bin = BinDirectory(serverRoot);
        Directory.CreateDirectory(bin);
        foreach (var source in sources)
        {
            var target = Path.Combine(bin, Path.GetFileName(source));
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(source)))
                File.Copy(source, target, overwrite: true);
        }
    }

    /// <summary>Adds the SubModule entry once, before &lt;/SubModules&gt;, in the file's own indent, line ends and
    /// BOM (ServerSubModuleXml). Repeated entries from older tools are collapsed first.</summary>
    public static void EnsureEntry(string serverRoot, string subModuleName, string dllName, string classType)
    {
        var xmlPath = Path.Combine(ModuleDirectory(serverRoot), "SubModule.xml");
        if (!File.Exists(xmlPath))
            throw new InvalidOperationException("DedicatedServer.Windows was not found in the server folder. Prepare the server first.");
        ServerSubModuleXml.Collapse(xmlPath);
        var (xml, bom) = ServerSubModuleXml.Read(xmlPath);
        if (ServerSubModuleXml.Has(xml, dllName, subModuleName))
            return;
        var next = ServerSubModuleXml.Insert(xml, subModuleName, dllName, classType)
            ?? throw new InvalidOperationException("DedicatedServer.Windows\\SubModule.xml looks different than expected. Nothing was changed.");
        ServerSubModuleXml.Backup(xmlPath);
        ServerSubModuleXml.Write(xmlPath, next, bom);
    }
}
