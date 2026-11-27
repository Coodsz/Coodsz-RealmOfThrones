using BannerlordCoopLauncher.Core.IO;

namespace BannerlordCoopLauncher.Core.Steam;

public static class GameRoot
{
    public static bool IsRoot(string path, IFileSystem files)
    {
        var executable = Path.Combine(path, "bin", "Win64_Shipping_Client", "Bannerlord.exe");
        return files.FileExists(executable) && files.DirectoryExists(Path.Combine(path, "Modules"));
    }

    public static string ExecutablePath(string gameRoot) =>
        Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Bannerlord.exe");

    public static string WorkingDirectory(string gameRoot) =>
        Path.Combine(gameRoot, "bin", "Win64_Shipping_Client");

    public static string? Normalize(string? input, IFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var path = input.Trim().Trim('"');
        if (files.FileExists(path))
            path = Path.GetDirectoryName(path) ?? path;

        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(path); depth++)
        {
            if (IsRoot(path, files))
                return path;
            path = Path.GetDirectoryName(path);
        }

        return null;
    }
}
