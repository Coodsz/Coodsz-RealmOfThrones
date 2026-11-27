using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

// Keeps a copy of what a host window reads from the server's console, one
// file per server start (host-console-<date>.log), so it is still there after
// the window closes. Lines are cleaned like the log zip.
public sealed class ConsoleLogFile : IDisposable
{
    public const string Pattern = "host-console-*.log";

    private readonly object _gate = new();
    private StreamWriter? _writer;

    public string? Path { get; private set; }

    public void Open(string logsRoot, string firstLine)
    {
        Close();
        try
        {
            Directory.CreateDirectory(logsRoot);
            var path = System.IO.Path.Combine(logsRoot, "host-console-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            lock (_gate)
                _writer = writer;
            Path = path;
            Write(firstLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Path = null;
        }
    }

    public void Write(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        lock (_gate)
        {
            if (_writer == null)
                return;
            try
            {
                _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + ProblemReport.Redact(line));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose() => Close();
}
