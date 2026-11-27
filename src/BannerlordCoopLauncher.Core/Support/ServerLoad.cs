using System.Globalization;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The "CPU 12% · RAM 3.4 GB" line on the server overview. A sample is taken every few
/// seconds on a worker thread (<see cref="ServerResourceMonitor"/> reads every process),
/// and <see cref="Line"/> gives the newest one.
/// </summary>
public sealed class ServerLoad
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private ServerResourceMonitor? _monitor;
    private string _root = "";
    private DateTime _lastSample = DateTime.MinValue;
    private bool _busy;
    private string _line = "";

    /// <summary>
    /// The newest line, or "" while the server is stopped or nothing was counted yet.
    /// Starts a new sample when the last one is older than <see cref="Interval"/>.
    /// </summary>
    /// <param name="serverRoot">The server folder (the one with engine\ in it).</param>
    /// <param name="rootProcessId">The process this tool started, when it did.</param>
    public string Line(bool running, string? serverRoot, int? rootProcessId)
    {
        lock (_gate)
        {
            if (!running || string.IsNullOrWhiteSpace(serverRoot))
            {
                _monitor?.Reset();
                _line = "";
                return "";
            }

            var monitor = _monitor;
            if (monitor == null || !serverRoot.Equals(_root, StringComparison.OrdinalIgnoreCase))
            {
                monitor = _monitor = new ServerResourceMonitor(serverRoot);
                _root = serverRoot;
                _line = "";
            }

            if (!_busy && DateTime.UtcNow - _lastSample >= Interval)
            {
                _busy = true;
                _lastSample = DateTime.UtcNow;
                _ = Task.Run(() =>
                {
                    string line;
                    try
                    {
                        line = Format(monitor.Sample(rootProcessId));
                    }
                    catch (Exception)
                    {
                        line = "";
                    }

                    lock (_gate)
                    {
                        _busy = false;
                        if (ReferenceEquals(monitor, _monitor))
                            _line = line;
                    }
                });
            }

            return _line;
        }
    }

    /// <summary>"CPU 12% · RAM 3.4 GB", or "" when no server process was found.</summary>
    public static string Format(ServerResources resources) =>
        resources.ProcessCount == 0
            ? ""
            : "CPU " + Math.Round(resources.CpuPercent).ToString("0", CultureInfo.CurrentCulture) + "% · RAM "
              + resources.MemoryGigabytes.ToString("0.0", CultureInfo.CurrentCulture) + " GB";
}
