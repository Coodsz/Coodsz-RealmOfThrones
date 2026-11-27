using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>
/// A module id that more than one Steam Workshop item carries. The game loads only one of
/// them. A re-upload can claim the official version and still be a different build, so a
/// copy with the same id and version is not always harmless: a player subscribed to a
/// second Harmony, ButterLib and UIExtenderEx crashed at every start with nothing in the
/// log naming why (Coodsz-Crash-Reports#31).
/// </summary>
public sealed record WorkshopTwin(string ModuleId, string Name, IReadOnlyList<string> ItemIds, string? KeepId, string? LoadedId)
{
    public IReadOnlyList<string> Unsubscribe => KeepId == null ? [] : ItemIds.Where(id => id != KeepId).ToList();
}

public static class WorkshopTwins
{
    // The BUTR items every setup starts with. MCM's official item was removed from the Workshop
    // (RetiredWorkshopItems); setups get MCM from Nexus Mods now.
    public static readonly IReadOnlyDictionary<string, string> Official = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Bannerlord.Harmony"] = "2859188632",
        ["Bannerlord.ButterLib"] = "2859232415",
        ["Bannerlord.UIExtenderEx"] = "2859222409",
    };

    public const string CrashLead = "A mod on this setup is in more than one Steam Workshop item, and that alone can crash the game at startup.";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, (string Stamp, List<(string ItemId, SubModuleInfo Info)> Items)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The twins among <paramref name="moduleIds"/>. <paramref name="preferred"/> names the
    /// item to keep per module id when the tool knows it; the official BUTR items are known
    /// anyway. <paramref name="gameLog"/>, an engine log of a client start, tells which item
    /// the game loaded; <paramref name="fallbackLog"/> is asked when it does not say (a crash
    /// capture keeps only the end of the log, and the loading lines are near the top). Both
    /// are only read when there is a twin.
    /// </summary>
    public static IReadOnlyList<WorkshopTwin> Find(
        IEnumerable<string> workshopContentDirectories,
        IEnumerable<string> moduleIds,
        IReadOnlyDictionary<string, string>? preferred = null,
        Func<string?>? gameLog = null,
        Func<string?>? fallbackLog = null)
    {
        var wanted = new HashSet<string>(moduleIds, StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, List<(string ItemId, SubModuleInfo Info)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in workshopContentDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var item in Scan(content).Where(item => wanted.Contains(item.Info.Id)))
            {
                if (!byId.TryGetValue(item.Info.Id, out var list))
                    byId[item.Info.Id] = list = new();
                if (!list.Any(seen => seen.ItemId == item.ItemId))
                    list.Add(item);
            }
        }

        var twins = new List<WorkshopTwin>();
        var main = new Lazy<string?>(() => gameLog?.Invoke());
        var fallback = new Lazy<string?>(() => fallbackLog?.Invoke());
        foreach (var (moduleId, items) in byId.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ids = items.Select(item => item.ItemId).OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal).ToList();
            // The official BUTR item wins over the one the tool happened to pick, which can be the re-upload.
            var official = Official.GetValueOrDefault(moduleId);
            var picked = preferred?.GetValueOrDefault(moduleId);
            var keep = official != null && ids.Contains(official) ? official
                : picked != null && ids.Contains(picked) ? picked
                : null;
            var name = items.Select(item => item.Info.Name).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? moduleId;
            twins.Add(new WorkshopTwin(moduleId, name, ids, keep, LoadedItem(main.Value, ids) ?? LoadedItem(fallback.Value, ids)));
        }

        return twins;
    }

    /// <summary>The one item of <paramref name="itemIds"/> the game loaded code from, or null when the log does not say.</summary>
    public static string? LoadedItem(string? gameLog, IReadOnlyList<string> itemIds)
    {
        if (string.IsNullOrEmpty(gameLog))
            return null;
        var log = gameLog.Replace('/', '\\');
        var loaded = itemIds.Where(id => log.Contains(@"261550\" + id + @"\bin\", StringComparison.OrdinalIgnoreCase)).ToList();
        return loaded.Count == 1 ? loaded[0] : null;
    }

    // The mods load in the first seconds, so the start of a log is all that is needed.
    private const int HeadBytes = 2_000_000;
    private static readonly Dictionary<string, (long Read, string? Text)> HeadCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The start of the newest engine log of a game start (not a dedicated server), or null.
    /// A running game's log keeps growing, so only its first part is read, and only again
    /// while that part is still filling.
    /// </summary>
    public static string? LatestClientLog(string engineLogDirectory)
    {
        try
        {
            if (!Directory.Exists(engineLogDirectory))
                return null;
            foreach (var file in Directory.GetFiles(engineLogDirectory, "rgl_log_*.txt")
                         .Where(path => !Path.GetFileName(path).StartsWith("rgl_log_errors", StringComparison.OrdinalIgnoreCase))
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(info => info.LastWriteTimeUtc)
                         .Take(6))
            {
                var head = Head(file);
                if (head != null)
                    return head;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    // The first part of a client log, or null for a server log or one with no command line yet.
    private static string? Head(FileInfo file)
    {
        var size = Math.Min(file.Length, HeadBytes);
        lock (Gate)
        {
            if (HeadCache.TryGetValue(file.FullName, out var cached) && cached.Read == size)
                return cached.Text;
        }

        var buffer = new char[size];
        int read;
        using (var reader = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
            read = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, read);
        var args = text.Split('\n').FirstOrDefault(line => line.Contains("Command Args:", StringComparison.OrdinalIgnoreCase)) ?? "";
        // A dedicated server on this PC writes rgl logs here too; it loads DedicatedServer.Windows.
        var client = args.Contains("/singleplayer", StringComparison.OrdinalIgnoreCase)
            && !args.Contains("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase);
        var result = client ? text : null;
        lock (Gate)
        {
            if (HeadCache.Count > 32)
                HeadCache.Clear();
            HeadCache[file.FullName] = (size, result);
        }

        return result;
    }

    public static string Link(string itemId) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + itemId;

    public static string Describe(WorkshopTwin twin)
    {
        var line = twin.Name + " is in more than one Steam Workshop item (" + string.Join(", ", twin.ItemIds) + ").";
        line += twin.LoadedId == null
            ? " The game loads only one of them and can pick either."
            : " The game loaded " + twin.LoadedId + " in the last game start.";
        if (twin.KeepId == null)
            line += " Keep the one you use, unsubscribe the others on the Workshop, then click Check mods.";
        else if (Official.GetValueOrDefault(twin.ModuleId) == twin.KeepId)
            line += " Unsubscribe " + string.Join(", ", twin.Unsubscribe.Select(Link)) + " on the Workshop and keep " + Link(twin.KeepId) + ", the official one, then click Check mods.";
        else
            line += " Unsubscribe " + string.Join(", ", twin.Unsubscribe.Select(Link)) + " on the Workshop and keep " + Link(twin.KeepId) + ", the one this tool uses, then click Check mods.";
        return line;
    }

    /// <summary>A startup crash explanation with the twins put first, since a twin explains a crash no extra mod is named in.</summary>
    public static IReadOnlyList<string> PointCrashAt(IReadOnlyList<string> explanation, IReadOnlyList<WorkshopTwin> twins)
    {
        if (twins.Count == 0 || explanation.Count == 0 || explanation[0].Contains("did not see a startup crash", StringComparison.OrdinalIgnoreCase))
            return explanation;
        return new[] { CrashLead }.Concat(twins.Select(Describe)).Concat(explanation).ToList();
    }

    private static IReadOnlyList<(string ItemId, SubModuleInfo Info)> Scan(string content)
    {
        try
        {
            if (!Directory.Exists(content))
                return [];
            var folders = Directory.GetDirectories(content)
                .Where(folder => Path.GetFileName(folder).All(char.IsDigit))
                .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Steam makes an item folder before it fills it, so each SubModule.xml's own time is in the stamp.
            var stamp = Directory.GetLastWriteTimeUtc(content).Ticks + "|" + string.Join("|", folders.Select(folder =>
                Path.GetFileName(folder) + ":" + File.GetLastWriteTimeUtc(Path.Combine(folder, "SubModule.xml")).Ticks));
            lock (Gate)
            {
                if (Cache.TryGetValue(content, out var cached) && cached.Stamp == stamp)
                    return cached.Items;
            }

            var items = new List<(string ItemId, SubModuleInfo Info)>();
            foreach (var folder in folders)
            {
                try
                {
                    var path = Path.Combine(folder, "SubModule.xml");
                    if (File.Exists(path) && SubModuleXml.TryRead(File.ReadAllText(path), out var info, out _) && info != null)
                        items.Add((Path.GetFileName(folder), info));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            lock (Gate)
                Cache[content] = (stamp, items);
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
