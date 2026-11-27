using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// Crash tools: safe mode (the locked Realm of Thrones setup once) and Find the crashing mod. Neither changes the saved setup.
public partial class MainWindow
{
    private bool _safeModeOnce;

    // The setup Connect starts: the saved one, or the locked Realm of Thrones setup for one safe mode start.
    private TaomSetup LaunchSetup => _safeModeOnce
        ? _settings.ModSetups.Find(RotSetupBook.CoreName) ?? _settings.ModSetups.Current()
        : _settings.ModSetups.Current();

    private async void SafeMode_Click(object sender, RoutedEventArgs e)
    {
        Log("Safe mode joins with the Realm of Thrones core only this one time. Your setups are not changed.");
        _safeModeOnce = true;
        try
        {
            UpdateConnectButton();
            await ConnectAsync();
        }
        finally
        {
            _safeModeOnce = false;
        }
    }

    private void RememberJoin(string key, IReadOnlyList<string> modules)
    {
        void Save()
        {
            _settings.LastJoins[key] = CoopJoinRules.Compared(modules).ToList();
            // This host let the join in, so the mods it named when it turned one down are no longer a reason to stop.
            _settings.ServerNeeds.Remove(key);
            SaveSettings();
        }

        if (Dispatcher.CheckAccess())
            Save();
        else
            Dispatcher.Invoke(Save);
    }

    // Halves the extra mods each round and starts the game to the main menu with one half, until
    // one mod is left. The saved setup stays as it is. The Realm of Thrones core loads in every round.
    private async void FindCrash_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Log("Wait for the current action to finish.");
            return;
        }
        // Not searched again here: the game found at startup also sets up the mods.
        if (_gameRoot == null)
        {
            Log("Bannerlord was not found.");
            return;
        }
        var setup = LaunchSetup;
        CrashBisect search;
        try
        {
            search = CrashBisect.Start(setup, Manifests(GatherModFacts()), RotLoadOrder.ClientOrder);
        }
        catch (InvalidOperationException ex)
        {
            Log(ex.Message);
            return;
        }

        if (Coodsz.Ui.UiLanguage.Message(
                "This starts the game to the main menu a few times, each time with half of the extra mods on " + setup.Name + ". Wait for the main menu or the crash, close the game, then answer. Your setup is not changed.",
                "Find the crashing mod", MessageBoxButton.OKCancel, MessageBoxImage.Information, this) != MessageBoxResult.OK)
            return;
        // Every round loads Coop, so the RoT build this launcher carries goes in first, as at Connect.
        if (!await PutInCoopAsync())
            return;

        _busy = true;
        try
        {
            var labels = setup.Extras.ToDictionary(mod => mod.ModuleId, mod => mod.Label, StringComparer.OrdinalIgnoreCase);
            string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => labels.TryGetValue(id, out var label) ? label : id));
            while (!search.Done)
            {
                Log("Round " + search.Round + ". Testing " + Names(search.Testing) + ".");
                var crashed = await TestToMainMenuAsync(search.TestSetup());
                if (crashed == null)
                {
                    Log("Find the crashing mod was stopped. Your setup was not changed.");
                    return;
                }

                search.Report(crashed.Value);
            }

            var found = search.Found;
            Log(found == null
                ? "No single mod was found. The crash may need two mods together, or it may not come from the extra mods."
                : "The crash most likely comes from " + Names(new[] { found }) + ". Turn it off or remove it, then click Save mods.");
        }
        finally
        {
            _busy = false;
        }
    }

    // The version of an extra mod on this PC, or "".
    private string InstalledVersion(string moduleId) =>
        _installedExtras.TryGetValue(moduleId, out var mod) ? VersionIn(mod.Directory) : "";

    // The launch order of the setup in use, for the version compare and the log pack.
    private IReadOnlyList<string> LaunchOrder()
    {
        try
        {
            return RotLoadOrder.LaunchModules(LaunchSetup, false);
        }
        catch (InvalidOperationException)
        {
            return LaunchSetup.EnabledExtras.Select(mod => mod.ModuleId).ToList();
        }
    }

    // True when the game crashed before the main menu, false when it reached it, null when stopped.
    private async Task<bool?> TestToMainMenuAsync(TaomSetup test)
    {
        var gameRoot = _gameRoot!;
        try
        {
            ClientLaunchGuard.EnsureSingleGame();
            await UnblockBeforeLaunchAsync();
            var arguments = TaomLaunch.Arguments(connect: false, steamLobby: false, password: null, modules: RotLoadOrder.LaunchModules(test, joinPassword: false));
            Log(arguments);
            var started = StartWithPrefabsFix(test, new ProcessStartInfo
            {
                FileName = GameRoot.ExecutablePath(gameRoot),
                WorkingDirectory = GameRoot.WorkingDirectory(gameRoot),
                Arguments = arguments,
                UseShellExecute = false
            });
            await started.WaitForExitAsync();
            RestorePrefabsFix();
            var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
            await Task.Delay(1500);
            var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
            var lines = StartupCrashExplain.Explain(false, capture.ExitCode, capture.ReportText, capture.GameLog,
                Array.Empty<StartupCrashExplain.ModName>(), RotLoadOrder.ClientOrder(test).ToList(), setupUsesWorkshop: true);
            if (StartupCrashExplain.SawStartupCrash(lines))
            {
                Log("The game crashed in this round.");
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Log(ex.Message);
            return null;
        }

        var answer = Coodsz.Ui.UiLanguage.Message("Did the game reach the main menu without crashing?\n\nYes if it did. No if it crashed. Cancel stops the search.",
            "Find the crashing mod", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, this);
        return answer switch
        {
            MessageBoxResult.Yes => false,
            MessageBoxResult.No => true,
            _ => null
        };
    }
}
