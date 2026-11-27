using BannerlordCoopLauncher.Core.IO;
namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>The small badge a mod row shows: Kind is Works, Broken, ClientOnly or Unknown.</summary>
public sealed record ModCompatBadge(string Kind, string Text, string Tip)
{
    /// <summary>The green count chip next to the badge, like 12 say works, or null when nobody said so.</summary>
    public string? WorksChip { get; init; }

    /// <summary>The red count chip next to the badge, like 2 say broken, or null when nobody said so.</summary>
    public string? BrokenChip { get; init; }

    /// <summary>The ModderLords verdict chip (Tested: works, Works with settings, Tested: broken), or null when his records have none.</summary>
    public CompatTestedChip? Tested { get; init; }

    /// <summary>The same badge in the current language, for rows that bind it (a window cannot
    /// translate bound text).</summary>
    public ModCompatBadge Translated() => this with
    {
        Text = Localization.Translator.T(Text),
        Tip = string.Join("\n", Tip.Split('\n').Select(Localization.Translator.T)),
        WorksChip = WorksChip == null ? null : Localization.Translator.T(WorksChip),
        BrokenChip = BrokenChip == null ? null : Localization.Translator.T(BrokenChip),
        Tested = Tested?.Translated()
    };
}

/// <summary>
/// The community mod list as the windows use it: one copy per tool run (the downloaded one
/// when it arrives, the kept or built in one until then), the badge for a mod, and the
/// prefilled GitHub issue form for "Report how this mod worked".
/// </summary>
public static class CommunityCompat
{
    /// <summary>The last tip line of a list badge: the click opens the GitHub report form in the browser.</summary>
    public const string ReportTip = "Click to report how this mod worked. Opens the report page in your browser.";

    /// <summary>Shared by every Coodsz tool, next to the language choice.</summary>
    public static string CachePath { get; set; } =
        Path.Combine(ToolData.RoamingRoot, "mod-compat.json");

    private static readonly object Gate = new();
    private static ModCompatDatabase? _current;
    private static Task? _refresh;

    /// <summary>Raised (on a worker thread) when a downloaded list replaced the kept one.</summary>
    public static event Action? Changed;

    public static ModCompatDatabase Current
    {
        get
        {
            lock (Gate)
                return _current ??= ModCompatDatabase.LoadCached(CachePath);
        }
    }

