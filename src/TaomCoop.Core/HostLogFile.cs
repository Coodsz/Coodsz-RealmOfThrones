using System.Text;

namespace TaomCoop.Core;

public static class HostLogFile
{
    public static string ReadTail(string path, int maxBytes = 200_000)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var unicode = false;
            if (stream.Length >= 2)
            {
                var first = stream.ReadByte();
                var second = stream.ReadByte();
                unicode = first == 0xFF && second == 0xFE;
            }

            var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
            if (!unicode)
                stream.Position = 0;

            if (stream.Length > maxBytes)
            {
                var start = stream.Length - maxBytes;
                if (unicode && (start & 1) != 0)
                    start--;
                stream.Position = start;
            }

            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    public static string ReadTailContaining(string path, string marker, int windowBytes = 256_000)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var unicode = false;
            if (stream.Length >= 2)
            {
                var first = stream.ReadByte();
                var second = stream.ReadByte();
                unicode = first == 0xFF && second == 0xFE;
            }

            var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
            var startLimit = unicode ? 2L : 0L;
            var end = stream.Length;
            var overlap = 4_096;
            while (end > startLimit)
            {
                var window = (int)Math.Min(windowBytes, end - startLimit);
                var start = end - window;
                if (unicode && (start & 1) != 0)
                    start--;
                stream.Position = start;
                var count = (int)(end - start);
                var buffer = new byte[count];
                var read = stream.Read(buffer, 0, count);
                var text = encoding.GetString(buffer, 0, read);
                if (text.Contains(marker, StringComparison.Ordinal))
                    return text;
                if (start <= startLimit)
                    break;
                var nextEnd = start + overlap;
                if (unicode && (nextEnd & 1) != 0)
                    nextEnd--;
                if (nextEnd >= end)
                    break;
                end = nextEnd;
            }

            return "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    public static IEnumerable<FileInfo> Prefer(IEnumerable<FileInfo> files)
    {
        return files.OrderBy(Rank).ThenByDescending(file => file.LastWriteTimeUtc);
    }

    /// <summary>Only the logs written since this server run started. The Coodsz TAOM server (the
    /// engine started directly) writes no coop-server log, so without this the coop-server log of an
    /// older run would be read as if it were this run, SERVING line and all.</summary>
    public static IEnumerable<FileInfo> ThisRun(IEnumerable<FileInfo> files, DateTime? startedUtc) =>
        startedUtc == null ? files : files.Where(file => file.LastWriteTimeUtc >= startedUtc.Value);

    /// <summary>The log with the server's console of this run: the coop-server log of the sealed
    /// server, or the host-console log the tool keeps (the only one the Coodsz TAOM server has).</summary>
    public static FileInfo? ConsoleOf(IEnumerable<FileInfo> files, DateTime? startedUtc) =>
        Prefer(ThisRun(files, startedUtc).Where(file =>
                file.Name.StartsWith("coop-server-", StringComparison.OrdinalIgnoreCase)
                || file.Name.StartsWith("host-console-", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();

    private static int Rank(FileInfo file)
    {
        var name = file.Name;
        if (name.StartsWith("coop-server", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.StartsWith("server-run", StringComparison.OrdinalIgnoreCase))
            return 1;
        // The tool's own copy of the server console: the main log of the Coodsz TAOM server,
        // ahead of the mod logs (TAOM.CoopCompat) that can be written later.
        if (name.StartsWith("host-console", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }
}
