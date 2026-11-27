namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>Which part of a tool's list a module is, as the calling tool decides: its pinned core,
/// a library mod the host's install sets the version of, or an extra.</summary>
public enum SharedModPart
{
    Core,
    Library,
    Extra
}

public enum SharedModProblemKind
{
    /// <summary>On the host's list, not on this PC.</summary>
    Missing,
    /// <summary>On both, with another version here.</summary>
    Different,
    /// <summary>Loads on this PC, not on the host's list.</summary>
    NotOnHost
}

/// <summary>One difference between the host's list and this PC, with the line a launcher shows.</summary>
public sealed record SharedModProblem(SharedModProblemKind Kind, SharedModPart Part, string ModuleId, string Name, string HostVersion, string ThisPcVersion, string Text);

/// <summary>
/// The host's coodsz-mod-list.json against this PC, for the tools whose Coop server turns a join down
/// on any difference in module ids or versions (the Coop nightly compares both ways). The caller says
/// which part each module id is and which ids to leave out (client add-ons, modules Coop ignores).
/// </summary>
public static class SharedModVersions
{
    /// <summary>
    /// The modules of the host's list that are missing on this PC or have another version here, in the
    /// list's order. installed gives each module id on this PC with its version; ids are matched without
    /// case and the first of two that differ only in case wins. A version the list or this PC leaves
    /// empty is not compared.
    /// </summary>
    public static IReadOnlyList<SharedModProblem> AgainstHost(
        IReadOnlyList<SharedModEntry> host,
        IEnumerable<KeyValuePair<string, string>> installed,
        Func<string, SharedModPart> partOf,
        Func<string, bool>? skip = null)
    {
        var have = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, version) in installed)
        {
            if (!string.IsNullOrWhiteSpace(id))
                have.TryAdd(id.Trim(), version ?? "");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<SharedModProblem>();
        foreach (var entry in host)
        {
            var id = (entry.Id ?? "").Trim();
            if (id.Length == 0 || skip?.Invoke(id) == true || !seen.Add(id))
                continue;
            var name = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim();
            var wanted = (entry.Version ?? "").Trim();
            if (!have.TryGetValue(id, out var version))
            {
                problems.Add(new SharedModProblem(SharedModProblemKind.Missing, partOf(id), id, name, wanted, "",
                    name + " is on the host's list but not on this PC."));
                continue;
            }

            // Both sides read the version from the same SubModule.xml text, and the Coop nightly compares it
            // as written as far as we know, so "v2.4.2.248" and "2.4.2.248" differ. Only spaces and case are
            // ignored (VersionWarning).
            var text = SharedModList.VersionWarning(name, wanted, version);
            if (text != null)
                problems.Add(new SharedModProblem(SharedModProblemKind.Different, partOf(id), id, name, wanted, version.Trim(), text));
        }

        return problems;
    }

    /// <summary>The modules this PC loads (active, in launch order) that the host's list does not have.
    /// name gives a module's name when it is known.</summary>
    public static IReadOnlyList<SharedModProblem> NotOnHost(
        IReadOnlyList<SharedModEntry> host,
        IEnumerable<string> active,
        Func<string, SharedModPart> partOf,
        Func<string, bool>? skip = null,
        Func<string, string?>? name = null)
    {
        var listed = new HashSet<string>(host.Select(entry => (entry.Id ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<SharedModProblem>();
        foreach (var raw in active)
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0 || skip?.Invoke(id) == true || listed.Contains(id) || !seen.Add(id))
                continue;
            var label = name?.Invoke(id) is { Length: > 0 } known ? known.Trim() : id;
            problems.Add(new SharedModProblem(SharedModProblemKind.NotOnHost, partOf(id), id, label, "", "",
                label + " is on this setup but not on the host's list."));
        }

        return problems;
    }
}
