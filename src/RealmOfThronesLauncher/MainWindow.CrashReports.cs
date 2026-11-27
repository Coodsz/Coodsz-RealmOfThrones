using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;

namespace RealmOfThronesLauncher;

// Send my logs and Send crash report, built like the other Coodsz client launchers. With the crash repo in RotRelease
// and a report endpoint, Send my logs and My reports fill the slot on Logs and help, Something went wrong? Send your
// logs sits under Connect, and a game that crashed before it joined offers Send crash report. Each one opens the
// consent window, which shows every file before anything is sent (CrashReportWindow.cs). Without them Logs and help
// offers Open client logs only.
public partial class MainWindow
{
    private bool _reportsOn;

    private void SetUpCrashReports()
    {
        if (!RotRelease.ReportsReady || !CrashReports.HasEndpoint())
            return;
        _reportsOn = true;
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, OpenSendLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, OpenSendLogs);
        // My reports next to Send my logs, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, OpenSendLogs, Log, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
    }

    private void OpenSendLogs() => Coodsz.Ui.CrashReportWindow.Open(this, CrashRequest(Coodsz.Ui.CrashContext.Logs), Log);

    // After a crash before the join (ExplainEarlyExitAsync): Send crash report, once sending is set up.
    private void OfferCrashReport(StartupCrashExplain.Capture capture)
    {
        if (!_reportsOn)
            return;
        var line = CrashReports.FindCrashLine(capture.ReportText, capture.GameLog);
        var context = new Coodsz.Ui.CrashContext(CrashReason.Crash, line, ExitCode: capture.ExitCode, CrashedAtUtc: DateTime.UtcNow);
        if (Coodsz.Ui.UiLanguage.Message(CrashReportAsk, "Send crash report", MessageBoxButton.YesNo, MessageBoxImage.Question, this) == MessageBoxResult.Yes)
            Coodsz.Ui.CrashReportWindow.Open(this, CrashRequest(context), Log);
    }

    private const string CrashReportAsk = "The game crashed before it joined. Send a crash report to Coodsz? You see every file first, then you get a short code to post in Discord.";

    // The Realm of Thrones client logs Send my logs and Send crash report pack (RotClientLogPack: the client logs,
    // the Coop Compat log and the logs of the Realm of Thrones server on this PC), text only and cleaned, and the
    // game's newest crash dump while Add the crash dump is ticked. Everything is read here, on the window thread;
    // Collect and FindDump run later off it.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        var windowsDumps = LogZipBuilder.WindowsDumpRoot();
        var gameRoot = _gameRoot;
        var activity = Coodsz.Ui.UiLanguage.EnglishLog(LogBox);
        var actionLog = _actions.FilePath;
        var server = RotServerSettings.Load(Path.Combine(ToolData.FolderOf(ToolData.RotServer), RotServerSettings.FileName));
        var serverData = server.DataRoot;
        // Coop writes Coop_server.log into the server folder, C:\RotServer unless the host chose another.
        var serverRoot = string.IsNullOrWhiteSpace(server.ServerRoot) ? RotServerBuild.DefaultRoot : server.ServerRoot;
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        _actions.Note("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a crash report.");
        return new CrashReportRequest
        {
            Tool = RotRelease.ToolId,
            Side = "client",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = RotStack.CoopVersion,
            Game = RotStack.GameVersion,
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            // The server's join password too, the one in server-config.json and the one the Server Tool saved, so player-notes.txt
            // and the logs are cleaned of both.
            Secrets = new[] { CurrentPassword(), _settings.Password, server.Password, RotClientLogPack.ServerPassword(serverData) }
                .Where(password => !string.IsNullOrEmpty(password)).Select(password => password!).Distinct(StringComparer.Ordinal).ToList(),
            Modules = LaunchOrder().Select(id => (id, (string?)InstalledVersion(id))).ToList(),
            FileStem = "rot-client-logs",
            ModuleRoots = ModuleFingerprints.Roots(gameRoot, [serverRoot]),
            Collect = (zip, limits) => RotClientLogPack.Create(zip, gameRoot, documents, programData, windowsDumps, activity, actionLog, serverData, serverRoot, limits).Skipped,
            FindDump = () => CrashDumps.LatestClientDump(ClientLogZip.ProgramDataCrashes(programData), windowsDumps, dumpSince, dumpUntil)
        };
    }
}
