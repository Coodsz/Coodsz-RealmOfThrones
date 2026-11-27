using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;

namespace RealmOfThronesHost;

// Send my logs in the sidebar and on the Logs page, the same buttons as the other Coodsz server tools. With the
// crash repo in RotRelease and a report endpoint they work like the other Coodsz server tools: each opens the consent
// window (RotHostLogPack), and after the server crashed or stopped by itself the red bar offers Send server crash report.
// Without them the buttons say sending is not set up and offer the log files, and no crash bar opens after a crash.
// The exit line (NightlyServer.ExitHint) still says what happened.
public partial class MainWindow
{
    private Coodsz.Ui.ServerCrashBar? _crashBar;

    private void SetUpCrashReports()
    {
        if (!RotRelease.ReportsReady || !CrashReports.HasEndpoint())
        {
            SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, ShowReportsNotSetUp);
            SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, ShowReportsNotSetUp);
            return;
        }

        _crashBar = new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, RotRelease.ToolId, CrashRequest, line => NoteLog(line));
        SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, _crashBar.OpenLogs);
        SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this, _crashBar.OpenLogs);
        LogsSendLogsButton.ToolTip = Translator.T(SendLogsTip);
        // My reports under Send my logs in the sidebar, and the check for Coodsz's answers (ReportReplyWindow.cs).
        Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, _crashBar.OpenLogs, line => NoteLog(line), below: true, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);
        // My reports beside Send my logs on the Logs page, once a report was sent.
        Coodsz.Ui.ReportRepliesUi.AddMyReports(this, LogsMyReportsSlot, _crashBar.OpenLogs, line => NoteLog(line), logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs));
        // Coodsz's notice above every page (AnnouncementUi.cs). It names Send my logs, so it only shows where that button is.
        Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, RotRelease.ToolId);
        Loaded += (_, _) => _crashBar.ShowIntroOnce();
    }

    private const string SendLogsTip = "Sends the cleaned server logs, Coop_server.log, the Realm of Thrones Coop Compat log, what this window captured from the server, the engine logs, server crash reports and the settings without the password to Coodsz. You see every file first, then you get a short code to post in Discord. Save a copy instead is in that window.";

    private void SendLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_crashBar != null)
            _crashBar.OpenLogs();
        else
            ShowReportsNotSetUp();
    }

    // Sending is not set up (no crash repo or no endpoint), so the button says so and offers the log files instead.
    private void ShowReportsNotSetUp()
    {
        NoteLog(RotRelease.ReportsNotSetUp);
        if (ConfirmWindow.Ask(this, "Send my logs", RotRelease.ReportsNotSetUp + " " + LogsInstead, null, "Open server logs"))
            OpenLogs_Click(this, new RoutedEventArgs());
    }

    private const string LogsInstead = "Open server logs shows the log files, so you can share them where you ask for help.";

    // After the server process exited by itself: a crash or a stop nobody asked for brings Send server crash report.
    // With sending not set up there is no bar.
    private void OfferServerCrashReport(Process process)
    {
        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var exit = ServerExitInfo.From(exitCode, _stopAsked);
        if (_crashBar == null || !CrashReports.ShowsServerCrashBar(exit))
            return;
        _crashBar.ServerCrashed(CrashReports.FindCrashLine(_logTail.ToString()), CrashReports.ServerCrashTitle(exit.Kind), exit.ExitCode);
    }

    // The Realm of Thrones server logs Send my logs and Send server crash report pack (RotHostLogPack), text only
    // and cleaned, and the server's newest crash dump only while Add the crash dump is ticked. Everything is read
    // here, on the window thread.
    private CrashReportRequest CrashRequest(Coodsz.Ui.CrashContext context)
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        var windowsDumps = LogZipBuilder.WindowsDumpRoot();
        var serverRoot = _settings.ServerRoot;
        var dataRoot = _settings.DataRoot;
        var windowLog = Coodsz.Ui.UiLanguage.EnglishLog(LogBox);
        var dumpSince = context.DumpSince(DateTime.UtcNow);
        var dumpUntil = context.DumpUntil();
        _actions.Note("Crash report", context.Reason == CrashReason.Logs ? "Packed the logs for Send my logs." : "Packed the logs for a server crash report.");
        return new CrashReportRequest
        {
            Tool = RotRelease.ToolId,
            Side = "server",
            Reason = context.Reason,
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = RotStack.CoopVersion,
            Game = RotStack.GameVersion,
            CrashLine = context.CrashLine,
            SuspectedMod = context.SuspectedMod,
            ExitCode = context.ExitCode,
            Secrets = KnownPasswords(),
            Modules = ReportModules(),
            FileStem = "rot-host-logs",
            ModuleRoots = ModuleFingerprints.Roots(null, [serverRoot]),
            Collect = (zip, limits) => RotHostLogPack.Create(zip, serverRoot, dataRoot, programData, windowLog, includeDumps: false, windowsDumpRoot: windowsDumps, limits: limits).Skipped,
            ServerFolders = string.IsNullOrWhiteSpace(serverRoot) ? [] : [serverRoot],
            FindDump = () => RotHostLogPack.LatestDump(programData, windowsDumps, dumpSince, dumpUntil)
        };
    }

    // The setup's server order; a setup that breaks the core order still sends the core.
    private List<(string, string?)> ReportModules()
    {
        IReadOnlyList<string> order;
        try
        {
            order = RotLoadOrder.ServerOrder(HostSetup);
        }
        catch (InvalidOperationException)
        {
            order = RotStack.ServerModules;
        }

        return order.Select(id => (id, (string?)null)).ToList();
    }

    // The server passwords this tool knows, like Coodsz Host: the box, the saved one and the one in
    // the server's own server-config.json. Kept out of every log the tool writes.
    private IReadOnlyList<string> KnownPasswords()
    {
        string? configPassword = null;
        try
        {
            var path = Path.Combine(_settings.DataRoot, "server-config.json");
            if (!string.IsNullOrWhiteSpace(_settings.DataRoot) && File.Exists(path))
                configPassword = ReadString(File.ReadAllText(path), "password");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        return CrashReports.KnownSecrets(PasswordBox.Text, _settings.Password, configPassword);
    }
}
