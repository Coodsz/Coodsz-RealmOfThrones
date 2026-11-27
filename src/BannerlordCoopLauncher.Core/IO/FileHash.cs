using System.Security.Cryptography;

namespace BannerlordCoopLauncher.Core.IO;

// SHA-256 of a file, as upper-case hex. One helper for every tool.
public static class FileHash
{
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Sha256(stream);
    }

    public static string Sha256(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream));

    // An empty string when the file cannot be read (locked by antivirus, for example).
    public static string TrySha256(string path)
    {
        try
        {
            return Sha256(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
