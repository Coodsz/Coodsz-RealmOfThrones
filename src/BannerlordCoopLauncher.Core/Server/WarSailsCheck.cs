using System.Text.Json;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>
/// War Sails (NavalDLC) on a Coop dedicated server. The server turns it on with "warSails" in the
/// "modOptions" block of mod-config.json, then wants NavalDLC in its module list and loads NavalDLC's
/// own campaign map, never an overhaul's. These tools can't host it yet, so every engine start
/// (Start server, the mod test) refuses it first instead of letting the server stop with exit code 10.
/// The one exception is the Coop and Friends Server Tool on a Coop nightly server (NightlyServer).
/// </summary>
public static class WarSailsCheck
{
    public const string ModuleId = "NavalDLC";

    public const string ModuleRefusal =
        "This tool can't host War Sails yet. NavalDLC (War Sails) is in the server's module list. Turn NavalDLC off for the server, then start the server again.";

    /// <summary>Is War Sails on, read the way the server reads it: the key in "modOptions" without regard to
    /// case, true, "true" (with spaces around it too, as Coop's Newtonsoft reader takes it) or a number that is not 0. A
    /// missing key or a file the server can't read is off.</summary>
    public static bool IsOn(string? modConfigJson)
    {
        if (string.IsNullOrWhiteSpace(modConfigJson))
            return false;
        try
        {
            using var doc = JsoncEditor.Parse(modConfigJson).ToJsonDocument();
            if (Property(doc.RootElement, "modOptions") is not { ValueKind: JsonValueKind.Object } options
                || Property(options, "warSails") is not { } value)
                return false;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                JsonValueKind.Number => value.TryGetDouble(out var number) && number != 0,
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>True when the server may run War Sails: not the AD1259 edition, and the server folder's own Coop is the
    /// nightly (CoopInstall.IsNightly). The Coop comes from the server's Modules, never the game's Modules\Coop or Native.</summary>
    public static bool NightlyServer(bool ad1259, string? serverCoopId, string? serverCoopVersion) =>
        !ad1259 && CoopInstall.IsNightly(serverCoopId, serverCoopVersion);

    /// <summary>The Start line on a Coop nightly server: which way War Sails went, from the NavalDLC row.</summary>
    public static string StartLine(bool warSails) => warSails
        ? "War Sails is on for this start: NavalDLC loads right after Coop, and \"warSails\" is true in mod-config.json."
        : "War Sails is off for this start: \"warSails\" is false in mod-config.json. To host War Sails, turn NavalDLC on in the mod list of a setup of your own. The built in setups are locked, so make one with New setup on the Saves page.";

    /// <summary>The Mods page note on a locked setup of a Coop nightly server (the one with a NavalDLC row): the row stays
    /// off there, so it says where War Sails can be turned on.</summary>
    public const string LockedSetupLine =
        "This setup is locked. The working mods cannot be changed. War Sails needs a setup of your own, so click New setup on the Saves page and turn NavalDLC on there.";

    /// <summary>
    /// The server module order with NavalDLC moved right after the Coop row (Coop or CoopNightly), the place
    /// NightlyServer.LoadsFirst gives it. The order is unchanged when there is no NavalDLC or no Coop.
    /// </summary>
    public static IReadOnlyList<string> NavalAfterCoop(IReadOnlyList<string> order)
    {
        var coop = order.FirstOrDefault(BannerlordCoopModules.IsEither);
        var naval = order.FirstOrDefault(id => id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase));
        if (coop == null || naval == null)
            return order;
        var moved = order.Where(id => !id.Equals(ModuleId, StringComparison.OrdinalIgnoreCase)).ToList();
        moved.Insert(moved.IndexOf(coop) + 1, naval);
        return moved;
    }

    /// <summary>
    /// The exit explanation on a Coop nightly server. For exit codes 9 and 10 the known crash pattern's What to do and
    /// What not to do lines that name War Sails or NavalDLC say these tools can't host War Sails, so they give way to
    /// NightlyServer's own line. What happened and every other line stay. Other codes and servers keep their lines.
    /// </summary>
    public static IReadOnlyList<string> NightlyExitLines(bool nightlyServer, int? exitCode, IReadOnlyList<string> lines)
    {
        if (!nightlyServer || exitCode is not (ServerExit.WarSailsSaveMismatchCode or ServerExit.WarSailsSetupCode))
            return lines;
        var kept = lines.Where(line => !((line.StartsWith("What to do:", StringComparison.Ordinal) || line.StartsWith("What not to do:", StringComparison.Ordinal))
            && (line.Contains("War Sails", StringComparison.Ordinal) || line.Contains("NavalDLC", StringComparison.Ordinal)))).ToList();
        var happened = kept.FindIndex(line => line.StartsWith("What happened:", StringComparison.Ordinal));
        kept.Insert(happened + 1, exitCode == ServerExit.WarSailsSetupCode ? NightlySetupLine : "What to do: " + Server.NightlyServer.ExitHint(exitCode.Value));
        return kept;
    }

    /// <summary>Exit code 10 on a nightly server: "warSails" and NavalDLC did not agree at start, or the build has no War
    /// Sails. Start writes "warSails" from the NavalDLC row, so a new Start is the first step.</summary>
    public const string NightlySetupLine =
        "What to do: Press Start server again. It writes \"warSails\" to match the NavalDLC row, so a file changed by hand is fixed. If the server stops with exit code 10 again, this server build has no War Sails. Turn NavalDLC off, or use a Coop nightly server download that has it.";

    /// <summary>
    /// Why a War Sails start can't go on, checked before the mods are copied, or null. War Sails brings NavalDLC's own campaign
    /// map, so a picked map mod (one of <paramref name="mapMods"/>, the map box's choices) is refused. A new campaign starts
    /// from the War Sails blank (the server copies it, NightlyServer.EnsureBlank), so its name follows NewSaveProblem and the blank must be there.
    /// </summary>
    /// <param name="writeProblem">WriteProblem for this start, so a War Sails start is refused before the mods are copied.</param>
    public static string? StartProblem(bool warSails, string? writeProblem, string? campaignMap, IEnumerable<string> mapMods, bool createsCampaign, string? saveName, string dataDir, string serverRoot)
    {
        if (!warSails)
            return null;
        if (writeProblem != null)
            return writeProblem;
        if (!string.IsNullOrWhiteSpace(campaignMap) && mapMods.Contains(campaignMap, StringComparer.OrdinalIgnoreCase))
            return MapRefusal;
        if (!createsCampaign)
            return null;
        if (Server.NightlyServer.NewSaveProblem(saveName) is { } name)
            return name;
        return Server.NightlyServer.BlankCandidates(dataDir, serverRoot, warSails: true).Any(File.Exists) ? null : NoBlank;
    }

    /// <summary>
    /// Why an existing campaign can't start in this War Sails mode on a Coop nightly server, or null. A save remembers
    /// NavalDLC (its header's module list), and the server never adds War Sails to a world or takes it out of one (exit
    /// code 9). A save without a module list is left to the server.
    /// </summary>
    public static string? SaveProblem(bool warSails, IReadOnlyList<string>? saveModules)
    {
        if (saveModules == null || saveModules.Count == 0)
            return null;
        var naval = saveModules.Contains(ModuleId, StringComparer.OrdinalIgnoreCase);
        return naval == warSails ? null : warSails ? SaveWithoutWarSails : SaveWithWarSails;
    }

    public const string SaveWithoutWarSails =
        "This campaign was made without War Sails, and War Sails can't be added to a campaign that already started. To play War Sails, type a new name on the Saves page, click + New save and press Start server. To play this campaign, turn NavalDLC off on the Mods page.";

    public const string SaveWithWarSails =
        "This campaign was made with War Sails, so it only starts with NavalDLC on. Turn NavalDLC on in the mod list of a setup of your own, or pick another campaign.";

    /// <summary>The marker a War Sails run leaves next to mod-config.json until "warSails" is false again, so a run whose
    /// tool closed before its server stopped is still reset later (ResetAfterStop).</summary>
    public static string MarkerPath(string dataDir) =>
        Path.Combine(Path.GetDirectoryName(Server.NightlyServer.ModConfigPath(dataDir))!, "coodsz-war-sails-run");

    /// <summary>
    /// Why a Start on a Coop nightly server can't write "warSails" (<paramref name="warSails"/>) now, or null. A Coop server
    /// engine found on this PC refuses it, since the write would change the setting under that server. A dotnet whose path
    /// can't be read (<paramref name="unreadable"/>, an elevated build server say) may be one, so then Start goes on only
    /// when mod-config.json already reads as that value the way the server reads it (IsOn), so the value a running server
    /// reads does not change (Start may still write the key out in full).
    /// </summary>
    public static string? WriteProblem(bool engineFound, IReadOnlyList<int> unreadable, string modConfigPath, bool warSails)
    {
        if (engineFound)
            return OtherServerRunning;
        if (unreadable.Count == 0)
            return null;
        string? text = null;
        try
        {
            if (File.Exists(modConfigPath))
                text = File.ReadAllText(modConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read as off, the way the server reads it.
        }

        return IsOn(text) == warSails ? null : UnreadableProcessLine(unreadable[0]);
    }

    /// <summary>Why the mod test can't set "warSails" to false on a Coop nightly server, or null: a dotnet whose path can't
    /// be read (<paramref name="unreadable"/>) may be a Coop server, so a "warSails" that reads as on stays on, and every
    /// test server would stop with exit code 10 and count against its mod.</summary>
    public static string? ModTestProblem(IReadOnlyList<int> unreadable, string modConfigPath) =>
        WriteProblem(false, unreadable, modConfigPath, false) == null
            ? null
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.Translator.T(ModTestUnreadableText), unreadable[0]);

    public const string ModTestUnreadableText =
        "This tool can't read the dotnet process {0}, so it can't tell if that is a Coop server, and \"warSails\" is true in the mod-config.json every Coodsz server tool shares. The test servers can't run with it on. Close that program, or run it and this tool the same way (both as administrator or both not), then click Start test again.";

    /// <summary>A War Sails start while another Coop server engine runs on this PC (any folder or tool) is refused.</summary>
    public const string OtherServerRunning =
        "Another Coop server is running on this PC. War Sails changes a setting that every Coodsz server tool shares, so stop that server first, then press Start server again.";

    public const string UnreadableProcessText =
        "This tool can't read the dotnet process {0}, so it can't tell if that is a Coop server, and this start would change \"warSails\" in the mod-config.json every Coodsz server tool shares. Close that program, or run it and this tool the same way (both as administrator or both not), then press Start server again.";

    /// <summary>The refusal for an unreadable dotnet when "warSails" would change, in the tool's language.</summary>
    public static string UnreadableProcessLine(int processId) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.Translator.T(UnreadableProcessText), processId);

