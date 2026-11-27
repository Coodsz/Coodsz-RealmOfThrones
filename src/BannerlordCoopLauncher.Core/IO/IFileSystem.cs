namespace BannerlordCoopLauncher.Core.IO;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string? ReadAllText(string path);
    IEnumerable<string> EnumerateDirectories(string directory);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? ReadAllText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<string>();
        return Directory.EnumerateDirectories(directory);
    }
}
