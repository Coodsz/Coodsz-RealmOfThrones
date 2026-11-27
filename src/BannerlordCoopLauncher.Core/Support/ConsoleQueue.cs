using System.Text;

namespace BannerlordCoopLauncher.Core.Support;

// PickedUp: the server took the command file. Answered: it wrote a result.
// Failed: the result started with ERROR|.
public sealed record ConsoleQueueReply(bool PickedUp, bool Answered, bool Failed, string Text)
{
    public bool Succeeded => PickedUp && !Failed;

    // The console-queue folders the command was written to, and why a write
    // failed. Shown when the server did not pick the command up.
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string WriteError { get; init; } = "";

    // A server start removed the command before anyone took it (ConsoleQueue.ClearPending). A flag, so it stays true when
    // a caller adds words to Text (ServerSession.Explain does).
    public bool WasCleared { get; init; }
}

// The dedicated server's DedicatedServer.Windows module reads commands from
// server-data\console-queue\<id>.cmd and writes server-data\console-results\<id>.result
// ("OK|..." or "ERROR|..."). It does this for any running server, whichever
// program started it, so a reopened host window can still send commands.
public static class ConsoleQueue
{
    public const string NotPickedUp = "The server did not pick up the command. Wait until the status says Server online, then send it again.";
    public const string StillWorking = "Sent. The server took the command but has not answered yet.";
    public const int MaxCommandLength = 2048;

    // A command file this old was never read by a server. It is removed so it
    // cannot run when a server starts later.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    // The reply of a command ClearPending removed while its Send still waited: a server start threw it away. The same
    // words as the TAOM queue's RunEnded, so it reads as not sent everywhere.
    public const string Cleared = "The command was not sent: the server it was queued for has stopped.";

    // The ids Send is still waiting on, with what ClearPending did to that command's file: Clearing while it tries the
    // delete, Removed once the file is gone by its hand (a gone file then means cleared, not taken by the server), and
    // back to Waits when the file stayed locked, so that Send keeps its usual answer.
    private const int Waits = 0, Clearing = 1, Removed = 2;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Waiting = new(StringComparer.OrdinalIgnoreCase);

    private static int StateOf(string id) => Waiting.TryGetValue(id, out var state) ? state : Waits;

    public static ConsoleQueueReply Send(IEnumerable<string> dataRoots, string command, TimeSpan? timeout = null)
    {
        var text = command.Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("Enter a command.");
        if (text.Length > MaxCommandLength)
            throw new InvalidOperationException("Commands may not exceed 2048 characters.");

        var id = Guid.NewGuid().ToString("N");
        Waiting[id] = Waits;
        try
        {
            return SendAs(id, dataRoots, text, timeout);
        }
        finally
        {
            Waiting.TryRemove(id, out _);
        }
    }

