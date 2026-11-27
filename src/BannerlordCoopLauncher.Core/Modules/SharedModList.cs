using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>One module of a shared mod list. CoopBuild is the exact Coop build (CoopBuildId) on the Coop entry of a
/// tool that needs one build; it is not written when null, and a list without it still reads.</summary>
public sealed record SharedModEntry(string Id, string Name, string Version, string WorkshopId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CoopBuild = null);

public sealed record SharedApplyResult(
    IReadOnlyList<string> Order,
    IReadOnlyList<string> Enabled,
    IReadOnlyList<string> TurnedOn,
    IReadOnlyList<string> TurnedOff,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> LeftOff);

public static class SharedModList
{
    public const string Kind = "CoodszModList";

    public const string ClientToServer = "From the client to the server";
    public const string ServerToClient = "From the server to the client";
    public const string Intro =
        "This uses a file named coodsz-mod-list.json. Save it anywhere. The Desktop is fine. Do not put it in the Bannerlord folders.";
    public const string SendToSomeone =
        "To send it to someone else, give them the file. They click Import mod list and pick it.";

    // The guide windows name the tools of the build they are in: Coodsz Coop Client Launcher with
    // Coodsz Coop Server Tool, and the AD1259 tools in the AD1259 build.
    public static string ClientStepsFor(string clientTool, string serverTool) =>
        $"1. In {clientTool}, click Share mod list and save the file.\n" +
        $"2. In {serverTool}, open Modules and click Import mod list, then pick that file.\n" +
        "3. Click Save module config.";

    public static string ServerStepsFor(string serverTool, string clientTool) =>
        $"1. In {serverTool}, click Share mod list and save the file.\n" +
        $"2. In {clientTool}, click Import mod list and pick that file.\n" +
        "3. Click Save mods.";

    /// <summary>The AD1259 client launcher has no mod list buttons, so the AD1259 server's file goes from one server tool to another.</summary>
    public const string ServerToServer = "From one server tool to another";

    public static string ServerToServerSteps(string serverTool) =>
        $"1. In {serverTool}, click Share mod list and save the file.\n" +
        $"2. On the other PC, open {serverTool}, open Modules and click Import mod list, then pick that file.\n" +
        "3. Click Save module config.";

    public static string ClientSetsItsOwnMods(string clientTool) =>
        $"Players do not need this file. {clientTool} sets the mods itself.";

    /// <summary>What Import mod list does with a locked setup in the client launcher: it makes a new setup for the list.</summary>
    public const string ExtraModsClient =
        "Extra mods are matched. The working mods stay where they are. A locked setup keeps its mods, so a new setup is made for the list.";

    /// <summary>What Import mod list does with a locked setup in the server tool: nothing, it says so.</summary>
    public const string ExtraModsServer =
        "Extra mods are matched. The working mods stay where they are. A locked setup keeps its mods. Pick or make an unlocked setup first.";

    public const string LockedImportRefused =
        "This setup is locked, so its mods stay as they are. Pick or make an unlocked setup first, then import the list.";

    public const string ImportAfterStop =
        "The server started, so the mod list stayed as it was. Import the list again after you stop the server.";

