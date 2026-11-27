using BannerlordCoopLauncher.Core.Parsing;

namespace TaomCoop.Core;

/// <summary>
/// Find the crashing mod: halves the suspected extra mods each round. A round starts the game to
/// the main menu with the TAOM core and only the mods under test (plus the extras they need, so a
/// mod is never tested without its dependency). If that crashes, the culprit is among them;
/// otherwise it is among the rest. It assumes one mod causes the crash. The player's setup is never
/// changed; each round runs a copy.
/// </summary>
public sealed class CrashBisect
{
    private readonly TaomSetup _original;
    private readonly IReadOnlyDictionary<string, ModuleManifest> _manifests;
    private List<string> _suspects;

    private CrashBisect(TaomSetup original, IReadOnlyList<string> suspects, IReadOnlyDictionary<string, ModuleManifest> manifests)
    {
        _original = original.Clone();
        _suspects = suspects.ToList();
        _manifests = manifests;
        Plan();
    }

    /// <summary>Starts a search. <paramref name="order"/> gives the client load order the halves follow; the TAOM order
    /// when left out (the Burning Empires launcher passes its own).</summary>
    public static CrashBisect Start(TaomSetup setup, IReadOnlyDictionary<string, ModuleManifest>? manifests = null, Func<TaomSetup, IReadOnlyList<string>>? order = null)
    {
        var suspects = (order ?? TaomLoadOrder.ClientOrder)(setup).Where(id => setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (suspects.Count == 0)
            throw new InvalidOperationException(setup.Name + " has no extra mods turned on, so there is nothing to test.");
        return new CrashBisect(setup, suspects, manifests ?? new Dictionary<string, ModuleManifest>());
    }

    public int Round { get; private set; }

    public IReadOnlyList<string> Suspects => _suspects;

    /// <summary>The suspects under test this round.</summary>
    public IReadOnlyList<string> Testing { get; private set; } = Array.Empty<string>();

    /// <summary>Everything turned on this round: the mods under test and the extras they need.</summary>
    public IReadOnlyList<string> TurnedOn { get; private set; } = Array.Empty<string>();

    public bool Done => _suspects.Count <= 1;

    /// <summary>The mod that crashes the game, once the search is done.</summary>
    public string? Found => _suspects.Count == 1 ? _suspects[0] : null;

    /// <summary>A copy of the setup with only this round's mods turned on.</summary>
    public TaomSetup TestSetup()
    {
        var copy = _original.Clone();
        copy.Locked = false;
        foreach (var mod in copy.Extras)
            mod.Enabled = TurnedOn.Contains(mod.ModuleId, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    /// <summary>The result of this round: true when the game crashed before the main menu.</summary>
    public void Report(bool crashed)
    {
        if (Done)
            return;
        var tested = new HashSet<string>(Testing, StringComparer.OrdinalIgnoreCase);
        _suspects = crashed
            ? _suspects.Where(tested.Contains).ToList()
            : _suspects.Where(id => !tested.Contains(id)).ToList();
        Plan();
    }

    private void Plan()
    {
        if (Done)
        {
            Testing = _suspects.ToList();
            TurnedOn = WithNeeds(Testing);
            return;
        }

        Round++;
        Testing = _suspects.Take((_suspects.Count + 1) / 2).ToList();
        TurnedOn = WithNeeds(Testing);
    }

    private List<string> WithNeeds(IEnumerable<string> start)
    {
        var extras = new HashSet<string>(_original.Extras.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var on = new List<string>();
        var queue = new Queue<string>(start);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (on.Contains(id, StringComparer.OrdinalIgnoreCase))
                continue;
            on.Add(id);
            if (!_manifests.TryGetValue(id, out var manifest))
                continue;
            foreach (var need in manifest.RequiredIds.Where(extras.Contains))
                queue.Enqueue(need);
        }

        return on;
    }
}

/// <summary>
/// What a player last joined a host with. A host's campaign save remembers its mods, so joining
/// the same host with other mods can be turned down or can harm that campaign.
/// </summary>
public static class JoinMemory
{
    public static string Key(bool steam, string steamName, string address, string port) =>
        steam ? "steam:" + steamName.Trim().ToLowerInvariant() : "direct:" + address.Trim().ToLowerInvariant() + ":" + port.Trim();

    /// <summary>Null when the mods match the last join (or there was none); otherwise a plain warning.</summary>
    public static string? Problem(IReadOnlyList<string>? lastJoin, IReadOnlyList<string> now)
    {
        if (lastJoin == null || lastJoin.Count == 0)
            return null;
        var difference = BannerlordCoopLauncher.Core.Modules.CoopJoinRules.Compare(lastJoin, now);
        if (difference.None)
            return null;
        var parts = new List<string>();
        if (difference.OnlySecond.Count > 0)
            parts.Add("This time you add " + string.Join(", ", difference.OnlySecond) + ".");
        if (difference.OnlyFirst.Count > 0)
            parts.Add("This time you leave out " + string.Join(", ", difference.OnlyFirst) + ".");
        return "Last time you joined this host with other mods. " + string.Join(" ", parts)
            + " The host's campaign remembers its mods, so the server can turn the join down. Pick the setup you joined with before, or ask the host.";
    }
}
