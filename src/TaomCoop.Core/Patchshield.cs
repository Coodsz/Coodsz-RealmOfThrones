namespace TaomCoop.Core;

public static class Patchshield
{
    public const string FileName = "patchshield-disabled.flag";

    public static bool Disable(string gameRoot)
    {
        var folder = Path.Combine(gameRoot, "Modules", "TAOM.Dependencies");
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Modules\\TAOM.Dependencies was not found. Click Install or repair TAOM first.");
        var path = Path.Combine(folder, FileName);
        if (File.Exists(path))
            return false;
        File.WriteAllBytes(path, Array.Empty<byte>());
        return true;
    }

    public static bool Enable(string gameRoot)
    {
        var folder = Path.Combine(gameRoot, "Modules", "TAOM.Dependencies");
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Modules\\TAOM.Dependencies was not found. Click Install or repair TAOM first.");
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    public static string? FlagPath(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return null;
        var path = Path.Combine(gameRoot, "Modules", "TAOM.Dependencies", FileName);
        return File.Exists(path) ? path : null;
    }
}