    public static void Write(string path, IReadOnlyList<SharedModEntry> modules, bool taom157Exact = false)
    {
        var file = new SharedModFile
        {
            Kind = Kind,
            Modules = modules.ToList(),
            Taom157Exact = taom157Exact ? true : null
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<SharedModEntry> Read(string json) => Read(json, out _);

    public static IReadOnlyList<SharedModEntry> Read(string json, out bool taom157Exact)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        taom157Exact = root.TryGetProperty("Taom157Exact", out var exact) && exact.ValueKind == JsonValueKind.True;
        if (!root.TryGetProperty("kind", out var kind) && !root.TryGetProperty("Kind", out kind))
            throw new InvalidOperationException("That file is not a Coodsz mod list.");
        if (!string.Equals(kind.GetString(), Kind, StringComparison.Ordinal))
            throw new InvalidOperationException("That file is not a Coodsz mod list.");
        if (!root.TryGetProperty("modules", out var modules) && !root.TryGetProperty("Modules", out modules))
            return Array.Empty<SharedModEntry>();
        if (modules.ValueKind != JsonValueKind.Array)
            return Array.Empty<SharedModEntry>();

        var list = new List<SharedModEntry>();
        foreach (var item in modules.EnumerateArray())
        {
            var id = Text(item, "id", "Id");
            if (id.Length == 0)
                continue;
            var build = Text(item, "coopBuild", "CoopBuild").Trim();
            list.Add(new SharedModEntry(id, Text(item, "name", "Name"), Text(item, "version", "Version"), Text(item, "workshopId", "WorkshopId"),
                build.Length == 0 ? null : build));
        }

        return list;
    }

    public static string? VersionWarning(string name, string listed, string installed)
    {
        if (string.IsNullOrWhiteSpace(listed) || string.IsNullOrWhiteSpace(installed))
            return null;
        var wanted = listed.Trim();
        var have = installed.Trim();
        if (wanted.Equals(have, StringComparison.OrdinalIgnoreCase))
            return null;
        var label = string.IsNullOrWhiteSpace(name) ? "A mod" : name.Trim();
        return label + " is " + have + " here. The shared list says " + wanted + ".";
    }

    /// <summary>
    /// The Workshop id to write for a mod: the stored one, else the number in its folder path
    /// (...\workshop\content\261550\<id>\...). A setup made with New setup does not store the ids, and
    /// without them a friend's import cannot download anything.
    /// </summary>
    public static string WorkshopIdFor(string? stored, string? directory)
    {
        if (IsWorkshopId(stored))
            return stored!.Trim();
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        var match = System.Text.RegularExpressions.Regex.Match(directory, @"[\\/]workshop[\\/]content[\\/]261550[\\/](\d+)(?:[\\/]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && IsWorkshopId(match.Groups[1].Value) ? match.Groups[1].Value : "";
    }

    /// <summary>Module id to Workshop id for every Bannerlord Workshop mod in these content folders.</summary>
    public static IReadOnlyDictionary<string, string> WorkshopIdsIn(IEnumerable<string> workshopContentRoots)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in workshopContentRoots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var item in Directory.EnumerateDirectories(root))
            {
                var workshopId = Path.GetFileName(item);
                if (!IsWorkshopId(workshopId))
                    continue;
                IEnumerable<string> xmls;
                try { xmls = Directory.EnumerateFiles(item, "SubModule.xml", SearchOption.AllDirectories).ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (var xml in xmls)
                {
                    try
                    {
                        if (Parsing.SubModuleXml.TryRead(File.ReadAllText(xml), out var info, out _) && info != null && !string.IsNullOrWhiteSpace(info.Id))
                            ids.TryAdd(info.Id, workshopId);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }

        return ids;
    }

    // A hidden or removed Workshop item is left out: Steam can not download it, so a
    // friend's import should not try.
    public static bool IsWorkshopId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id!.Length > 1 && id.All(char.IsDigit) && id != "0" && !RetiredWorkshopItems.IsRetired(id);

    /// <summary>One module slot: the same id, or Coop and CoopNightly, which are one Coop.</summary>
    public static bool SameSlot(string? id, string? other) =>
        string.Equals(id, other, StringComparison.OrdinalIgnoreCase)
        || (BannerlordCoopModules.IsEither(id) && BannerlordCoopModules.IsEither(other));

    /// <summary>The ids an import counts as already on this PC, with both Coop ids when either is: a host list naming the
    /// other id means this PC's own Coop, not a mod to add.</summary>
    public static HashSet<string> WithCoopAliases(IEnumerable<string> known)
    {
        var ids = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        if (ids.Any(BannerlordCoopModules.IsEither))
        {
            ids.Add(BannerlordCoopModules.Coop);
            ids.Add(BannerlordCoopModules.CoopNightly);
        }

        return ids;
    }

    public static SharedApplyResult ApplyExtras(
        IReadOnlyList<string> installedOrder,
        IReadOnlyCollection<string> currentlyEnabled,
        IReadOnlyList<string> sharedIds,
        IReadOnlyList<string> working,
        IReadOnlyCollection<string> stayOff,
        Func<string, bool>? canToggleWorking = null)
    {
        var installed = new HashSet<string>(installedOrder, StringComparer.OrdinalIgnoreCase);
        var enabledNow = new HashSet<string>(currentlyEnabled, StringComparer.OrdinalIgnoreCase);
        var workingSet = new HashSet<string>(working, StringComparer.OrdinalIgnoreCase);
        var off = new HashSet<string>(stayOff, StringComparer.OrdinalIgnoreCase);
        var coopIsWorking = working.Any(BannerlordCoopModules.IsEither);
        var wanted = new List<string>();
        var missing = new List<string>();
        var leftOff = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Coop and CoopNightly are one Coop slot: a list naming the other id (a host on the Coop nightly) means this PC's
        // own Coop row, never a missing mod that turns that row off.
        var coopRow = installedOrder.FirstOrDefault(BannerlordCoopModules.IsEither);
        foreach (var listed in sharedIds)
        {
            var id = coopRow != null && BannerlordCoopModules.IsEither(listed) ? coopRow : listed;
            if (!seen.Add(id))
                continue;
            if (off.Contains(id))
            {
                leftOff.Add(id);
                continue;
            }

            if (IsWorkingSlot(id, workingSet, coopIsWorking))
                continue;

            if (!installed.Contains(id))
                missing.Add(id);
            else
                wanted.Add(id);
        }

        var wantedSet = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        var tail = wanted.Concat(installedOrder.Where(id => !workingSet.Contains(id) && !wantedSet.Contains(id) && !(coopIsWorking && BannerlordCoopModules.IsEither(id)))).ToList();
        var order = WorkingLoadOrder.PlaceExtrasBelow(working.Where(installed.Contains).Concat(tail).ToList(), working);
        var enabled = new HashSet<string>(enabledNow, StringComparer.OrdinalIgnoreCase);
        var turnedOn = new List<string>();
        var turnedOff = new List<string>();
        foreach (var id in installedOrder)
        {
            var workingSlot = IsWorkingSlot(id, workingSet, coopIsWorking);
            if (workingSlot && canToggleWorking?.Invoke(id) != true)
                continue;
            var shouldEnable = workingSlot ? seen.Contains(id) && !off.Contains(id) : wantedSet.Contains(id);
            if (shouldEnable)
            {
                if (enabled.Add(id))
                    turnedOn.Add(id);
            }
            else if (enabled.Remove(id))
                turnedOff.Add(id);
        }

        foreach (var id in off)
            enabled.Remove(id);
        return new SharedApplyResult(order, enabled.ToList(), turnedOn, turnedOff, missing, leftOff);
    }

    private static bool IsWorkingSlot(string id, HashSet<string> working, bool coopIsWorking) =>
        working.Contains(id) || (coopIsWorking && BannerlordCoopModules.IsEither(id));

    private static string Text(JsonElement item, string camel, string pascal)
    {
        if (item.TryGetProperty(camel, out var value) || item.TryGetProperty(pascal, out value))
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        return "";
    }

    private sealed class SharedModFile
    {
        public string Kind { get; set; } = SharedModList.Kind;
        public List<SharedModEntry> Modules { get; set; } = new();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Taom157Exact { get; set; }
    }
}
