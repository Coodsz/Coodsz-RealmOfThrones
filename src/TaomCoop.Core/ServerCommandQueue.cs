using System.Runtime.CompilerServices;

namespace TaomCoop.Core;

/// <summary>What a command's turn finds (ServerCommandQueue.Check).</summary>
public enum RunCheck
{
    Same,
    NoServer,
    Ended
}

/// <summary>The server run a command was queued for: the window's own process, or the start time of a server it did not start.</summary>
public sealed record ServerRun(System.Diagnostics.Process? Process, DateTime? ForeignStarted, int? ForeignProcessId = null);

/// <summary>How a bounded write to the server's console ended (ServerCommandQueue.WriteWithinAsync).</summary>
public enum ConsoleWrite
{
    /// <summary>Written.</summary>
    Sent,
    /// <summary>Not written, and it never will be: the lock or the turn did not come in time.</summary>
    NotSent,
    /// <summary>The write began but had not finished in time (a full pipe): it may still reach the server.</summary>
    MaybeSent,
    /// <summary>Not written: the caller's last check, run inside the stdin lock right before the write, said no.</summary>
    Dropped
}

/// <summary>
/// The one ordered path for every command a server tool writes to its server (the Realm of Thrones and TAOM Server Tools), like
/// Coodsz Host's ServerSession.SendDetailed: commands run strictly in the order they were queued, one at a time, and
/// each keeps its turn until its reply is read. The caller records the host's intent (a time command) when it queues,
/// so that order is the queue's order. An automatic step (a join pause, a Play hold resend, a kick) passes a check that
/// runs at its turn, right before it is written; when it says no, nothing is written and null comes back.
/// There is no timeout per turn: a stdin write that blocks holds the whole queue. The world save and stop therefore go
/// through <see cref="WriteWithin"/>, whose fallback writes them anyway when their turn does not come in time.
/// </summary>
public sealed class ServerCommandQueue
{
    private readonly object _lock = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>How long a write waits for the stdin lock before it counts as not sent.</summary>
    public static readonly TimeSpan StdinLockWait = TimeSpan.FromSeconds(5);

    /// <summary>The reply of a command whose write did not get the stdin lock in time. "was not sent" makes it a failure
    /// for CommandReply and ServerTime.Result.</summary>
    public const string ConsoleTimeout = "The command was not sent: the server's console did not take it within 5 seconds.";

    /// <summary>The reply of a command whose write failed (a broken pipe), a failure for CommandReply too.</summary>
    public static string NotSent(string reason) => "The command was not sent: " + reason;

    /// <summary>
    /// Runs <paramref name="write"/> under <paramref name="stdinLock"/>, waiting at most <paramref name="wait"/> for it.
    /// False when the lock did not come (a write that blocks holds it), so the caller treats the line as not sent and a
    /// forced stop still runs. A throw from the write goes to the caller.
    /// </summary>
    public static bool TryWriteUnder(object stdinLock, TimeSpan wait, Action write)
    {
        if (!Monitor.TryEnter(stdinLock, wait))
            return false;
        try
        {
            write();
            return true;
        }
        finally
        {
            Monitor.Exit(stdinLock);
        }
    }

    /// <summary>The reply of a command whose write began but had not finished within 5 seconds (a full pipe): it may still
    /// reach the server once the pipe moves, so it is neither a done line nor a "was not sent" failure that invites a
    /// second try.</summary>
    public const string ConsoleSlow = "The server's console is slow to take the command, so it may still reach the server in a moment. Check the server log before you send it again.";

    /// <summary>True when a reply is <see cref="ConsoleSlow"/>: the command may or may not have landed yet, so a log line must not say it did.</summary>
    public static bool Unconfirmed(IEnumerable<string>? reply) => reply?.Contains(ConsoleSlow) == true;

    /// <summary>
    /// The write on a pool thread, waiting at most <paramref name="wait"/> in all, the lock and the write itself, so a
    /// WriteLine or Flush that blocks on a full pipe never holds the caller, the window or the queue forever. One state
    /// decides between the write and the deadline: a write that had not begun by the deadline never begins afterwards
    /// (<see cref="ConsoleWrite.NotSent"/>), and one that began but had not finished (blocked in the pipe) is
    /// <see cref="ConsoleWrite.MaybeSent"/>, since it goes out once the pipe moves. A blocked write keeps the stdin lock,
    /// so later writes time out on it too and a forced stop still runs. A throw from the write goes to the caller.
    /// </summary>
    public static async Task<ConsoleWrite> WriteWithinAsync(object stdinLock, TimeSpan wait, Action write) =>
        (await WriteBounded(stdinLock, wait, write).ConfigureAwait(false)).Result;

