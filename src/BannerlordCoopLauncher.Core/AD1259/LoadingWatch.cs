namespace BannerlordCoopLauncher.Core.AD1259;

/// <summary>One look at the starting game: how long it has run, its CPU time and its log sizes.</summary>
public readonly record struct LoadingSample(TimeSpan Elapsed, TimeSpan CpuTime, long EngineLogBytes, long CoopLogBytes);

public sealed record LoadingReport(string Title, string Detail, bool Stalled);

/// <summary>
/// Tells a player whether a starting game is still working. The first AD1259 start builds shaders and
/// downloads a large world; the window often looks frozen, so players close a game that was fine.
/// Working means the game used CPU or wrote to its engine or Coop log since the last look.
/// </summary>
public sealed class LoadingWatch
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(3);

    // Less than this much CPU between two looks two seconds apart is an idle process.
    private static readonly TimeSpan BusyCpu = TimeSpan.FromMilliseconds(200);

    private LoadingSample? _last;
    private TimeSpan _lastActivity;
    private TimeSpan _lastLogWrite = TimeSpan.MinValue;

    public LoadingReport Update(LoadingSample sample, string coopLog)
    {
        var busy = false;
        if (_last is { } last)
        {
            busy = sample.CpuTime - last.CpuTime >= BusyCpu;
            if (sample.EngineLogBytes != last.EngineLogBytes || sample.CoopLogBytes != last.CoopLogBytes)
                _lastLogWrite = sample.Elapsed;
        }
        else
        {
            busy = true;
            _lastLogWrite = sample.Elapsed;
        }

        if (busy || _lastLogWrite == sample.Elapsed)
            _lastActivity = sample.Elapsed;
        _last = sample;

        var idle = sample.Elapsed - _lastActivity;
        var stage = Stage(coopLog);
        if (idle >= StallAfter)
        {
            return new LoadingReport(
                "Bannerlord has not done anything for " + (int)idle.TotalMinutes + " minutes",
                stage + ". It is not using the CPU and its logs have stopped. If it stays like this for 5 more minutes, close the game and click Send my logs.",
                Stalled: true);
        }

        var signs = new List<string>();
        if (busy)
            signs.Add("using the CPU");
        if (_lastLogWrite != TimeSpan.MinValue)
        {
            var ago = (int)(sample.Elapsed - _lastLogWrite).TotalSeconds;
            signs.Add(ago <= 2 ? "writing its log now" : "last wrote its log " + ago + " s ago");
        }

        return new LoadingReport(
            "Bannerlord is still loading · " + Clock(sample.Elapsed),
            stage + ". Still working: " + string.Join(", ", signs) + ".",
            Stalled: false);
    }

    /// <summary>
    /// The game reached character creation or the campaign map, so loading is over. Takes the Coop log
    /// plus the engine log's "TopScreen:" lines; on some PCs Coop's own log stays empty.
    /// </summary>
    public static bool InGame(string coopLog) =>
        coopLog.Contains("Game State is changing to MapState", StringComparison.Ordinal)
        || coopLog.Contains("Game State is changing to CharacterCreationState", StringComparison.Ordinal)
        || coopLog.Contains("TopScreen: MapScreen", StringComparison.Ordinal)
        || coopLog.Contains("TopScreen: CharacterCreation", StringComparison.Ordinal);

    /// <summary>Keeps only the engine log lines that name the current screen.</summary>
    public static string ScreenLines(string engineLog) =>
        string.Join("\n", engineLog.Split('\n').Where(line => line.Contains("TopScreen:", StringComparison.Ordinal)).Select(line => line.Trim()));

    public static string Stage(string coopLog)
    {
        if (coopLog.Contains("Game State is changing to GameLoadingState", StringComparison.Ordinal))
            return "Loading the server's world";
        if (coopLog.Contains("Received host save transfer", StringComparison.Ordinal))
            return "Loading the server's world";
        if (coopLog.Contains("Receiving host save transfer", StringComparison.Ordinal))
            return "Downloading the server's world";
        if (coopLog.Contains("Connected! Please wait for transfer", StringComparison.OrdinalIgnoreCase))
            return "Connected. Waiting for the server to send its world";
        if (coopLog.Contains("TopScreen: CoopConnectionUI", StringComparison.Ordinal))
            return "Connecting to the server and receiving its world";
        if (coopLog.Length > 0)
            return "Connecting to the server";
        return "Starting Bannerlord and loading the mods (the first start also builds shaders here)";
    }

    public static string Clock(TimeSpan elapsed) =>
        (int)elapsed.TotalMinutes + ":" + elapsed.Seconds.ToString("00");
}
