namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// The module rules Coop 0.1.6 uses when a player joins a dedicated server, as Coodsz Host and
/// Start-ModdedServer.ps1 rely on them:
///   - every DedicatedServer.* module is left out on both sides (server host modules and client
///     add-ons such as the join password module);
///   - the official TaleWorlds modules may differ (a server leaves out StoryMode and CustomBattle);
///   - every other (community) module has to be on both sides, so a mod whose code is switched off
///     on the server still stays in the server's list.
/// CoopNightly is the old name of Coop. Coop Save Rescue is DedicatedServer.CoopSaveRescue now; saves from an
/// older Rescue save still list its old id CoopSaveRescue, which stays left out too.
/// </summary>
public static class CoopJoinRules
{
    public static readonly IReadOnlyCollection<string> OfficialModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "BirthAndDeath", "Multiplayer", "NavalDLC"
    };

    public static bool IsIgnored(string moduleId) =>
        moduleId.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase)
        || OfficialModules.Contains(moduleId)
        || moduleId.Equals("CoopSaveRescue", StringComparison.OrdinalIgnoreCase);

    /// <summary>The community modules Coop compares, CoopNightly read as Coop, each once.</summary>
    public static IReadOnlyList<string> Compared(IEnumerable<string> modules)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in modules)
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0 || IsIgnored(id))
                continue;
            if (id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase))
                id = "Coop";
            if (seen.Add(id))
                list.Add(id);
        }

        return list;
    }

    public sealed record Difference(IReadOnlyList<string> OnlyFirst, IReadOnlyList<string> OnlySecond)
    {
        public bool None => OnlyFirst.Count == 0 && OnlySecond.Count == 0;
    }

    public static Difference Compare(IEnumerable<string> first, IEnumerable<string> second)
    {
        var a = Compared(first);
        var b = Compared(second);
        var onlyFirst = a.Where(id => !b.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var onlySecond = b.Where(id => !a.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        return new Difference(onlyFirst, onlySecond);
    }

    /// <summary>Null when a player launching <paramref name="client"/> can join a server loading
    /// <paramref name="server"/>; otherwise a plain sentence naming what is different.</summary>
    public static string? JoinProblem(IEnumerable<string> client, IEnumerable<string> server)
    {
        var difference = Compare(client, server);
        if (difference.None)
            return null;
        var parts = new List<string>();
        if (difference.OnlyFirst.Count > 0)
            parts.Add("Players load " + string.Join(", ", difference.OnlyFirst) + " but the server does not.");
        if (difference.OnlySecond.Count > 0)
            parts.Add("The server loads " + string.Join(", ", difference.OnlySecond) + " but players do not.");
        return string.Join(" ", parts) + " Coop turns that join down.";
    }

    /// <summary>Null when a save remembers the same community modules as the server list; otherwise a
    /// plain sentence. A save without a module list gives null (nothing to compare).</summary>
    public static string? SaveProblem(IReadOnlyList<string> saveModules, IEnumerable<string> server)
    {
        if (saveModules.Count == 0)
            return null;
        var difference = Compare(saveModules, server);
        if (difference.None)
            return null;
        var parts = new List<string>();
        if (difference.OnlyFirst.Count > 0)
            parts.Add("The save was made with " + string.Join(", ", difference.OnlyFirst) + ", and this setup does not load them.");
        if (difference.OnlySecond.Count > 0)
            parts.Add("This setup adds " + string.Join(", ", difference.OnlySecond) + ", and the save was made without them.");
        return string.Join(" ", parts);
    }
}