    private static ConsoleQueueReply SendAs(string id, IEnumerable<string> dataRoots, string text, TimeSpan? timeout)
    {
        var resultPaths = new List<string>();
        var commandPaths = new List<string>();
        var folders = new List<string>();
        var writeError = "";
        foreach (var dataRoot in Distinct(dataRoots))
        {
            var queue = Path.Combine(dataRoot, "console-queue");
            folders.Add(queue);
            try
            {
                var results = Path.Combine(dataRoot, "console-results");
                Directory.CreateDirectory(queue);
                Directory.CreateDirectory(results);
                RemoveStale(queue);
                var commandPath = Path.Combine(queue, id + ".cmd");
                var temporaryPath = Path.Combine(queue, id + ".tmp");
                File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
                File.Move(temporaryPath, commandPath, overwrite: true);
                commandPaths.Add(commandPath);
                resultPaths.Add(Path.Combine(results, id + ".result"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                writeError = "Could not write the command into " + queue + ": " + ex.Message;
            }
        }

        if (commandPaths.Count == 0)
            return new ConsoleQueueReply(false, false, false, writeError.Length > 0 ? writeError : NotPickedUp) { Folders = folders, WriteError = writeError };

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            foreach (var resultPath in resultPaths)
            {
                string result;
                try
                {
                    if (!File.Exists(resultPath))
                        continue;
                    result = File.ReadAllText(resultPath);
                    // Every answer starts with OK| or ERROR|, so an empty file is one the server
                    // has created but not filled yet. It is left for the next read.
                    if (result.Length == 0)
                        continue;
                    TryDelete(resultPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The server is still writing the result.
                    continue;
                }

                RemoveUnread(commandPaths);
                if (result.StartsWith("OK|", StringComparison.Ordinal))
                    return new ConsoleQueueReply(true, true, false, result[3..]);
                if (result.StartsWith("ERROR|", StringComparison.Ordinal))
                    return new ConsoleQueueReply(true, true, true, result[6..]);
                return new ConsoleQueueReply(true, true, false, result);
            }

            if (StateOf(id) == Removed)
                break;
            Thread.Sleep(100);
        }

        // The server takes a command by deleting its file. Only a file that is
        // gone before we clean up counts as picked up.
        var pickedUp = commandPaths.Any(path => !File.Exists(path));
        // ClearPending marks a file Clearing before it deletes it, so the mark is read after the look at the file: a file
        // a start removed is never taken for one the server took. A delete still in its tries is waited for (one 200 ms retry).
        var settle = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (StateOf(id) == Clearing && DateTime.UtcNow < settle)
            Thread.Sleep(20);
        if (StateOf(id) is Removed or Clearing)
        {
            RemoveUnread(commandPaths);
            return new ConsoleQueueReply(false, false, true, Cleared) { Folders = folders, WasCleared = true };
        }

        // A command nobody read must not run later, when the next server starts.
        RemoveUnread(commandPaths);
        return new ConsoleQueueReply(pickedUp, false, false, pickedUp ? StillWorking : NotPickedUp) { Folders = folders };
    }

    // Leftover command files from a server that stopped or never ran. A server
    // reads the oldest files first, so old ones are cleared before a new send.
    public static int RemoveStale(string queueFolder)
    {
        var removed = 0;
        try
        {
            foreach (var path in Directory.GetFiles(queueFolder, "*.cmd"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < StaleAfter)
                    continue;
                TryDelete(path);
                if (!File.Exists(path))
                    removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return removed;
    }

    // Every command file still waiting, whatever its age. A tool closed while it waited for an
    // answer leaves its file, and a server started within StaleAfter would run it, so a host clears
    // the queue before it starts a server. Only *.cmd goes: answers live in console-results under
    // new ids each time, and a .tmp is never read. A Send still waiting on a removed file answers
    // Cleared. A locked file gets one more try a moment later and is then listed in Locked, so the
    // host can say so; a missing folder is nothing to clear. Never throws.
    public static (int Removed, IReadOnlyList<string> Locked) ClearPending(string queueFolder) =>
        ClearPending(queueFolder, _ => Thread.Sleep(200));

    // The same with what happens between the two tries given, so the tests let a lock go exactly then.
    internal static (int Removed, IReadOnlyList<string> Locked) ClearPending(string queueFolder, Action<string> beforeRetry)
    {
        var removed = 0;
        var locked = new List<string>();
        try
        {
            if (!Directory.Exists(queueFolder))
                return (0, locked);
            foreach (var path in Directory.GetFiles(queueFolder, "*.cmd"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                // Clearing before the delete, so a Send that sees its file gone waits for the answer; Removed only once the
                // file is gone, and a file that stayed locked leaves that Send its usual answer.
                Waiting.TryUpdate(id, Clearing, Waits);
                var gone = false;
                try
                {
                    TryDelete(path);
                    if (File.Exists(path))
                    {
                        beforeRetry(path);
                        TryDelete(path);
                    }

                    gone = !File.Exists(path);
                }
                finally
                {
                    // Whatever happened in between, nothing stays Clearing.
                    Waiting.TryUpdate(id, gone ? Removed : Waits, Clearing);
                }

                if (gone)
                    removed++;
                else
                    locked.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return (removed, locked);
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (seen.Add(full))
                yield return full;
        }
    }

    private static void RemoveUnread(IEnumerable<string> commandPaths)
    {
        foreach (var path in commandPaths)
            TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
