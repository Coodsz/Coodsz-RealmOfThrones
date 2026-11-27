namespace BannerlordCoopLauncher.Core.Support;

public static class ShaderCache
{
    public static int Clear(string bannerlordProgramData, bool gameIsRunning)
    {
        if (gameIsRunning)
            throw new InvalidOperationException("Close Bannerlord before clearing the shader cache.");

        if (string.IsNullOrWhiteSpace(bannerlordProgramData))
            throw new InvalidOperationException("The shader cache folder was not found.");

        var parent = Path.GetFullPath(bannerlordProgramData);
        var folder = Path.GetFullPath(Path.Combine(parent, "Shaders"));
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The shader cache folder was not found.");

        if (!Directory.Exists(folder))
            return 0;

        var count = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(folder, recursive: true);
        return count;
    }
}
