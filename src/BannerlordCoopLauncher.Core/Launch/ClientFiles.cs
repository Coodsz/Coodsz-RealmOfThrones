namespace BannerlordCoopLauncher.Core.Launch;

// The client launchers patch and clean mod files under the game Modules folder
// and the Steam Workshop folder. The Coop Workshop item also carries the
// dedicated server (3770450698\DedicatedServer), and a host often plays on the
// same PC. Server files must never be touched: a changed Coop.Core.dll there
// stops the server with COOP MODULE VERIFICATION FAILED.
public static class ClientFiles
{
    public static IEnumerable<string> Under(string root, string fileName) =>
        Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Where(path => !IsServerCopy(path));

    public static bool IsServerCopy(string path)
    {
        var normal = path.Replace('/', '\\');
        return normal.Contains(@"\DedicatedServer\", StringComparison.OrdinalIgnoreCase)
            || normal.Contains(@"\Win64_Shipping_Server\", StringComparison.OrdinalIgnoreCase);
    }
}
