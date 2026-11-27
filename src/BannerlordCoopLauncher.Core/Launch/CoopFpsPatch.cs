namespace BannerlordCoopLauncher.Core.Launch;

public static class CoopFpsPatch
{
    public const string StaleAdapterName = "Adapter.Native.dll";

    public static IReadOnlyList<string> RemoveStaleBattleSizeAdapter(IEnumerable<string> roots)
    {
        var removed = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = ClientFiles.Under(root, StaleAdapterName).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    removed.Add(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return removed;
    }
}
