using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// The Coodsz Server Tools module (DedicatedServer.CoodszTools, compat\CoodszServerTools) in a server folder, for
/// every server tool that carries it: Coodsz Host (CoodszServerTools) and the Realm of Thrones Server Tool. Each copies
/// its payload copy into engine\Modules on every start and lists the module last. The TAOM Server Tool runs the same DLL
/// from DedicatedServer.Windows instead (TaomServerTools). Coop leaves DedicatedServer.* modules out of the client check,
/// so players do not need it.
/// </summary>
public static class ServerToolsModule
{
    public const string ModuleId = ToolsCommand.ModuleId;
    public const string Name = "Coodsz Server Tools";

    /// <summary>The server's data folder, set by the Realm of Thrones and TAOM Server Tools' starts only; Coodsz Host never sets
    /// it, so its server keeps reading Documents. The module and the settings sync read it (ClanCapSettings.ServerDataDirVariable,
    /// SettingsFiles.ServerDataDirVariable). TAOM's start also sets BANNERLORD_USER_DIR, which is why that one is not read.</summary>
    public const string DataDirVariable = "COODSZ_SERVER_DATA_DIR";

    public static string ModuleFolder(string serverRoot) => Path.Combine(ServerModuleInstall.ModulesRoot(serverRoot), ModuleId);

    /// <summary>True when the server folder has the module (its SubModule.xml), so a start may list it.</summary>
    public static bool Installed(string serverRoot) => File.Exists(Path.Combine(ModuleFolder(serverRoot), "SubModule.xml"));

    /// <summary>
    /// Copies every file of the payload copy whose bytes differ into the server's module folder and returns how many
    /// were copied. A file in use by a server that still runs is left for the next start.
    /// </summary>
    public static int CopyInto(string payloadRoot, string serverRoot)
    {
        var target = ModuleFolder(serverRoot);
        var copied = 0;
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            try
            {
                if (File.Exists(destination) && SameBytes(source, destination))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a server that is still running; the next start copies it.
            }
        }

        return copied;
    }

    /// <summary>A module list with the tools module last, once.</summary>
    public static IReadOnlyList<string> WithTools(IEnumerable<string> modules) =>
        modules.Where(id => !id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase)).Append(ModuleId).ToList();

    private static bool SameBytes(string a, string b)
    {
        var left = new FileInfo(a);
        var right = new FileInfo(b);
        return left.Length == right.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
