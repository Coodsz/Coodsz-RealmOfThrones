// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), Server/CrashRestartPolicy.cs,
// and changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>What to do after the server stopped.</summary>
/// <param name="ShouldRestart">Start the server again?</param>
/// <param name="Delay">How long to wait before that start.</param>
/// <param name="Reason">Why, for the log line.</param>
public readonly record struct CrashDecision(bool ShouldRestart, TimeSpan Delay, CrashDecisionReason Reason);

public enum CrashDecisionReason
{
    /// <summary>An asked for or clean stop, not a crash.</summary>
    NotACrash,

    /// <summary>A restart does not fix this kind of exit (broken save, changed Coop module, terms).</summary>
    NotRecoverable,

    /// <summary>The server is started again after the delay.</summary>
    Restarting,

    /// <summary>It crashes too often; restarting stopped and the person hosting decides.</summary>
    CrashLoop,

    /// <summary>Restart after a crash is turned off.</summary>
    Disabled,
}

/// <summary>
/// Restart after a crash, with a growing wait and a crash loop guard.
/// </summary>
/// <remarks>
/// Without a wait and a guard, a server that crashes while it loads restarts forever
/// and keeps the PC busy. The wait starts at 10 seconds and doubles up to 5 minutes.
/// More than 3 crashes inside 10 minutes stop the restarts. Exits that a restart cannot
/// fix (see <see cref="ServerExit.IsAutoRestartable"/>) are never restarted.
/// </remarks>
public sealed class CrashRestartPolicy
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _multiplier;
    private readonly int _maxRestartsPerWindow;
    private readonly TimeSpan _window;
    private readonly List<DateTimeOffset> _recentCrashes = new();

    public CrashRestartPolicy(
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double multiplier = 2.0,
        int maxRestartsPerWindow = 3,
        TimeSpan? window = null)
    {
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(10);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        _multiplier = multiplier <= 1 ? 2.0 : multiplier;
        _maxRestartsPerWindow = Math.Max(1, maxRestartsPerWindow);
        _window = window ?? TimeSpan.FromMinutes(10);
    }

    /// <summary>Is restart after a crash on?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Crashes inside the window, as of the last decision.</summary>
    public int RecentCrashCount => _recentCrashes.Count;

    /// <summary>Decides what to do after an exit and updates the crash count.</summary>
    public CrashDecision Evaluate(ServerExitInfo exit, DateTimeOffset now)
    {
        if (!exit.IsCrash)
        {
            _recentCrashes.Clear();
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.NotACrash);
        }

        if (!ServerExit.IsAutoRestartable(exit.Kind))
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.NotRecoverable);

        if (!Enabled)
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.Disabled);

        _recentCrashes.RemoveAll(time => now - time > _window);
        _recentCrashes.Add(now);
        if (_recentCrashes.Count > _maxRestartsPerWindow)
            return new CrashDecision(false, TimeSpan.Zero, CrashDecisionReason.CrashLoop);

        // The first crash waits initialDelay; each one after it waits multiplier times longer.
        var steps = _recentCrashes.Count - 1;
        var seconds = _initialDelay.TotalSeconds * Math.Pow(_multiplier, steps);
        var delay = TimeSpan.FromSeconds(Math.Min(seconds, _maxDelay.TotalSeconds));
        return new CrashDecision(true, delay, CrashDecisionReason.Restarting);
    }

    /// <summary>Forgets earlier crashes, for example after the person hosting started the server by hand.</summary>
    public void Reset() => _recentCrashes.Clear();
}
