using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

public partial class MainWindow
{
    private string? _memoryNoted;
    private string _twinsNoted = "";

    // A mod of this setup in two Workshop items: the game loads one of them, and a
    // re-upload of Harmony, ButterLib or UIExtenderEx crashes it at startup.
    private IReadOnlyList<WorkshopTwin> FindWorkshopTwins(string? gameLog = null) =>
        WorkshopTwins.Find(Workshops().Select(library => library.ContentDirectory),
            RotLoadOrder.ClientOrder(_settings.ModSetups.Current()),
            gameLog: () => gameLog, fallbackLog: () => WorkshopTwins.LatestClientLog(StartupCrashExplain.EngineLogDirectory()));

    private void NoteWorkshopTwins()
    {
        var lines = FindWorkshopTwins().Select(WorkshopTwins.Describe).ToList();
        var key = string.Join("|", lines);
        if (key == _twinsNoted)
            return;
        _twinsNoted = key;
        foreach (var line in lines)
            Log(line);
    }

    // MCM in the game's Modules folder (from Nexus, the copy the host runs) and the download of the removed official
    // Workshop item load as two MCMs. FindModuleXml takes the game folder copy, so Connect turns the download off and
    // Check mods turns it back on once the game folder copy is gone, as the Coop and Friends client does.
    private IReadOnlyList<string> ClientWorkshopFolders() => Workshops().Select(library => library.ContentDirectory).ToList();

    private void ParkRetiredMcm()
    {
        var problems = new List<string>();
        foreach (var moduleId in WorkshopDuplicates.ParkRetiredWorkshopCopies(_gameRoot, RotStack.ClientModules, ClientWorkshopFolders(), problems))
            Log("Turned off the old Workshop copy of " + RotJoinCheck.Name(moduleId) + ". The game uses the copy in its Modules folder, so it no longer loads two copies of that mod.");
        foreach (var problem in problems)
            Log(problem);
    }

    private void RestoreRetiredMcm()
    {
        var problems = new List<string>();
        foreach (var moduleId in WorkshopDuplicates.RestoreRetiredWorkshopCopies(_gameRoot, ClientWorkshopFolders(), problems))
            Log("Turned the old Workshop copy of " + RotJoinCheck.Name(moduleId) + " back on, because the game's Modules folder no longer has that mod.");
        foreach (var problem in problems)
            Log(problem);
    }

    private string _retiredNoted = "";

    private void NoteRetiredMcm()
    {
        var lines = WorkshopDuplicates.RetiredWorkshopCopies(_gameRoot, RotStack.ClientModules, ClientWorkshopFolders())
            .GroupBy(copy => copy.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => RotJoinCheck.Name(group.Key) + " has a second copy: " + string.Join(", ", group.Select(copy => "\"" + copy.Folder + "\"")) + ". That is an old Steam Workshop download. Unsubscribe from it in Steam so the game does not load both, then click Check mods.")
            .ToList();
        var key = string.Join("|", lines);
        if (key == _retiredNoted)
            return;
        _retiredNoted = key;
        foreach (var line in lines)
            Log(line);
    }

    // A short line under Connect when this PC is low on memory, or runs a dedicated server
    // with too little memory for both (a big overhaul froze at 97% on a 16 GB laptop that hosted).
    // It never blocks Connect.
    private async Task CheckMemoryAsync()
    {
        string? warning = null;
        try
        {
            warning = await Task.Run(() => LowMemory.ClientWarning(LowMemory.Read(), LowMemory.DedicatedServerRunning(), game: RotStack.Name));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        MemoryWarning.Text = warning ?? "";
        MemoryWarning.Visibility = warning == null ? Visibility.Collapsed : Visibility.Visible;
        if (warning == null || warning == _memoryNoted)
            return;
        _memoryNoted = warning;
        Log(warning);
        _actions.Note("Low memory", warning);
    }

    // The game closed before it joined: read its crash report and engine log
    // and say what to change, like the Coop and Friends client does.
    private async Task ExplainEarlyExitAsync(Process started)
    {
        int? exitCode = null;
        try
        {
            exitCode = started.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        await Task.Delay(1500);
        var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
        NoteBlockedDll(capture.ReportText + "\n" + capture.GameLog);
        var lines = StartupCrashExplain.Explain(
            false,
            capture.ExitCode ?? exitCode,
            capture.ReportText,
            capture.GameLog,
            Array.Empty<StartupCrashExplain.ModName>(),
            RotLoadOrder.ClientOrder(_settings.ModSetups.Current()).ToList(),
            setupUsesWorkshop: false);
        if (!StartupCrashExplain.SawStartupCrash(lines))
            return;
        foreach (var line in WorkshopTwins.PointCrashAt(lines, FindWorkshopTwins(capture.GameLog)))
            Log(line);
        OfferCrashReport(capture);
    }
}
