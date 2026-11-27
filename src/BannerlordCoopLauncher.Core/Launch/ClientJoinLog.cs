using System.Diagnostics;
using System.Text;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// Coop's client log while a client launcher watches a join. Coop 0.1.7 writes Coop_client.log, or
/// Coop_client_&lt;pid&gt;.log when another process holds Coop_client.log.lock (CoopMod.SetupLogging), so this reads
/// both and keeps only the text written after the game started. Every client launcher's WatchJoinAsync uses it.
/// </summary>
public sealed class ClientJoinLog
{
    /// <summary>The log line when the watch ends with the game open and nothing in either log.</summary>
    public const string EmptyLogNote = "Coop wrote nothing to its client log, so this tool can't tell whether the join worked. Look in the game.";

    private readonly StringBuilder _text = new();
    private long _mainOffset;
    private long _processOffset;

    public ClientJoinLog(string gameRoot, int processId)
    {
        MainPath = PathIn(gameRoot);
        ProcessPath = PathIn(gameRoot, processId);
        _mainOffset = Length(MainPath);
        _processOffset = Length(ProcessPath);
    }

    public string MainPath { get; }

    public string ProcessPath { get; }

    /// <summary>Everything either log got since the watch began.</summary>
    public string Text => _text.ToString();

    /// <summary>True once either log got any new text.</summary>
    public bool GotText => _text.Length > 0;

    /// <summary>Both logs' size on disk, for the AD1259 loading watch.</summary>
    public long Bytes => Length(MainPath) + Length(ProcessPath);

    /// <summary>Coop_client.log in the game's client bin folder.</summary>
    public static string PathIn(string gameRoot) => Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Coop_client.log");

    /// <summary>The log Coop writes instead when Coop_client.log is taken: Coop_client_&lt;pid&gt;.log beside it.</summary>
    public static string PathIn(string gameRoot, int processId) =>
        Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Coop_client_" + processId + ".log");

    /// <summary>Adds what both logs got since the last read and returns all the text so far.</summary>
    public string Read()
    {
        Append(MainPath, ref _mainOffset);
        Append(ProcessPath, ref _processOffset);
        return Text;
    }

    /// <summary>The pill when the watch ends with no join result: Game open while the game runs and neither log got
    /// any text, since then nothing tells whether the join worked; else Joining until the game closes.</summary>
    public static string TimedOutState(bool gameRunning, bool gotText) =>
        gameRunning && !gotText ? ClientNextStep.GameOpen : ClientNextStep.Joining;

    public string TimedOutState(bool gameRunning) => TimedOutState(gameRunning, GotText);

    /// <summary>False once the game closed, or when it can't be asked.</summary>
    public static bool StillRunning(Process game)
    {
        try
        {
            return !game.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void Append(string path, ref long offset)
    {
        if (!File.Exists(path))
            return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Coop deletes the log when it starts, so a shorter file is a new one.
            if (stream.Length < offset)
                offset = 0;
            stream.Position = offset;
            using var reader = new StreamReader(stream);
            var added = reader.ReadToEnd();
            offset = stream.Position;
            if (added.Length > 0)
                _text.Append(added);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone or locked between the check and the open while Coop makes it again: nothing new this poll.
        }
    }

    private static long Length(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
