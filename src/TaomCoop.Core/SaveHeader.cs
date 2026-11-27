using System.Text;
using System.Text.RegularExpressions;

namespace TaomCoop.Core;

public static class SaveHeader
{
    public static DateTime? ReadCreated(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var match = Regex.Match(text, "\"CreationTime\"\\s*:\\s*\"(\\d+)\"");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out var ticks))
                return null;
            return new DateTime(ticks, DateTimeKind.Unspecified);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
