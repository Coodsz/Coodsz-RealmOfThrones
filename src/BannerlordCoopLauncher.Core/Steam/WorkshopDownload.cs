namespace BannerlordCoopLauncher.Core.Steam;

public sealed record WorkshopDownloadResult(WorkshopSubscribeResult? Result, string? SteamProblem)
{
    /// <summary>Items Steam did not take: all of them when Steam could not be used.</summary>
    public IReadOnlyList<ulong> OpenByHand(IReadOnlyCollection<ulong> asked) =>
        Result == null ? asked.ToList() : Result.Failed.ToList();
}

/// <summary>
/// Subscribes to Workshop items and waits for Steam to download them, through Steam itself like
/// a mod manager does. Every Steam call runs on one thread. Used by the TAOM client launcher and
/// the TAOM server tool for Install mods.
/// </summary>
public static class WorkshopDownload
{
    public static async Task<WorkshopDownloadResult> RunAsync(
        string gameRoot,
        IReadOnlyDictionary<ulong, string> names,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        string? failure = null;
        WorkshopSubscribeResult? result = null;
        var ids = names.Keys.ToList();
        if (ids.Count == 0)
            return new WorkshopDownloadResult(null, null);
        await Task.Factory.StartNew(() =>
        {
            using var steam = SteamApiWorkshop.TryOpen(gameRoot, out var reason);
            if (steam == null)
            {
                failure = reason;
                return;
            }

            result = WorkshopSubscribe.Run(steam, ids, names, log, TimeSpan.FromMinutes(10), cancellationToken);
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return new WorkshopDownloadResult(result, failure);
    }
}
