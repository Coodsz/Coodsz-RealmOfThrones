using System.Collections.Concurrent;
using System.Diagnostics;

namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>
/// Server console lines on their way to the window. The output reader only adds (it never waits for
/// the window, or the pipe fills and the server's game thread stalls on its own console writes), and
/// the window drains them on a timer. Every line is handled in order; a line is only drawn when fewer
/// than <c>keep</c> lines are still waiting behind it, because the log box keeps the last 400 anyway.
/// </summary>
public sealed class ServerLineQueue<T>
{
    private readonly ConcurrentQueue<T> _items = new();

    public void Add(T item) => _items.Enqueue(item);

    public bool IsEmpty => _items.IsEmpty;

    public int Count => _items.Count;

    /// <summary>
    /// Handles lines oldest first until the queue is empty or <paramref name="budget"/> has passed, so the
    /// window still gets frames during a long burst; the rest waits for the next round. The handler gets
    /// true when the line should be drawn. Returns how many lines were handled.
    /// </summary>
    public int Drain(Action<T, bool> handle, int keep, TimeSpan budget)
    {
        var watch = Stopwatch.StartNew();
        var handled = 0;
        while (_items.TryDequeue(out var item))
        {
            handle(item, Drawn(_items.Count, keep));
            handled++;
            if (watch.Elapsed >= budget)
                break;
        }

        return handled;
    }

    /// <summary>A line is drawn when fewer than <paramref name="keep"/> lines come after it.</summary>
    public static bool Drawn(int linesAfter, int keep) => linesAfter < keep;
}