    /// <summary>
    /// <see cref="WriteWithinAsync"/> with the write's own task, so a caller can wait for a write that may still land
    /// (<see cref="AfterStuckWrite"/>) before it reads the next reply.
    /// </summary>
    public static async Task<(ConsoleWrite Result, Task<ConsoleWrite> Writing)> WriteBounded(object stdinLock, TimeSpan wait, Action write,
        Func<bool>? stillValid = null)
    {
        using var cancel = new CancellationTokenSource();
        var deadline = Task.Delay(wait, cancel.Token);
        var result = await WriteBounded(stdinLock, deadline, wait, write, stillValid).ConfigureAwait(false);
        // The write won: the timer goes.
        cancel.Cancel();
        return result;
    }

    /// <summary>The same with the deadline and the wait for the lock given apart, so the tests decide when the deadline comes.</summary>
    internal static async Task<(ConsoleWrite Result, Task<ConsoleWrite> Writing)> WriteBounded(object stdinLock, Task deadline, TimeSpan lockWait, Action write,
        Func<bool>? stillValid = null)
    {
        var state = new StrongBox<int>(Pending);
        var writing = Task.Run(() => WriteOnce(stdinLock, lockWait, write, state, stillValid));
        await Task.WhenAny(writing, deadline).ConfigureAwait(false);
        return (writing.IsCompleted ? await writing.ConfigureAwait(false) : TooLate(state), writing);
    }

    /// <summary>
    /// Before the next command is written: a write that was still stuck on a full pipe when its turn ended (MaybeSent) is
    /// waited for, at most <paramref name="wait"/>, and then its output gets <paramref name="settle"/> to reach the log,
    /// so the next reply read starts after it and never takes that command's output for its own. False when the stuck
    /// write is still not through: the next command would only wait on the stdin lock it holds, so it is not sent.
    /// </summary>
    public static async Task<bool> AfterStuckWrite(Task? stuck, TimeSpan wait, TimeSpan settle)
    {
        if (stuck == null)
            return true;
        if (!stuck.IsCompleted)
        {
            using var cancel = new CancellationTokenSource();
            if (await Task.WhenAny(stuck, Task.Delay(wait, cancel.Token)).ConfigureAwait(false) != stuck)
                return false;
            cancel.Cancel();
        }

        await Task.Delay(settle).ConfigureAwait(false);
        return true;
    }

    /// <summary><see cref="WriteWithinAsync"/> for a caller that must have the answer now (the save, stop and restart lines).</summary>
    public static ConsoleWrite TryWriteWithin(object stdinLock, TimeSpan wait, Action write) =>
        TryWriteWithin(stdinLock, wait, write, writing => writing.Wait(wait));

    /// <summary>The same with the wait for the deadline given, so the tests decide when it comes.</summary>
    internal static ConsoleWrite TryWriteWithin(object stdinLock, TimeSpan lockWait, Action write, Action<Task> untilDeadline)
    {
        var state = new StrongBox<int>(Pending);
        var writing = Task.Run(() => WriteOnce(stdinLock, lockWait, write, state));
        try
        {
            untilDeadline(writing);
        }
        catch (AggregateException)
        {
            // The write threw: GetResult below gives the caller the exception itself.
        }

        return writing.IsCompleted ? writing.GetAwaiter().GetResult() : TooLate(state);
    }

    private const int Pending = 0, Writing = 1, Cancelled = 2;

    // Inside the lock, right before the write: it only begins while the deadline has not claimed the state.
    // stillValid (a kick's "still on the server"): checked here too, inside the lock, so nothing can change between it and
    // the write but the write itself.
    private static ConsoleWrite WriteOnce(object stdinLock, TimeSpan wait, Action write, StrongBox<int> state, Func<bool>? stillValid = null)
    {
        if (!Monitor.TryEnter(stdinLock, wait))
            return ConsoleWrite.NotSent;
        try
        {
            if (stillValid != null && !stillValid())
            {
                Interlocked.CompareExchange(ref state.Value, Cancelled, Pending);
                return ConsoleWrite.Dropped;
            }
            if (Interlocked.CompareExchange(ref state.Value, Writing, Pending) != Pending)
                return ConsoleWrite.NotSent;
            write();
            return ConsoleWrite.Sent;
        }
        finally
        {
            Monitor.Exit(stdinLock);
        }
    }

