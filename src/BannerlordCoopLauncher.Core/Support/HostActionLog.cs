using System.IO;
using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

public sealed class HostActionLog
{
    private const long MaxBytes = 2_000_000;
    private readonly object _gate = new();
    private readonly Func<string?> _directory;
    private readonly string _fileName;

    public HostActionLog(Func<string?> directory, string fileName = "host-action.log")
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _fileName = string.IsNullOrWhiteSpace(fileName) ? "host-action.log" : fileName.Trim();
    }

    public string? FilePath
    {
        get
        {
            var directory = Folder();
            return directory == null ? null : Path.Combine(directory, _fileName);
        }
    }

    public void Note(string action, string detail)
    {
        if (string.IsNullOrWhiteSpace(action))
            return;
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + "  "
            + action.Trim()
            + (string.IsNullOrWhiteSpace(detail) ? "" : "  " + detail.Trim());
        Write(ProblemReport.Redact(line));
    }

    public void NotePassword(bool hasPassword) =>
        Note("Password", hasPassword ? "set" : "cleared");

    private string? Folder()
    {
        try
        {
            var directory = _directory();
            if (string.IsNullOrWhiteSpace(directory))
                return null;
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch
        {
            return null;
        }
    }

    private void Write(string line)
    {
        var path = FilePath;
        if (path == null)
            return;
        lock (_gate)
        {
            try
            {
                TrimIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
            }
        }
    }

    private static void TrimIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes)
            return;
        var text = File.ReadAllText(path);
        var keep = text.Length / 2;
        var cut = text.LastIndexOf('\n', Math.Max(0, text.Length - keep));
        if (cut < 0)
            return;
        File.WriteAllText(path, text[(cut + 1)..], new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
