using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// Get all of this server's mods: when the library mods or the extra mods of the saved setup Connect
// starts are not on this PC, Connect shows one prompt, gets them through Steam and the Install mods
// path, then joins. Then the host's mod list is compared with this PC before the game starts.
public partial class MainWindow
{
    // The library mods first (Harmony, ButterLib and UIExtenderEx from the Steam Workshop, MCM by hand from
    // Nexus Mods), then the setup's extra mods. The Realm of Thrones core is not here; the player puts it in.
    private IReadOnlyList<ServerModNeed> ServerNeeds() =>
        MissingLibraries()
            .Select(RotJoinCheck.LibraryNeed)
            .Concat(RotSharedList.ServerNeeds(LaunchSetup, _installedExtras.Keys,
                    id => ToolPayload.Peek(ToolPayload.BundledMods) is { } carried && File.Exists(Path.Combine(carried, id, "SubModule.xml"))))
            .ToList();

    /// <summary>True to go on with Connect: nothing is missing, or the prompt got every mod.</summary>
    private async Task<bool> GetServerModsFirstAsync()
    {
        // A missing Realm of Thrones core keeps Connect off; the core check after this says what to put in.
        if (_gameRoot == null || CoreProblems(withWorkshopLibraries: false).Count > 0)
            return true;
        ScanInstalledExtras();
        var prompt = ServerModPrompt.For(ServerNeeds());
        if (prompt == null)
            return true;

        _actions.Note("Server mods", prompt.Title + ". " + string.Join(", ", prompt.Mods.Select(need => need.Name)) + ".");
        if (!Coodsz.Ui.ServerModsWindow.Ask(this, prompt))
        {
            Log("Connect was cancelled. Nothing was changed.");
            return false;
        }

        var action = prompt.Action;
        await GetLibrariesAsync();
        await DownloadExtrasAsync(RotSharedList.Missing(LaunchSetup, _installedExtras.Keys).ToList());
        ScanInstalledExtras();
        UnblockModDlls(LaunchSetup);
        ChangedMods();
        var left = ServerModPrompt.For(ServerNeeds());
        if (action == ServerModAction.GetAndConnect && left == null)
        {
            Log("Got every mod the server needs. Connecting now.");
            _actions.Note("Server mods", "Got them all and connected.");
            return true;
        }

        if (left != null)
        {
            var names = string.Join(", ", left.Mods.Select(need => need.Name));
            Log(left.Count == 1
                ? names + " is not ready yet, so the game was not started. Click Connect again when it is there."
                : names + " are not ready yet, so the game was not started. Click Connect again when they are there.");
        }

        return false;
    }

    // ----- The host's mod list against this PC. Any difference stops Connect. -----

    // The mod list imported last (Import mod list), usually the host's. Kept in the launcher's settings
    // folder so Connect can compare it with this PC.
    private static string HostListPath => ToolData.File("rot-host-mod-list.json");