    // At the deadline: plain not sent only when the write had not begun, and then it never will.
    private static ConsoleWrite TooLate(StrongBox<int> state) =>
        Interlocked.CompareExchange(ref state.Value, Cancelled, Pending) == Pending ? ConsoleWrite.NotSent : ConsoleWrite.MaybeSent;

    /// <summary>The reply of a command whose server run ended (it stopped, crashed or was started again) before its turn
    /// came: nothing was written, so it can never reach the next run. "was not sent" makes it a failure too.</summary>
    public const string RunEnded = "The command was not sent: the server it was queued for has stopped.";

    /// <summary>
    /// The same for a run token: the window's own server process, or for a server it did not start (the tool was opened
    /// again), that server's start time. A foreign server counts as the same run only while it has the same start time and
    /// the window still has no process of its own, and the same process id when one could be read, so a command does not
    /// reach a server that was restarted since, also when Windows won't give its start time.
    /// </summary>
    public static bool SameRun(ServerRun queuedFor, ServerRun now) =>
        queuedFor.Process != null
            ? SameRun(queuedFor.Process, now.Process)
            : now.Process == null && queuedFor.ForeignStarted is { } started && now.ForeignStarted == started
              && queuedFor.ForeignProcessId == now.ForeignProcessId;

    /// <summary>
    /// The start time a run token keeps for a server the window did not start: null when no server was found, and
    /// DateTime.MinValue when one was found but its start time can't be read (a process Windows protects), so that server
    /// still matches itself from one command to the next.
    /// </summary>
    public static DateTime? ForeignStart(bool found, DateTime? started) => found ? started ?? DateTime.MinValue : null;

    /// <summary>What a command's turn finds: the same run, no server at all (now or when it was queued), or another run.</summary>
    public static RunCheck Check(ServerRun queuedFor, ServerRun now)
    {
        static bool None(ServerRun run) => run.Process == null && run.ForeignStarted == null;
        if (None(now) || None(queuedFor))
            return RunCheck.NoServer;
        return SameRun(queuedFor, now) ? RunCheck.Same : RunCheck.Ended;
    }

    /// <summary>
    /// True while a command queued for the run <paramref name="queuedFor"/> may still go out at its turn: the window still
    /// has that very server process and it has not exited. No process on either side is no run: a server the window did not
    /// start is matched by its start time instead (<see cref="SameRun(ServerRun, ServerRun)"/>). A process the window never
    /// started reads as ended.
    /// </summary>
    public static bool SameRun(System.Diagnostics.Process? queuedFor, System.Diagnostics.Process? now)
    {
        if (queuedFor == null || !ReferenceEquals(queuedFor, now))
            return false;
        try
        {
            return !now.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// A raw write in its turn. When the turn does not come within <paramref name="wait"/>, the write goes out at once
    /// instead and the queued turn is dropped; whichever claims first writes, so the line is never written twice.
    /// </summary>
    public async Task<bool> WriteWithin(Func<bool> write, TimeSpan wait)
    {
        var claimed = 0;
        var turn = Enqueue(() => Task.FromResult<object>(write()), () => Interlocked.CompareExchange(ref claimed, 1, 0) == 0);
        if (await Task.WhenAny(turn, Task.Delay(wait)).ConfigureAwait(false) == turn)
            return await turn.ConfigureAwait(false) is true;
        if (Interlocked.CompareExchange(ref claimed, 2, 0) == 0)
            return write();
        return await turn.ConfigureAwait(false) is true;
    }

    public Task<T?> Enqueue<T>(Func<Task<T>> send, Func<bool>? stillValid = null) where T : class
    {
        // The next command waits for this one, whatever happens to it.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (_lock)
        {
            previous = _tail;
            _tail = done.Task;
        }

        return Run(previous, send, stillValid, done);
    }

    private static async Task<T?> Run<T>(Task previous, Func<Task<T>> send, Func<bool>? stillValid, TaskCompletionSource done) where T : class
    {
        try
        {
            await previous;
            if (stillValid != null && !stillValid())
                return null;
            return await send();
        }
        finally
        {
            done.SetResult();
        }
    }
}
