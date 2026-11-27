namespace BannerlordCoopLauncher.Core.AD1259;

/// <summary>
/// Which Open Source set a PC or a server has. Current and Older need all three mods of that set; Mixed is any mix of the
/// two sets among the mods there. None: not a whole set and no mix (a missing or wrong mod has its own line).
/// </summary>
public enum OpenSourceGeneration
{
    None,
    Current,
    Older,
    Mixed
}

/// <summary>
/// The three Open Source mods of the AD1259 set come as a set: 2.0 from the Steam Workshop (the pins in AD1259Stack) or
/// the 1.x set AD1259 1.4.6 was made for (Armory 1.18, Saddlery 1.8, Weaponry 1.8, the Nexus files "FOR v1.2.x"). With
/// 2.0 about 50 AR_ and TV_ armour meshes AD1259 uses are gone, so some gear shows missing textures. Either set works,
/// but Coop refuses a join on any module version mismatch, so the host and every player need the same versions.
/// A mix is not refused: the server copies what the host's game loads, so it still plays when everyone has that mix.
/// The tools only say so.
/// </summary>
public static class OpenSourceSet
{
    public static readonly string[] Ids = ["OpenSourceArmory", "OpenSourceSaddlery", "OpenSourceWeaponry"];

    public const string PcOlder = "This PC has the Open Source 1.x set (Armory 1.18, Saddlery 1.8, Weaponry 1.8), the set the AD1259 team recommends for AD1259 1.4.6.";
    // No place: the 2.0 copies can be Workshop items or copies in the game's Modules folder.
    public const string PcCurrent = "This PC has the Open Source 2.0 set.";
    public const string PcMixed = "The Open Source mods on this PC are from two sets: {0}.";
    public const string ServerOlder = "The server has the Open Source 1.x set (Armory 1.18, Saddlery 1.8, Weaponry 1.8), the set the AD1259 team recommends for AD1259 1.4.6.";
    public const string ServerCurrent = "The server has the Open Source 2.0 set.";
    public const string ServerMixed = "The Open Source mods on the server are from two sets: {0}.";
    public const string MissingTextures = "With 2.0 some AD1259 gear can show missing textures. The AD1259 team recommends Armory 1.18, Saddlery 1.8 and Weaponry 1.8 from Nexus Mods (the files named FOR v1.2.x).";
    public const string OneSet = "Use all three from one set: 2.0 from the Steam Workshop, or Armory 1.18, Saddlery 1.8 and Weaponry 1.8 from Nexus Mods (the files named FOR v1.2.x).";
    public const string SameSet = "The host and every player must use the same set, or Coop refuses the join.";
    /// <summary>Added to a missing or wrong Open Source mod's line: the 1.x set is on Nexus Mods, not the Workshop.</summary>
    public const string OlderToo = "The 1.x set works too: Armory 1.18, Saddlery 1.8 and Weaponry 1.8 from Nexus Mods (the files named FOR v1.2.x).";

    public static bool IsOpenSource(string id) => Ids.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <param name="versionOf">The version of a mod id, or null when it is missing.</param>
    public static OpenSourceGeneration Of(Func<string, string?> versionOf)
    {
        var current = 0;
        var older = 0;
        foreach (var id in Ids)
        {
            var mod = AD1259Stack.Find(id)!;
            var version = versionOf(id);
            if (string.IsNullOrWhiteSpace(version))
                continue;
            if (AD1259Stack.SameVersion(mod.Version, version))
                current++;
            else if ((mod.AlsoAccepted ?? []).Any(accepted => AD1259Stack.SameVersion(accepted, version)))
                older++;
        }

        // An Armory alone is no set: only a whole set gets named.
        return current > 0 && older > 0 ? OpenSourceGeneration.Mixed
            : current == Ids.Length ? OpenSourceGeneration.Current
            : older == Ids.Length ? OpenSourceGeneration.Older
            : OpenSourceGeneration.None;
    }

    /// <summary>The set of a mod scan: only Ready mods count, a missing or wrong one has its own line.</summary>
    public static OpenSourceGeneration Of(IEnumerable<AD1259ModStatus> statuses)
    {
        var ready = statuses.Where(status => status.IsReady).ToList();
        return Of(id => ready.FirstOrDefault(status => status.Mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.FoundVersion);
    }

    /// <summary>The Client Launcher's line about its set, one sentence group per line, or null without one.</summary>
    /// <param name="withTextures">Adds the missing textures line for 2.0 (once per run, not on every refresh).</param>
    public static string? PcLine(IEnumerable<AD1259ModStatus> statuses, bool withTextures = true)
    {
        var list = statuses.ToList();
        return Line(Of(list), PcOlder, PcCurrent, PcMixed,
            id => list.FirstOrDefault(status => status.IsReady && status.Mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.FoundVersion, withTextures);
    }

    /// <summary>The Server Tool's line about the set its server has, or null without one.</summary>
    public static string? ServerLine(Func<string, string?> versionOf, bool withTextures = true) =>
        Line(Of(versionOf), ServerOlder, ServerCurrent, ServerMixed, versionOf, withTextures);

    // Each part on its own line, so the log and the window translate each known sentence group.
    private static string? Line(OpenSourceGeneration generation, string older, string current, string mixed, Func<string, string?> versionOf,
        bool withTextures) =>
        generation switch
        {
            OpenSourceGeneration.Older => older + "\n" + SameSet,
            OpenSourceGeneration.Current => current + (withTextures ? "\n" + MissingTextures : "") + "\n" + SameSet,
            OpenSourceGeneration.Mixed => string.Format(mixed, MixedList(versionOf)) + "\n" + OneSet + "\n" + SameSet,
            _ => null
        };

    // "Open Source Armory v1.18.0, Open Source Saddlery v2.0.0, Open Source Weaponry v2.0.1": the names stay English.
    private static string MixedList(Func<string, string?> versionOf) =>
        string.Join(", ", Ids.Select(id => (Id: id, Version: versionOf(id)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Version))
            .Select(item => AD1259Stack.Find(item.Id)!.Name + " " + item.Version));
}
