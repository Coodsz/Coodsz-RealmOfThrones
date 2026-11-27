namespace BannerlordCoopLauncher.Core.Saves;

/// <summary>
/// When the backup after a "world saved" line is taken, per save. The server writes the .sav in
/// place, and Restart saves twice a few seconds apart (Save game, then the save on stop), so a
/// backup taken a fixed time after the first line read the second save half written. A line that
/// comes while a backup of that save still waits moves the backup to after the newest save instead
/// of being dropped, and a line that comes after the backup was taken gets a backup of its own.
/// </summary>
/// <remarks>
/// The same line comes twice in Coodsz Host (the server output and the coop-server log, a moment
/// apart). The second one finds the backup still waiting, so it makes no second zip.
/// </remarks>
public sealed class AutoBackupSchedule
{
    /// <summary>How long after the newest line the backup waits. Coop writes the .json right after the .sav.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>Tries for one backup. A refused try waits <see cref="RetryDelay"/>; a 10 MB save takes about 2 seconds to write.</summary>
    public const int Attempts = 5;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, Plan> _plans = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Plan
    {
        public DateTime LastLineUtc;
        public BackupReason Reason;
        public bool Waiting;
        public bool Refused;
    }

    /// <summary>
    /// Notes a "world saved" line. True when the caller starts a backup run for the save; false when
    /// a run of this save is already waiting, which now waits for this newer save instead.
    /// </summary>
    public bool Saved(string name, BackupReason reason, DateTime nowUtc)
    {
        lock (_plans)
        {
            if (_plans.TryGetValue(name, out var plan) && plan.Waiting)
            {
                plan.LastLineUtc = nowUtc;
                plan.Reason = reason;
                return false;
            }

            // The copy of a line whose backup was refused.
            if (plan is { Refused: true } && nowUtc - plan.LastLineUtc < Settle)
                return false;

            _plans[name] = new Plan { LastLineUtc = nowUtc, Reason = reason, Waiting = true };
            return true;
        }
    }

    /// <summary>How much longer the run waits before it takes the backup. Zero means now.</summary>
    public TimeSpan Wait(string name, DateTime nowUtc)
    {
        lock (_plans)
        {
            if (!_plans.TryGetValue(name, out var plan))
                return TimeSpan.Zero;
            var left = plan.LastLineUtc + Settle - nowUtc;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>The reason of the newest line: a backup after the save on stop is named for it.</summary>
    public BackupReason Reason(string name)
    {
        lock (_plans)
            return _plans.TryGetValue(name, out var plan) ? plan.Reason : BackupReason.Autosave;
    }

    /// <summary>
    /// Ends the run whose backup try started at <paramref name="startedUtc"/>. False when a newer
    /// save came in since: the run stays and goes again for that save.
    /// </summary>
    public bool Done(string name, DateTime startedUtc)
    {
        lock (_plans)
        {
            if (!_plans.TryGetValue(name, out var plan))
                return true;
            if (plan.LastLineUtc >= startedUtc)
                return false;
            plan.Waiting = false;
            return true;
        }
    }

    /// <summary>Ends the run without a backup (the world is refused). A copy of the same line does not start a new run.</summary>
    public void Cancel(string name)
    {
        lock (_plans)
        {
            if (_plans.TryGetValue(name, out var plan))
            {
                plan.Waiting = false;
                plan.Refused = true;
            }
        }
    }
}
