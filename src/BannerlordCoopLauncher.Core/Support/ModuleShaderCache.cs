namespace BannerlordCoopLauncher.Core.Support;

public static class ModuleShaderCache
{
    public readonly record struct Move(string Name, string? Error);

    public static IReadOnlyList<Move> SetAside(IEnumerable<(string Name, string Directory)> modules)
    {
        var moved = new List<Move>();
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Directory))
                continue;
            var sack = Path.Combine(module.Directory, "Shaders", "D3D11", "compressed_shader_cache.sack");
            if (!File.Exists(sack))
                continue;
            var name = string.IsNullOrWhiteSpace(module.Name) ? new DirectoryInfo(module.Directory).Name : module.Name;
            try
            {
                var aside = sack + ".coodsz-old";
                ClearReadOnly(sack);
                if (File.Exists(aside))
                {
                    ClearReadOnly(aside);
                    File.Delete(aside);
                }

                File.Move(sack, aside);
                moved.Add(new Move(name, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                moved.Add(new Move(name, ex.Message));
            }
        }

        return moved;
    }

    private static void ClearReadOnly(string path)
    {
        var info = new FileInfo(path);
        if (info.IsReadOnly)
            info.IsReadOnly = false;
    }
}
