namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// The newest server console lines, capped by length. Adding a line is cheap: the text is only joined
/// when it is read. Rebuilding a 200 KB string on every line kept the TAOM tool's output reader busy
/// during a burst, and a server whose console nobody reads stalls on its own writes.
/// </summary>
public sealed class ServerLogTail
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _lines = new();
    private readonly int _maxChars;
    private int _chars;

    public ServerLogTail(int maxChars) => _maxChars = maxChars;

    public void Add(string line)
    {
        lock (_gate)
        {
            _lines.AddLast(line);
            _chars += line.Length + Environment.NewLine.Length;
            while (_chars > _maxChars && _lines.Count > 1)
            {
                _chars -= _lines.First!.Value.Length + Environment.NewLine.Length;
                _lines.RemoveFirst();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _chars = 0;
        }
    }

    /// <summary>The last <paramref name="count"/> lines joined, for checks that only need recent context.</summary>
    public string Recent(int count)
    {
        lock (_gate)
        {
            var skip = Math.Max(0, _lines.Count - count);
            return string.Join(Environment.NewLine, _lines.Skip(skip));
        }
    }

    public override string ToString()
    {
        lock (_gate)
            return string.Join(Environment.NewLine, _lines);
    }
}