    /// <summary>Downloads the list once per run. Offline it keeps the kept or built in copy. ModderLords'
    /// compat records come down next to it, and Changed is raised for them too.</summary>
    public static Task RefreshAsync(string userAgent)
    {
        lock (Gate)
            return _refresh ??= Task.Run(async () =>
            {
                _ = CompatRecords.RefreshAsync(userAgent).ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result)
                        Changed?.Invoke();
                }, TaskScheduler.Default);
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                    var db = await ModCompatDatabase.FetchAsync(http, CachePath).ConfigureAwait(false);
                    lock (Gate)
                        _current = db;
                    Changed?.Invoke();
                }
                catch (Exception)
                {
                    // The kept copy stays in use.
                }
            });
    }

    /// <summary>True when the list says the mod works with Coop: the Works group of the side Mods card. False for the
    /// modules Badge leaves out (official, Coop, DedicatedServer.*) and for a refused official module.</summary>
    public static bool Works(string? moduleId, ModCompatDatabase database, string? workshopId = null)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0 || ServerModuleNotes.IsCoopRefused(id) || CoopJoinRules.IsIgnored(id)
            || id.Equals("Coop", StringComparison.OrdinalIgnoreCase) || id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase))
            return false;
        return database.Find(id, workshopId)?.Status == CommunityStatus.Works;
    }

    /// <summary>The badge for a mod, or <see langword="null"/> for the official modules, Coop itself
    /// and the DedicatedServer.* host modules, which are not community mods. unknownNotTested off keeps
    /// the plain Unknown badge (the AD1259 server tool, which leaves out the per PC results). reportTip is
    /// the last tip line, what a click does: the GitHub form unless the tool passes the report buttons line.
    /// records and installedVersion add the ModderLords verdict chip (CompatRecords.Current in the windows).
    /// warSails: the Coop that loads is the nightly, which hosts War Sails, so NavalDLC is an official module with no
    /// badge there, never Broken with a line to turn it off.</summary>
    public static ModCompatBadge? Badge(string? moduleId, ModCompatDatabase database, bool unknownNotTested = true, string? workshopId = null, string? reportTip = null,
        CompatRecordDatabase? records = null, string? installedVersion = null, bool warSails = false)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0 || (warSails && id.Equals(Server.WarSailsCheck.ModuleId, StringComparison.OrdinalIgnoreCase)))
            return null;
        var refused = ServerModuleNotes.IsCoopRefused(id);
        if (!refused && (CoopJoinRules.IsIgnored(id)
            || id.Equals("Coop", StringComparison.OrdinalIgnoreCase)
            || id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)))
            return null;

        var entry = database.Find(id, workshopId);
        var status = refused ? CommunityStatus.Broken : entry?.Status ?? CommunityStatus.Unknown;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry?.Note))
            lines.Add(entry.Note.Trim());
        else if (refused)
            lines.Add("Coop refuses a player who has this official module turned on. Turn it off on the host and every player.");
        var unreported = entry == null || entry.Works + entry.Broken == 0;
        var notTested = unknownNotTested && unreported && status == CommunityStatus.Unknown;
        if (notTested)
            lines.Add(UnknownModBadge.CommunityTip);
        else if (unreported)
            lines.Add("Nobody has reported this mod yet.");
        else
            lines.Add(ModReports.CountsLine(entry!.Works, entry.Broken));
        var needs = Names(entry?.Needs, database);
        var conflicts = Names(entry?.Conflicts, database);
        var tested = Names(entry?.TestedWith, database);
        if (needs.Count > 0)
            lines.Add("Works only with " + string.Join(", ", needs) + ".");
        if (conflicts.Count > 0)
            lines.Add("Broken together with " + string.Join(", ", conflicts) + ".");
        if (tested.Count > 0)
            lines.Add("Tested with " + string.Join(", ", tested) + ".");
        if (!string.IsNullOrWhiteSpace(entry?.CoopVersion))
            lines.Add("Checked by hand with Coop " + entry.CoopVersion!.Trim() + ".");
        lines.Add(reportTip ?? ReportTip);

        var works = entry?.Works ?? 0;
        var broken = entry?.Broken ?? 0;
        // Whole sentences with the numbers in them, so each one has its own translation.
        var text = status switch
        {
            CommunityStatus.Works when conflicts.Count > 0 && works == 1 => "Works for 1 player, conflicts with " + conflicts[0],
            CommunityStatus.Works when conflicts.Count > 0 && works > 1 => "Works for " + works + " players, conflicts with " + conflicts[0],
            CommunityStatus.Works when conflicts.Count > 0 => "Works, conflicts with " + conflicts[0],
            CommunityStatus.Works when works == 1 => "Works for 1 player",
            CommunityStatus.Works when works > 1 => "Works for " + works + " players",
            CommunityStatus.Works => "Works",
            CommunityStatus.Broken when broken == 1 && !refused => "Broken for 1 player",
            CommunityStatus.Broken when broken > 1 && !refused => "Broken for " + broken + " players",
            CommunityStatus.Broken => "Broken",
            CommunityStatus.ClientOnly => "Client only",
            _ => notTested ? UnknownModBadge.Text : "Unknown"
        };
        return new ModCompatBadge(status.ToString(), text, string.Join("\n", lines))
        {
            WorksChip = ModReports.WorksChip(works),
            BrokenChip = ModReports.BrokenChip(broken),
            Tested = CompatRecords.Chip(records?.Find(id), installedVersion)
        };
    }

    // The display names of list ids (the id itself for a mod the list does not know), at most five.
    private static List<string> Names(IEnumerable<string>? ids, ModCompatDatabase database) =>
        (ids ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => database.Find(id.Trim())?.DisplayName ?? id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

    /// <summary>True for the last tip line of a badge, the one that says what a click does.</summary>
    public static bool IsReportTip(string line) => line == ReportTip || line == ModReports.ClickTip;

    /// <summary>The issue form (.github/ISSUE_TEMPLATE/mod-compat.yml) with the mod filled in.
    /// The field names have to match the form's ids.</summary>
    public static string ReportUrl(string moduleId, string? name, string? version, string? coopVersion, string? gameVersion)
    {
        static string E(string? s) => Uri.EscapeDataString((s ?? "").Trim());
        var id = (moduleId ?? "").Trim();
        return "https://github.com/" + ModCompatDatabase.Repository + "/issues/new?template=mod-compat.yml"
            + "&title=" + E("[Mod] " + id)
            + "&mod_id=" + E(id)
            + "&mod_name=" + E(string.IsNullOrWhiteSpace(name) ? id : name)
            + "&mod_version=" + E(version)
            + "&coop_version=" + E(coopVersion)
            + "&game_version=" + E(gameVersion);
    }
}