    private static void RememberHostList(IReadOnlyList<SharedModEntry> shared)
    {
        try
        {
            SharedModList.Write(HostListPath, shared);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private static IReadOnlyList<SharedModEntry> ReadHostList()
    {
        try
        {
            return File.Exists(HostListPath) ? SharedModList.Read(File.ReadAllText(HostListPath)) : Array.Empty<SharedModEntry>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return Array.Empty<SharedModEntry>();
        }
    }

    // The Coop nightly turns a join down on any difference in module ids or versions, both ways (RotJoinCheck.HostProblems).
    // The library mods update on each PC's own schedule, so this runs on Plain Realm of Thrones too once a list was imported.
    private IReadOnlyList<RotVersionProblem> HostProblems()
    {
        var host = ReadHostList();
        if (host.Count == 0 || _gameRoot == null)
            return Array.Empty<RotVersionProblem>();
        var setup = LaunchSetup;
        return RotJoinCheck.HostProblems(host, InstalledVersions(host), LaunchOrder(),
            id => setup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Label ?? RotJoinCheck.Name(id));
    }

    // The version on this PC of each module of a shared list that is here (RotSharedList.InstalledVersion): Coop's build,
    // and the compat module this launcher carries, which Connect puts in, so an import before the first Connect does not
    // count it as missing.
    private Dictionary<string, string> InstalledVersions(IReadOnlyList<SharedModEntry> list)
    {
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var carried = CarriedCompatXml() is { } xml ? VersionIn(Path.GetDirectoryName(xml)) : null;
        foreach (var entry in list)
        {
            var id = (entry.Id ?? "").Trim();
            if (id.Length > 0 && !installed.ContainsKey(id) && RotSharedList.InstalledVersion(entry, Describe(id), carried) is { } version)
                installed[id] = version;
        }

        return installed;
    }

    /// <summary>False when this PC differs from the host's list: the first difference and what to do go under Connect,
    /// every difference goes to the log, and the orange line near Connect counts them.</summary>
    private bool HostListMatches()
    {
        var problems = HostProblems();
        ShowVersionDriftNote(problems.Count);
        if (RotJoinCheck.Blocking(problems) is not { } blocking)
            return true;
        ShowJoin(blocking);
        foreach (var problem in problems.Skip(1))
            Log(problem.Text);
        return false;
    }

    // ----- The server's own mods against this setup, before the game starts (RotJoinCheck.NotLoadedHere). -----

    // The hosts Connect already stopped for in this session. The stop is advice: what the launcher knows of a server's
    // mods can be old, so a second Connect to the same host goes on and lets the server decide.
    private readonly HashSet<string> _serverModsWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>False when the server loads a mod this setup leaves out and Connect did not stop for this host yet in
    /// this session: the line under Connect names them and says to import the host's mod list. Plain Realm of Thrones
    /// with no list imported reaches here too.</summary>
    private async Task<bool> ServerModsLoadedHereAsync(bool steam, string joinKey)
    {
        var known = new List<string>(_settings.ServerNeeds.GetValueOrDefault(joinKey) ?? []);
        var port = PortBox.Text.Trim();
        if (!steam)
        {
            await ReadLocalServerAsync();
            // This PC's server on the port typed, and only when it is the Realm of Thrones Server Tool's own server.
            if (_localServer != null && _localServer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) == port
                && LocalServer.Suggest(true, HostBox.Text, _localServer, _adapters.Select(a => a.Address)) != null)
                known.AddRange(await Task.Run(() => OwnServerModules(port)));
        }

        var setup = LaunchSetup;
        var missing = RotJoinCheck.NotLoadedHere(known, LaunchOrder());
        if (missing.Count == 0 || !_serverModsWarned.Add(joinKey))
            return true;
        ShowJoin(RotJoinCheck.ServerModsLine(missing
            .Select(id => setup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Label ?? id).ToList(), before: true));
        return false;
    }

    // The "modules" list of the Realm of Thrones Server Tool's server on this PC, when the port typed is its port and a
    // running BannerlordCoopServer has its data folder as --data-dir. When that can't be told it gives nothing, so a
    // TAOM or Burning Empires server on the same port is never stopped by an old Realm of Thrones config.
    private static IReadOnlyList<string> OwnServerModules(string port)
    {
        try
        {
            var dataRoot = RotServerSettings.Load(Path.Combine(ToolData.FolderOf(ToolData.RotServer), RotServerSettings.FileName)).DataRoot;
            var path = Path.Combine(dataRoot ?? "", NightlyServer.ConfigFileName);
            if (string.IsNullOrWhiteSpace(dataRoot) || !File.Exists(path) || !RotJoinCheck.IsRotServer(ServerCommandLines(), dataRoot))
                return [];
            var text = File.ReadAllText(path);
            var serverPort = LocalServer.ConfigPort(text) ?? LocalServer.DefaultPort;
            return serverPort.ToString(System.Globalization.CultureInfo.InvariantCulture) == port ? NightlyServer.ReadModules(text) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static List<string?> ServerCommandLines()
    {
        var lines = new List<string?>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(NightlyServer.ExeName)))
        {
            using (process)
                lines.Add(ProcessCommandLine.Read(process.Id));
        }

        return lines;
    }

    // Coop turned the join down for mods this setup leaves out: the line says to import the host's mod list, and the
    // next Connect to this host stops once before the game starts. Kept on every refusal, which is harmless: a join to
    // this host that gets in clears it (RememberJoin). A mod Coop names without an id is only logged.
    private void RememberServerNeeds(string joinKey, IReadOnlyList<string> modules, string? log)
    {
        var missing = RotJoinCheck.NotLoadedHere(RotJoinCheck.RequiredByServer(log), modules);
        var named = RotJoinCheck.RequiredByServer(log, namesOnly: true);
        if (missing.Count == 0 && named.Count == 0)
            return;
        Dispatcher.Invoke(() =>
        {
            if (missing.Count > 0)
            {
                _settings.ServerNeeds[joinKey] = missing.ToList();
                SaveSettings();
            }
            Log(RotJoinCheck.ServerModsLine(missing.Concat(named).ToList(), before: false));
        });
    }

    // One orange line near Connect, so a difference is seen without reading the log.
    private void ShowVersionDriftNote(int differences)
    {
        var note = RotUiText.DriftNote(differences);
        VersionDriftNote.Text = note ?? "";
        VersionDriftNote.Visibility = note == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    // A server with Limitless (and its Coop patch) runs battles far bigger than Coop's 1000, and every
    // player's PC shows the whole battle. The host has to load the setup's extra mods too, so they
    // stand for the server's list. A short warning when this PC has little RAM; it never stops Connect.
    private void WarnBigBattles(TaomSetup setup)
    {
        var warning = BigBattles.ClientWarning(setup.EnabledExtras.Select(mod => mod.ModuleId), BigBattles.ThisPcRamGb());
        if (warning == null)
            return;
        Log(warning);
        _actions.Note("Big battles", warning);
    }
}