    /// <summary>A War Sails run starts: the marker names the server folder that owns the run, the tool window that starts
    /// it and, once it is known, the server process. It goes down before "warSails" is written true, through a temporary
    /// file, so it is never half written.</summary>
    public static void MarkRun(string dataDir, string serverRoot, int processId = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath(dataDir))!);
        var temp = MarkerPath(dataDir) + ".tmp";
        File.WriteAllText(temp, "root=" + Path.GetFullPath(serverRoot) + "\npid=" + processId + "\nhost=" + Environment.ProcessId + "\n");
        File.Move(temp, MarkerPath(dataDir), overwrite: true);
    }

    /// <summary>Where the War Sails seeds that wait for a cleanup are kept, next to the marker, so they outlive the tool.</summary>
    public static string SeedsPath(string dataDir) =>
        Path.Combine(Path.GetDirectoryName(Server.NightlyServer.ModConfigPath(dataDir))!, "coodsz-war-sails-seeds");

    /// <summary>The owner a marker names: its server folder ("" when unknown), server process id and the id of the tool
    /// window that started it (0 when unknown). The marker never shipped in another format (this branch was unreleased),
    /// so there is no older one to read.</summary>
    public static (string Root, int ProcessId, int HostProcessId) ReadMarker(string dataDir)
    {
        var root = "";
        var processId = 0;
        var hostProcessId = 0;
        foreach (var line in File.ReadAllLines(MarkerPath(dataDir)))
        {
            if (line.StartsWith("root=", StringComparison.Ordinal))
                root = line[5..].Trim();
            else if (line.StartsWith("pid=", StringComparison.Ordinal))
                _ = int.TryParse(line[4..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out processId);
            else if (line.StartsWith("host=", StringComparison.Ordinal))
                _ = int.TryParse(line[5..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out hostProcessId);
        }

        return (root, processId, hostProcessId);
    }

    /// <summary>
    /// After a War Sails run: "warSails" goes back to false in the shared mod-config.json, since the TAOM, Burning Empires
    /// and AD1259 Server Tools refuse to start while it is true, then the marker goes. Never while any Coop server engine
    /// runs on this PC (<paramref name="anyServerRunning"/>, from any folder or tool, asked only when there is a marker so
    /// a host who never ran War Sails pays no process scan), and the run's own owner must be gone too: the process the
    /// marker names, any server from its folder, and before the launch the other tool window that wrote it. A skipped
    /// reset keeps the marker, so the next tool start, Start or mod test tries again. A marker without a mod-config.json
    /// just goes, so no bare file is made. The marker stays when the write fails. Null when done or nothing was due, else
    /// the line to log.
    /// </summary>
    public static string? ResetAfterStop(string dataDir, Func<bool> anyServerRunning, Func<int, bool> processAlive, Func<string, bool> serverRunsFrom)
    {
        // How long another window's marker without a server process is left alone.
        var hostWait = TimeSpan.FromMinutes(5);
        try
        {
            if (!File.Exists(MarkerPath(dataDir)) || anyServerRunning())
                return null;
            var (root, processId, hostProcessId) = ReadMarker(dataDir);
            if ((processId > 0 && processAlive(processId)) || (root.Length > 0 && serverRunsFrom(root)))
                return null;
            // Another tool window is between its marker and its launch (no server process yet), so its start owns the
            // marker until then. This window's own marker (a failed launch here) is never waited for. Only for a fresh
            // marker: a launch takes seconds, and a tool that died there may have its process id reused by a program
            // that runs for days, which must not keep "warSails" true and the other tools locked out.
            if (processId == 0 && hostProcessId > 0 && hostProcessId != Environment.ProcessId && processAlive(hostProcessId)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerPath(dataDir)) < hostWait)
                return null;
            if (File.Exists(Server.NightlyServer.ModConfigPath(dataDir)))
                Server.NightlyServer.ApplyWarSails(dataDir, false);
            File.Delete(MarkerPath(dataDir));
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return ResetFailed(Server.NightlyServer.ModConfigPath(dataDir), ex.Message);
        }
    }

    /// <summary>
    /// A launch that failed: the War Sails blank copy goes (also a partial copy SeedSave left before it threw), then
    /// "warSails" goes back, each on its own so neither failure skips the other. <paramref name="seedName"/> is the new
    /// campaign a seed was tried for, or null. The lines of the steps that failed. <paramref name="anyServerRunning"/> is
    /// asked at most once, and only when there is a seed or a marker.
    /// </summary>
    /// <param name="keptSeed">The seed name when its drop waited for a running engine or failed, so the caller keeps it for a retry.</param>
    public static IReadOnlyList<string> RollBack(string dataDir, string serverRoot, string? seedName, Func<bool> anyServerRunning, Func<int, bool> processAlive, Func<string, bool> serverRunsFrom, out string? keptSeed)
    {
        bool? running = null;
        bool Running() => running ??= anyServerRunning();
        var failed = new List<string>();
        keptSeed = seedName != null && Running() ? seedName : null;
        // A drop that fails (a locked blank) keeps the seed for a retry too, as DropSeedNamed does.
        if (seedName != null && keptSeed == null && DropSeed(dataDir, serverRoot, seedName) is { } seed)
        {
            failed.Add(seed);
            keptSeed = seedName;
        }
        if (ResetAfterStop(dataDir, Running, processAlive, serverRunsFrom) is { } reset)
            failed.Add(reset);
        return failed;
    }

    /// <summary>Removes a War Sails blank copy the server never built a world from (NightlyServer.RemoveUnbuiltSeed). Null
    /// when it went or was no blank copy, else the line to log.</summary>
    public static string? DropSeed(string dataDir, string serverRoot, string seedName) => DropSeed(dataDir, serverRoot, seedName, out _);

    public static string? DropSeed(string dataDir, string serverRoot, string seedName, out bool removed)
    {
        removed = false;
        try
        {
            removed = Server.NightlyServer.RemoveUnbuiltSeed(dataDir, serverRoot, seedName);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SeedNotRemoved(seedName, ex.Message);
        }
    }

    public static string SeedNotRemoved(string name, string reason) =>
        "The blank copy for the new campaign " + name + " could not be removed. " + reason;

    public static string ResetFailed(string path, string reason) =>
        "\"warSails\" could not be set back to false in " + path + ". " + reason + " The TAOM, Burning Empires and AD1259 Server Tools refuse to start until it is false. This tool tries again on the next Start.";

    /// <summary>War Sails brings NavalDLC's own campaign map, and the start script refuses two map mods at once.</summary>
    public const string MapRefusal =
        "War Sails brings its own campaign map, so it can't run with a map mod. Pick Vanilla campaign Map, or turn NavalDLC off, then press Start server again.";

    /// <summary>A new War Sails campaign needs the server's War Sails blank (NightlyServer.EnsureBlank).</summary>
    public const string NoBlank =
        "This server has no default_new_game_warsails.sav, the blank campaign a new War Sails campaign starts from. Turn NavalDLC off, or use a Coop nightly server download that has it, then press Start server again.";

    /// <summary>Null when the server can start; otherwise the line that says why it was not started.</summary>
    /// <param name="modConfigPath">The mod-config.json the server reads (COOP_DATA_DIR).</param>
    /// <param name="serverModules">The module list the engine would start with.</param>
    /// <param name="nightlyServer">NightlyServer: the Coop nightly hosts War Sails, so nothing is refused.</param>
    public static string? Refusal(string modConfigPath, IEnumerable<string> serverModules, bool nightlyServer = false)
    {
        if (nightlyServer)
            return null;
        if (serverModules.Contains(ModuleId, StringComparer.OrdinalIgnoreCase))
            return ModuleRefusal;
        string? text = null;
        try
        {
            if (File.Exists(modConfigPath))
                text = File.ReadAllText(modConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server can't read it either, so it runs without War Sails.
        }

        return IsOn(text)
            ? "This tool can't host War Sails yet. \"warSails\" is true in mod-config.json. Set it to false in " + modConfigPath + ", then start the server again."
            : null;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}

/// <summary>
/// The War Sails seed of the server run that made it, so a late exit of an older run never removes a newer run's blank.
/// </summary>
public sealed class WarSailsSeed
{
    // One record per run, so a newer run's seed never replaces an older run's before that run's exit is handled. Kept in
    // a small file when a path is given, so a seed whose drop waited for a running engine outlives the tool.
    private readonly Dictionary<int, string> _seeds = new();
    private readonly string? _path;
    private int _kept;

    public WarSailsSeed(string? path = null)
    {
        _path = path;
        try
        {
            if (path == null || !File.Exists(path))
                return;
            foreach (var line in File.ReadAllLines(path))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line[..tab], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var run) && line.Length > tab + 1)
                    _seeds[run] = line[(tab + 1)..];
            }
            // Runs of an earlier session never match this session's run numbers, so their seeds get negative keys
            // (counting down from -1), which no run of ServerSession ever has: only TakeAll hands them out.
            var earlier = _seeds.Values.ToList();
            _seeds.Clear();
            foreach (var name in earlier)
                _seeds[--_kept] = name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable file keeps nothing; its blanks are only byte identical copies.
        }
    }

    /// <summary>A run started; <paramref name="seededSave"/> is the new campaign it seeded, or null.</summary>
    public void Started(int run, string? seededSave)
    {
        if (seededSave == null)
            return;
        _seeds[run] = seededSave;
        Save();
    }

    /// <summary>The seed of this run, handed out once, or null for a run without one.</summary>
    public string? TakeFor(int run)
    {
        if (!_seeds.Remove(run, out var name))
            return null;
        Save();
        return name;
    }

    /// <summary>True while a seed is kept, so a host who never seeded one pays no process scan for the cleanup.</summary>
    public bool Any => _seeds.Count > 0;

    /// <summary>Every kept seed, handed out once, for a retry when no engine runs.</summary>
    public IReadOnlyList<string> TakeAll()
    {
        var all = _seeds.Values.ToList();
        _seeds.Clear();
        Save();
        return all;
    }

    /// <summary>A seed whose drop failed is kept for the next retry, under the next negative key (see the constructor).</summary>
    public void Keep(string name)
    {
        _seeds[--_kept] = name;
        Save();
    }

    private void Save()
    {
        if (_path == null)
            return;
        try
        {
            if (_seeds.Count == 0)
            {
                if (File.Exists(_path))
                    File.Delete(_path);
                return;
            }
            var temp = _path + ".tmp";
            File.WriteAllLines(temp, _seeds.Select(seed => seed.Key.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" + seed.Value));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The seeds stay in memory for this session.
        }
    }
}
