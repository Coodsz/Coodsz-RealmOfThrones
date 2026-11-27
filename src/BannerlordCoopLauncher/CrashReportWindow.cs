using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace Coodsz.Ui;

/// <summary>What a tool knows about the crash a bar or button offers to report. CrashedAtUtc is when the
/// tool saw the crash (its bar came up), so the crash dump picked is from this crash (CrashDumps.Since and Until).</summary>
public sealed record CrashContext(string Reason, string? CrashLine = null, string? SuspectedMod = null, int? ExitCode = null, DateTime? CrashedAtUtc = null)
{
    /// <summary>The oldest write time the crash dump of this report may have.</summary>
    public DateTime DumpSince(DateTime nowUtc) => CrashDumps.Since(Reason, CrashedAtUtc, nowUtc);

    /// <summary>The newest write time the crash dump of this report may have, so a crash bar left open does not send a later session's dump.</summary>
    public DateTime DumpUntil() => CrashDumps.Until(Reason, CrashedAtUtc);

    public static CrashContext Logs { get; } = new(CrashReason.Logs);
}

/// <summary>
/// The consent window of a crash report. It packs the cleaned logs first (off the window
/// thread), then shows every file with its size and the summary, a big Send button and a small
/// Save a copy instead link. Nothing leaves the PC without the Send click. After sending it shows
/// the code big, with Copy and the line Post only this code in Discord, not your log files.
/// </summary>
public sealed class CrashReportWindow : Window
{
    public const string SendText = "Send";
    public const string SaveCopyText = "Save a copy instead";
    public const string SaveCopyTip = "Saves the same zip to this PC instead of sending it. Nothing is sent.";
    public const string CopyText = "Copy";

    private readonly CrashReportRequest _request;
    private readonly Action<string> _log;
    private readonly StackPanel _stack = new() { Margin = new Thickness(20) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 10, 0, 0) };
    private CrashReportPack? _pack;
    private string? _problem;
    private Button? _send;

    public CrashReportWindow(CrashReportRequest request, Action<string>? log = null)
    {
        _request = request;
        _log = log ?? (_ => { });
        _problem = request.Problem;
        Title = TitleFor(request);
        Width = 760;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = BrushOf(14, 20, 24);
        Foreground = BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");
        Content = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ShowPreparing();
        Loaded += async (_, _) => await PrepareAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            DeletePack();
        };
    }

    private bool _closed;

    /// <summary>The window title: Send crash report, Send server crash report or Send my logs.</summary>
    public static string TitleFor(CrashReportRequest request) => request.Reason switch
    {
        CrashReason.ServerCrash => CrashReportUi.ServerCrashButton,
        CrashReason.Crash => CrashReportUi.CrashButton,
        _ => CrashReportUi.SendLogsText
    };

    /// <summary>Opens the consent window over a window.</summary>
    public static void Open(Window? owner, CrashReportRequest request, Action<string>? log = null)
    {
        var window = new CrashReportWindow(request, log);
        if (owner != null && owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
    }

    /// <summary>Save a copy instead, straight from a bar: packs the same cleaned zip and saves it where the player picks.</summary>
    public static async Task SaveCopyAsync(Window? owner, CrashReportRequest request, Action<string> log)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Zip|*.zip",
            FileName = request.FileStem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip",
            Title = SaveCopyText
        };
        if (dialog.ShowDialog(owner) != true)
            return;
        CrashReportPack? pack = null;
        try
        {
            var install = InstallId();
            pack = await Task.Run(() => CrashReports.Prepare(request, install));
            CrashReports.SaveCopy(pack, dialog.FileName);
            Reveal(dialog.FileName);
            log("Saved a copy of the cleaned logs (" + pack.Summary.Files.Count + " files). Nothing was sent. " + dialog.FileName);
        }
        catch (Exception ex)
        {
            // Fire and forget from the bar, so nothing may escape: the error goes to the log.
            log("Could not save the copy. " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (pack != null)
                TryDelete(pack.ZipPath);
        }
    }

    internal static string InstallId()
    {
        var settings = ModReports.LoadSettings();
        var install = ModReports.InstallId(settings);
        ModReports.SaveSettings(settings);
        return install;
    }

    private void ShowPreparing()
    {
        _stack.Children.Clear();
        _stack.Children.Add(Heading(Title));
        _stack.Children.Add(Muted("Getting your logs ready. Your Windows name, PC name, Steam ids, addresses and passwords are taken out first. Nothing is sent yet.", 14));
    }

    // Runs from the async void Loaded handler, so nothing may escape it: any error shows in the
    // window and goes to the log instead of closing the tool.
    private async Task PrepareAsync()
    {
        try
        {
            var install = InstallId();
            // The known crash patterns (crash-signatures.json) come down while the logs are packed, and
            // the triage waits for them only after the packing; offline the kept or built in copy is
            // used. FetchForScreenAsync never throws. Send my logs runs only its allow list, as a hint.
            var download = CrashScreen.RunsTriage(_request.Tool, _request.Reason)
                ? CrashSignatureFile.FetchForScreenAsync("CoodszCrashReports/1")
                : null;
            var pack = await Task.Run(() => CrashReports.Prepare(_request, install,
                signaturesLater: download == null ? null : () => download.GetAwaiter().GetResult()));
            var signatures = download == null ? null : await download;
            if (pack.TriageRan)
                _log(pack.Match == null
                    ? "Crash triage: no known pattern matched (signatures " + signatures?.Version + ")."
                    : "Crash triage: " + pack.Match.Signature.Id + " (" + pack.Match.Signature.Category + ", signatures " + pack.Match.SignaturesVersion + ").");
            if (_closed)
            {
                // The window closed while the logs were packed: the zip is not kept.
                TryDelete(pack.ZipPath);
                return;
            }

            _pack = pack;
            ShowConsent();
        }
        catch (Exception ex)
        {
            _log("Could not pack the logs for the report. " + ex.GetType().Name + ": " + ex.Message);
            if (_closed)
                return;
            _stack.Children.Add(new TextBlock { Text = BannerlordCoopLauncher.Core.Localization.Translator.T(PackFailedText), TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Red(), Margin = new Thickness(0, 10, 0, 0) });
        }
    }

    public const string PackFailedText = "Could not pack the logs. Close this window and try again. The details are in this tool's log.";

    private void ShowConsent()
    {
        var pack = _pack!;
        var mode = CrashScreen.ModeFor(_request.Tool, _request.Reason, pack.TriageRan, pack.Match);
        _stack.Children.Clear();
        _stack.Children.Add(Heading(Title));
        // What the known crash patterns say, at the top, before anything else.
        if (TriageBox(mode, pack.Match) is { } box)
            _stack.Children.Add(box);
        _stack.Children.Add(Muted(_request.Reason == CrashReason.Logs
            ? "Something went wrong but nothing crashed? Send your cleaned logs to Coodsz. You get a short code to post in Discord."
            : "Coodsz gets these cleaned logs privately. You get a short code to post in Discord instead of the files.", 14));

        if (_request.Reason == CrashReason.Logs)
            _stack.Children.Add(ProblemPicker());

        _stack.Children.Add(ExtrasSection());

        _stack.Children.Add(Section("What is sent"));
        _stack.Children.Add(SummaryGrid(pack.Summary));

        _stack.Children.Add(Section("Files in the report"));
        _stack.Children.Add(_fileArea);
        ShowFiles();
        // What was left out, one line each so every line is translated. With a dump to add, the
        // checkbox below speaks for it, so the line that dumps were left out is not shown.
        foreach (var line in pack.Skipped.Distinct().Where(line => pack.Dump == null || line != LogZipBuilder.DumpsLeftOutText))
            _stack.Children.Add(Muted(line, 12, new Thickness(0, 4, 0, 0)));

        if (pack.Dump != null)
            _stack.Children.Add(DumpSection());

        // The dump sentence only where there is a dump and its checkbox shows.
        _stack.Children.Add(Muted(pack.Dump != null ? CrashReports.PrivacyText : CrashReports.PrivacyTextNoDump, 12, new Thickness(0, 10, 0, 0)));

        var show = SmallButton("Show the summary that is sent", "Shows the exact summary text that goes with the zip.");
        show.HorizontalAlignment = HorizontalAlignment.Left;
        show.Margin = new Thickness(0, 8, 0, 0);
        var preview = new TextBox
        {
            IsReadOnly = true,
            Visibility = Visibility.Collapsed,
            Tag = UiLanguage.NoTranslate,
            MaxHeight = 200,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BrushOf(16, 22, 27),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            FlowDirection = FlowDirection.LeftToRight
        };
        show.Click += (_, _) =>
        {
            preview.Text = PreviewText();
            preview.Visibility = preview.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        };
        _stack.Children.Add(show);
        _stack.Children.Add(preview);

        // Your Discord name, right above Send: Send waits for it, Save a copy does not.
        _stack.Children.Add(ContactSection());

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        if (CrashScreen.BigSend(mode))
        {
            _send = CrashReportUi.Big(this, SendText, "Sends this zip and summary to Coodsz. Only Coodsz can open it.", CrashReportUi.Green(this), "📤");
        }
        else
        {
            // A known cause that needs no report: Send is only a small faded link.
            _send = CrashReportUi.Link(this, CrashScreen.SendAnywayText, CrashScreen.SendAnywayTip, BrushOf(142, 160, 174), 13);
            _send.Opacity = 0.75;
            _send.VerticalAlignment = VerticalAlignment.Center;
        }

        System.Windows.Automation.AutomationProperties.SetAutomationId(_send, "CrashReportSend");
        _send.Click += async (_, _) => await SendAsync();
        actions.Children.Add(_send);
        _sendHint = new TextBlock
        {
            Text = CrashContact.NeededText,
            FontSize = 13,
            Foreground = CrashReportUi.Gold(this),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_sendHint, "CrashReportContactNeeded");
        actions.Children.Add(_sendHint);
        UpdateSendReady();
        var save = CrashReportUi.Link(this, SaveCopyText, SaveCopyTip, CrashReportUi.Gold(this), 14);
        save.Margin = new Thickness(20, 0, 0, 0);
        save.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "CrashReportSaveCopy");
        save.Click += async (_, _) => await SaveCopyHereAsync();
        actions.Children.Add(save);
        _stack.Children.Add(actions);
        _stack.Children.Add(_status);
    }

    // The box at the top: No need to report this (a known cause, the fix right there), a known bug
    // being worked on that still wants the logs, or an unknown crash that is worth sending.
    private Border? TriageBox(CrashScreenMode mode, CrashSignatureMatch? match)
    {
        if (mode == CrashScreenMode.None)
            return null;
        var (color, back) = mode switch
        {
            CrashScreenMode.KnownNoReport => (CrashReportUi.Green(this), ModReportUi.GreenBack(this)),
            // Send my logs: a hint, gold like a bug that still wants the logs, never the green No need to report this.
            CrashScreenMode.KnownSendHelps or CrashScreenMode.Hint => (CrashReportUi.Gold(this), ModReportUi.GoldBack(this)),
            _ => (CrashReportUi.Green(this), ModReportUi.GreenBack(this))
        };
        var inner = new StackPanel();
        System.Windows.Automation.AutomationProperties.SetAutomationId(inner, "CrashTriage" + mode);
        inner.Children.Add(new TextBlock
        {
            Text = CrashScreen.Headline(mode, match),
            TextWrapping = TextWrapping.Wrap,
            FontSize = mode == CrashScreenMode.KnownNoReport ? 24 : 18,
            FontWeight = FontWeights.Bold,
            Foreground = color
        });
        if (match != null)
        {
            // The player text in the tool language (crash-signatures.json translations), English when there is none.
            var text = match.Localized(BannerlordCoopLauncher.Core.Localization.Translator.Current);
            inner.Children.Add(new TextBlock { Text = text.Title, Tag = UiLanguage.NoTranslate, TextWrapping = TextWrapping.Wrap, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            inner.Children.Add(new TextBlock { Text = CrashScreen.WhatHappened, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = color, Margin = new Thickness(0, 10, 0, 0) });
            inner.Children.Add(new TextBlock { Text = text.WhatHappened, Tag = UiLanguage.NoTranslate, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 2, 0, 0) });
            inner.Children.Add(new TextBlock { Text = CrashScreen.WhatToDo, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = color, Margin = new Thickness(0, 10, 0, 0) });
            inner.Children.Add(new TextBlock { Text = text.WhatToDo, Tag = UiLanguage.NoTranslate, TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
            // Only a GitHub page opens from a downloaded file.
            if (match.Signature.Upstream is { } upstream && upstream.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                var more = CrashReportUi.Link(this, CrashScreen.MoreInfoText, upstream, color, 13);
                more.Margin = new Thickness(0, 10, 0, 0);
                more.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(upstream) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                };
                inner.Children.Add(more);
            }

            // Every answer the tool shows goes into My reports. A known cause also gets Still not working?.
            KeepKnownFix(match.Signature.Id, text.Title + "\n\n" + text.WhatHappened + "\n\n" + text.WhatToDo);
            if (mode == CrashScreenMode.KnownNoReport)
            {
                var said = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Tag = UiLanguage.NoTranslate, Margin = new Thickness(0, 4, 0, 0) };
                var still = ReportRepliesUi.StillNotWorkingButton(this, null, (line, good) =>
                {
                    said.Text = BannerlordCoopLauncher.Core.Localization.Translator.T(line);
                    said.Foreground = good ? CrashReportUi.Green(this) : Red();
                });
                still.Margin = new Thickness(0, 10, 0, 0);
                inner.Children.Add(still);
                inner.Children.Add(said);
            }
        }

        return new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(2),
            Background = back,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 14),
            Margin = new Thickness(0, 4, 0, 12),
            Child = inner
        };
    }

    private void KeepKnownFix(string signatureId, string text)
    {
        try
        {
            ReportReplyStore.ForThisTool()?.SaveKnownFix(signatureId, text);
            _knownFixId = signatureId;
            ReportRepliesUi.NotifyChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Could not keep the known fix for My reports. " + ex.Message);
        }
    }

    // The summary that is sent: the problem picked for Send my logs and the Discord name. The name
    // is only ever in the summary, never in the zip.
    private CrashReportSummary SummaryOf(CrashReportPack pack) =>
        CrashContact.WithContact(_request.Reason == CrashReason.Logs ? CrashReports.WithProblem(pack.Summary, _problem) : pack.Summary, _contact?.Text);

    // ----- Your Discord name (needed for Send, kept on this PC) -----

    private TextBox? _contact;
    private TextBlock? _sendHint;
    private bool _sending;

    private FrameworkElement ContactSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(Section(CrashContact.Label));
        _contact = new TextBox
        {
            Text = CrashContact.Load(),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            MaxLength = CrashContact.MaxChars,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 360,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6),
            FontSize = 13,
            Background = BrushOf(16, 22, 27),
            Foreground = BrushOf(213, 222, 230),
            CaretBrush = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            // Typed text is never translated, only the label and the line under it are.
            Tag = UiLanguage.NoTranslate
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_contact, "CrashReportContact");
        System.Windows.Automation.AutomationProperties.SetName(_contact, BannerlordCoopLauncher.Core.Localization.Translator.T(CrashContact.Label));
        _contact.TextChanged += (_, _) => UpdateSendReady();
        panel.Children.Add(_contact);
        panel.Children.Add(Muted(CrashContact.Note, 12, new Thickness(0, 4, 0, 0)));
        _contactPanel = panel;
        return panel;
    }

    private StackPanel? _contactPanel;

    // Send is on only with a Discord name in the box (and not while a send runs); the hint next to it says why.
    private void UpdateSendReady()
    {
        var ready = CrashContact.CanSend(_contact?.Text);
        if (_send != null)
            _send.IsEnabled = ready && !_sending;
        if (_sendHint != null)
            _sendHint.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
    }

    // The summary as Show the summary that is sent shows it: with the player's file and pictures
    // listed, the crash dump when it goes too, and the cleaned explanation under it exactly as it goes into the zip.
    private string PreviewText()
    {
        var notes = ReportExtras.CleanNotes(_notes?.Text, _request.Secrets);
        var summary = ReportExtras.PreviewSummary(SummaryOf(_pack!), notes, _shots);
        if (DumpGoes(notes) is { } dump)
            summary.Files.Add(new CrashReportFile(dump.Entry, dump.Size));
        var text = CrashReports.SummaryJson(summary);
        return notes.Length == 0 ? text : text + Environment.NewLine + Environment.NewLine + ReportExtras.NotesName + Environment.NewLine + notes;
    }

    // ----- Add the crash dump (only when one was found and fits) -----

    private readonly StackPanel _fileArea = new();
    private TextBlock? _dumpWarning;

    // The dump that goes with the next Send or Save: ticked, and it fits with the screenshots and the note.
    private CrashDumpPick? DumpGoes(string? cleanedNotes = null)
    {
        if (_pack?.Dump is not { } dump || !_request.IncludeDump)
            return null;
        var notes = cleanedNotes ?? ReportExtras.CleanNotes(_notes?.Text, _request.Secrets);
        var extra = _shots.Sum(shot => shot.Size) + System.Text.Encoding.UTF8.GetByteCount(notes);
        return CrashDumps.FitsWith(dump, extra) ? dump : null;
    }

    // The file count and list, with the crash dump while it goes (ticked and room for it), so the list is what is sent.
    private void ShowFiles()
    {
        var pack = _pack!;
        var files = pack.Summary.Files.ToList();
        var zipped = pack.ZipBytes;
        _dumpListed = DumpGoes() != null;
        if (pack.Dump is { } dump && _dumpListed)
        {
            files.Add(new CrashReportFile(dump.Entry, dump.Size));
            zipped += dump.CompressedSize;
        }

        _fileArea.Children.Clear();
        _fileArea.Children.Add(Muted(files.Count + " files, " + CrashReports.SizeText(pack.TotalBytes) + " of text, " + CrashReports.SizeText(zipped) + " zipped.", 13));
        _fileArea.Children.Add(FileList(files));
    }

    private FrameworkElement DumpSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        // On by default and for this send only: a new report asks again.
        var box = new CheckBox
        {
            Content = CrashDumps.CheckboxText,
            ToolTip = CrashDumps.CheckboxTip,
            IsChecked = _request.IncludeDump,
            Foreground = BrushOf(213, 222, 230),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "CrashReportAddDump");
        box.Checked += (_, _) => SetIncludeDump(true);
        box.Unchecked += (_, _) => SetIncludeDump(false);
        panel.Children.Add(box);
        panel.Children.Add(Muted(CrashDumps.CheckboxNote, 12, new Thickness(22, 2, 0, 0)));
        _dumpWarning = Muted(CrashDumps.TooBigWithScreenshotsText, 12, new Thickness(22, 4, 0, 0));
        _dumpWarning.Foreground = CrashReportUi.Gold(this);
        panel.Children.Add(_dumpWarning);
        UpdateDumpWarning();
        _dumpPanel = panel;
        return panel;
    }

    private StackPanel? _dumpPanel;

    private bool _dumpListed;

    private void SetIncludeDump(bool include)
    {
        _request.IncludeDump = include;
        UpdateDumpWarning();
    }

    // The warning shows while the dump is ticked but the screenshots leave it no room, and the file
    // list follows whether the dump goes (rebuilt only when that changes, not on every key).
    private void UpdateDumpWarning()
    {
        if (_pack?.Dump == null || _dumpWarning == null)
            return;
        var goes = DumpGoes() != null;
        _dumpWarning.Visibility = _request.IncludeDump && !goes ? Visibility.Visible : Visibility.Collapsed;
        if (goes != _dumpListed)
            ShowFiles();
    }

    // ----- Tell us what happened and Add screenshots (both optional) -----

    private TextBox? _notes;
    private readonly List<ReportScreenshot> _shots = new();
    private readonly WrapPanel _shotList = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _shotStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private int _adding;

    private StackPanel? _extras;

    private FrameworkElement ExtrasSection()
    {
        var panel = new StackPanel();
        _extras = panel;
        panel.Children.Add(Section(ReportExtras.NotesLabel));
        _notes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = ReportExtras.MaxNotesChars,
            MinHeight = 80,
            MaxHeight = 160,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6),
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = BrushOf(16, 22, 27),
            Foreground = BrushOf(213, 222, 230),
            CaretBrush = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            // Typed text is never translated, only the tip is.
            ToolTip = ReportExtras.NotesTip
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_notes, "CrashReportNotes");
        var counter = new TextBlock { Text = ReportExtras.CounterText(0), Tag = UiLanguage.NoTranslate, FontSize = 12, Foreground = BrushOf(142, 160, 174), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        _notes.TextChanged += (_, _) =>
        {
            counter.Text = ReportExtras.CounterText(_notes.Text.Length);
            UpdateDumpWarning();
        };
        panel.Children.Add(_notes);
        panel.Children.Add(counter);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var add = SmallButton(ReportExtras.AddScreenshotsText, ReportExtras.AddScreenshotsTip);
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "CrashReportAddScreenshots");
        add.Click += async (_, _) => await PickScreenshotsAsync();
        row.Children.Add(add);
        row.Children.Add(Muted(ReportExtras.PasteHint, 12, new Thickness(10, 0, 0, 0)));
        ((TextBlock)row.Children[1]).VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(row);
        panel.Children.Add(_shotList);
        panel.Children.Add(_shotStatus);
        panel.Children.Add(Muted(ReportExtras.ScreenshotNote, 12, new Thickness(0, 4, 0, 0)));

        PreviewKeyDown -= OnPreviewKeyDown;
        PreviewKeyDown += OnPreviewKeyDown;
        return panel;
    }

    // Ctrl+V with a picture (or picture files) on the clipboard adds it. Text still pastes into the box as usual.
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || _pack == null || _send == null || _sending)
            return;
        try
        {
            // Text pastes into the box as usual, even when the clipboard also holds a picture (Excel, Word, browsers).
            if (Keyboard.FocusedElement is TextBox && Clipboard.ContainsText())
                return;
            if (Clipboard.ContainsImage())
            {
                e.Handled = true;
                var image = Clipboard.GetImage();
                if (image == null)
                    return;
                if ((long)image.PixelWidth * image.PixelHeight > ReportExtras.MaxScreenshotPixels)
                {
                    ShotSay(ReportExtras.TooLargeText);
                    return;
                }

                // Frozen, so the picture is encoded off the window thread.
                image.Freeze();
                await AddScreenshotsAsync(new[] { (Name: "Pasted picture", Load: (Func<byte[]>)(() => ReportScreenshotImages.EncodeClipboard(image))) });
            }
            else if (Clipboard.ContainsFileDropList() && !(Keyboard.FocusedElement is TextBox))
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count == 0)
                    return;
                e.Handled = true;
                await AddFilesAsync(files);
            }
        }
        catch (Exception ex)
        {
            // From an async void key handler, so nothing may escape.
            _log("Could not paste the picture. " + ex.GetType().Name + ": " + ex.Message);
            ShotSay(ReportExtras.CouldNotReadText);
        }
    }

    private async Task PickScreenshotsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PNG or JPG|*.png;*.jpg;*.jpeg|All files|*.*",
            Multiselect = true,
            Title = ReportExtras.AddScreenshotsText
        };
        if (dialog.ShowDialog(this) != true)
            return;
        await AddFilesAsync(dialog.FileNames);
    }

    private Task AddFilesAsync(IEnumerable<string> paths) =>
        AddScreenshotsAsync(paths.Select(path => (Name: Path.GetFileName(path), Load: (Func<byte[]>)(() => ReportScreenshotImages.ReadFile(path)))));

    // Each picture is read and re-encoded off the window thread, then checked against the caps
    // here, one at a time, so the 5 picture and size caps always hold.
    private async Task AddScreenshotsAsync(IEnumerable<(string Name, Func<byte[]> Load)> items)
    {
        _adding++;
        try
        {
            ShotSay(null);
            foreach (var (name, load) in items)
            {
                if (_shots.Count >= ReportExtras.MaxScreenshots)
                {
                    ShotSay(ReportExtras.TooManyText);
                    return;
                }

                ReportScreenshot shot;
                try
                {
                    shot = await Task.Run(() => ReportScreenshotImages.Prepare(load(), name));
                }
                catch (InvalidDataException ex)
                {
                    ShotSay(ex.Message);
                    continue;
                }
                catch (Exception ex)
                {
                    // From async void click and key handlers, so nothing may escape: WPF imaging
                    // throws many kinds of errors on an odd file, and none of them may close the tool.
                    _log("Could not read the picture " + name + ". " + ex.GetType().Name + ": " + ex.Message);
                    ShotSay(ReportExtras.CouldNotReadText);
                    continue;
                }

                if (_closed)
                    return;
                if (ReportExtras.CheckAdd(_shots, shot) is { } problem)
                {
                    ShotSay(problem);
                    continue;
                }

                _shots.Add(shot);
                ShowShots();
            }
        }
        finally
        {
            _adding--;
        }
    }

    private void ShowShots()
    {
        UpdateDumpWarning();
        _shotList.Children.Clear();
        foreach (var shot in _shots.ToList())
        {
            var card = new StackPanel { Width = 132, Margin = new Thickness(0, 0, 8, 6) };
            var image = new Image { Height = 74, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = shot.DisplayName };
            try
            {
                image.Source = ReportScreenshotImages.Thumbnail(shot.Bytes);
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or System.Runtime.InteropServices.COMException)
            {
                // The list still shows the name.
            }

            card.Children.Add(image);
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 0) };
            var remove = new Button
            {
                Content = "✕",
                ToolTip = ReportExtras.RemoveTip,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Background = BrushOf(27, 40, 51),
                Foreground = BrushOf(213, 222, 230),
                BorderBrush = BrushOf(58, 76, 92)
            };
            System.Windows.Automation.AutomationProperties.SetName(remove, ReportExtras.RemoveTip);
            remove.Click += (_, _) =>
            {
                _shots.Remove(shot);
                ShotSay(null);
                ShowShots();
            };
            DockPanel.SetDock(remove, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(new TextBlock
            {
                Text = shot.DisplayName + " (" + CrashReports.SizeText(shot.Size) + ")",
                Tag = UiLanguage.NoTranslate,
                FontSize = 11,
                Foreground = BrushOf(142, 160, 174),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = shot.DisplayName
            });
            card.Children.Add(line);
            _shotList.Children.Add(card);
        }
    }

    private void ShotSay(string? text)
    {
        _shotStatus.Text = text == null ? "" : BannerlordCoopLauncher.Core.Localization.Translator.T(text);
        _shotStatus.Foreground = Red();
        _shotStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool HasExtras => CrashReports.HasExtras(_notes?.Text, _shots);

    // The zip that is sent or saved: the packed one, or with nothing added. With extras or the
    // ticked crash dump a new zip is made next to it (the caller deletes it when it is not the packed one).
    private async Task<CrashReportPack> FinalPackAsync()
    {
        var pack = _pack!;
        var includeDump = _request.IncludeDump && pack.Dump != null;
        if (!HasExtras && !includeDump)
            return pack;
        var notes = _notes?.Text;
        var shots = _shots.ToList();
        return await Task.Run(() => CrashReports.WithExtras(_request, pack, notes, shots, includeDump));
    }

    private void DeleteIfExtra(CrashReportPack? final)
    {
        if (final != null && !ReferenceEquals(final, _pack))
            TryDelete(final.ZipPath);
    }

    private FrameworkElement ProblemPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = "What went wrong?", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var box = new ComboBox { MinWidth = 300, FontSize = 14, ToolTip = "Pick what went wrong, so Coodsz knows where to look. It goes with the summary." };
        box.Items.Add(new ComboBoxItem { Tag = null, Content = "Pick one (optional)" });
        foreach (var (id, label) in CrashProblem.Choices)
            box.Items.Add(new ComboBoxItem { Tag = id, Content = label });
        box.SelectedIndex = Math.Max(0, CrashProblem.Choices.ToList().FindIndex(choice => choice.Id == _problem) + 1);
        box.SelectionChanged += (_, _) => _problem = (box.SelectedItem as ComboBoxItem)?.Tag as string;
        row.Children.Add(box);
        return row;
    }

    private Grid SummaryGrid(CrashReportSummary summary)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new List<(string Label, string Value)>
        {
            ("Tool", CrashReportUi.ToolName(summary.Tool, summary.Side) + " " + summary.ToolVersion),
            ("Coop", summary.Coop.Length > 0 ? summary.Coop : "Not found"),
            ("Bannerlord", summary.Game.Length > 0 ? summary.Game : "Not found"),
            ("Crash line", summary.CrashLine.Length > 0 ? summary.CrashLine : summary.Reason == CrashReason.Logs ? "None, nothing crashed" : "None found in the logs")
        };
        if (summary.SuspectedMod != null)
            rows.Add(("Suspected mod", summary.SuspectedMod));
        foreach (var (label, value) in rows)
        {
            var index = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, FontSize = 13, Foreground = BrushOf(142, 160, 174), Margin = new Thickness(0, 2, 16, 2) };
            var text = new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Tag = UiLanguage.NoTranslate };
            if (value is "Not found" or "None, nothing crashed" or "None found in the logs")
                text.Tag = null;
            Grid.SetRow(name, index);
            Grid.SetRow(text, index);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static FrameworkElement FileList(IReadOnlyList<CrashReportFile> files)
    {
        var list = new StackPanel();
        foreach (var file in files)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 8, 1) };
            var size = new TextBlock { Text = CrashReports.SizeText(file.Size), Tag = UiLanguage.NoTranslate, FontSize = 12, Foreground = BrushOf(142, 160, 174), Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(new TextBlock { Text = file.Name, Tag = UiLanguage.NoTranslate, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Name });
            list.Children.Add(row);
        }

        return new Border
        {
            BorderBrush = BrushOf(58, 76, 92),
            BorderThickness = new Thickness(1),
            Background = BrushOf(16, 22, 27),
            Padding = new Thickness(8, 6, 4, 6),
            Margin = new Thickness(0, 6, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
            Child = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
    }

    private async Task SendAsync()
    {
        if (_pack == null || _send == null)
            return;
        if (_adding > 0)
        {
            Say(WaitForScreenshotsText, good: false);
            return;
        }

        if (!CrashContact.CanSend(_contact?.Text))
        {
            Say(CrashContact.NeededText, good: false);
            return;
        }

        // Kept for the next report, so next time the player just clicks Send.
        CrashContact.Save(_contact?.Text);
        _sending = true;
        _send.IsEnabled = false;
        // What is sent is what the window shows: no picture or text changes while it goes.
        SetExtrasEnabled(false);
        Say("Sending the report.", good: true);
        CrashReportSendResult result;
        CrashReportPack? final = null;
        try
        {
            final = await FinalPackAsync();
        }
        catch (Exception ex)
        {
            // From the async void click handler, so nothing may escape.
            _log("Could not pack the report. " + ex.GetType().Name + ": " + ex.Message);
            if (_closed)
                return;
            _sending = false;
            SetExtrasEnabled(true);
            UpdateSendReady();
            Say(PackExtrasFailedText, good: false);
            return;
        }

        // The reply key goes only into the summary that is sent, never into the zip, the preview or a log.
        // It is kept on this PC only when the worker says it can answer (ReportReplies).
        var replyKey = ReportReplies.NewKey();
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoodszCrashReports/1");
            result = await CrashReports.SendAsync(http, final, CrashReports.WithReplyKey(SummaryOf(final), replyKey));
        }
        catch (Exception ex)
        {
            // From the async void click handler, so nothing may escape.
            _log("Could not send the report. " + ex.GetType().Name + ": " + ex.Message);
            result = new CrashReportSendResult(CrashReportSendStatus.Failed, null, "Could not send the report. Check the internet connection and try again, or click Save a copy instead.");
        }
        finally
        {
            DeleteIfExtra(final);
        }

        if (result.Status == CrashReportSendStatus.Sent && result.Code != null)
        {
            _log("Sent a crash report. The code is " + result.Code + ". " + CrashReports.PostOnlyCode);
            DeletePack();
            var kept = await KeepReplyKeyAsync(result, replyKey);
            if (_closed)
                return;
            ShowCode(result.Code, kept);
            return;
        }

        // The worker's own reason goes to the tool's log, so a refused report can be looked into.
        _log("The crash report was not sent (" + result.Status + "). " + (result.Detail ?? result.Message));
        if (_closed)
            return;
        _sending = false;
        SetExtrasEnabled(true);
        UpdateSendReady();
        Say(result.Message, good: false);
    }

    public const string PackExtrasFailedText = "Could not add your text and screenshots to the report. Remove a screenshot and try again. The details are in this tool's log.";

    // The worker said it can answer this report: the key stays next to the code for the reply check
    // (ReportReplies.KeepSentAsync, off the window thread, tried again while the other exe holds the
    // file). True only when the key is in the file, so the code screen promises an answer only then.
    private async Task<bool> KeepReplyKeyAsync(CrashReportSendResult result, string replyKey)
    {
        var store = ReportReplyStore.ForThisTool();
        if (store == null || result.Code == null)
            return false;
        // The short title My reports shows: the known cause, else the crash line, else the reason's own title.
        var title = _pack?.Match?.Title is { Length: > 0 } known ? known : _request.CrashLine;
        var kept = await ReportReplies.KeepSentAsync(store, result.Code, result.Replies ? replyKey : null, _knownFixId, _log, title: title, reason: _request.Reason);
        ReportRepliesUi.NotifyChanged();
        return kept;
    }

    private string? _knownFixId;

    private void SetExtrasEnabled(bool enabled)
    {
        if (_extras != null)
            _extras.IsEnabled = enabled;
        if (_contactPanel != null)
            _contactPanel.IsEnabled = enabled;
        if (_dumpPanel != null)
            _dumpPanel.IsEnabled = enabled;
    }

    private void ShowCode(string code, bool replies = false)
    {
        _stack.Children.Clear();
        _stack.Children.Add(Heading("Sent. Your code is"));
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            Tag = UiLanguage.NoTranslate,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 48,
            FontWeight = FontWeights.Bold,
            Foreground = CrashReportUi.Green(this),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, 4, 0, 4)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "CrashReportCode");
        _stack.Children.Add(box);
        var copy = CrashReportUi.Big(this, CopyText, "Copies the code, ready to paste in Discord.", CrashReportUi.Green(this), "📋");
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
                Say("Copied. Paste it in Discord.", good: true);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Say("Could not copy. Select the code and press Ctrl+C.", good: false);
            }
        };
        _stack.Children.Add(copy);
        _stack.Children.Add(new TextBlock
        {
            Text = CrashReports.PostOnlyCode,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = CrashReportUi.Gold(this),
            Margin = new Thickness(0, 14, 0, 0)
        });
        _stack.Children.Add(Muted("Coodsz keeps the report for 30 days. The code is also in this tool's log.", 13, new Thickness(0, 6, 0, 0)));
        if (replies)
            _stack.Children.Add(Muted(ReportReplyText.AnswersHere, 13, new Thickness(0, 6, 0, 0)));
        _stack.Children.Add(_status);
        _status.Text = "";
        var close = SmallButton("Close", "Closes this window.");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 14, 0, 0);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        _stack.Children.Add(close);
    }

    public const string WaitForScreenshotsText = "Wait a moment until the screenshots are added, then try again.";

    private async Task SaveCopyHereAsync()
    {
        if (_pack == null)
            return;
        if (_adding > 0)
        {
            Say(WaitForScreenshotsText, good: false);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Zip|*.zip",
            FileName = _request.FileStem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip",
            Title = SaveCopyText
        };
        if (dialog.ShowDialog(this) != true)
            return;
        CrashReportPack? final = null;
        try
        {
            final = await FinalPackAsync();
            CrashReports.SaveCopy(final, dialog.FileName);
            Reveal(dialog.FileName);
            _log("Saved a copy of the cleaned logs (" + final.Summary.Files.Count + " files). Nothing was sent. " + dialog.FileName);
            if (!_closed)
                Say("Saved a copy. Nothing was sent.", good: true);
        }
        catch (Exception ex)
        {
            // From the async void click handler, so nothing may escape.
            _log("Could not save the copy. " + ex.GetType().Name + ": " + ex.Message);
            if (!_closed)
                Say("Could not save the copy. " + ex.Message, good: false);
        }
        finally
        {
            DeleteIfExtra(final);
        }
    }

    private void DeletePack()
    {
        if (_pack != null)
            TryDelete(_pack.ZipPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private void Say(string text, bool good)
    {
        _status.Text = BannerlordCoopLauncher.Core.Localization.Translator.T(text);
        _status.Foreground = good ? CrashReportUi.Green(this) : Red();
    }

    private Brush Red() => ModReportUi.Red(this);

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Palatino Linotype"),
        FontSize = 26,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 0)
    };

    private static TextBlock Muted(string text, double size = 13, Thickness? margin = null) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Foreground = BrushOf(142, 160, 174),
        Margin = margin ?? new Thickness(0)
    };

    private static Button SmallButton(string text, string tip) => new()
    {
        Content = text,
        ToolTip = tip,
        Padding = new Thickness(12, 4, 12, 4),
        FontSize = 13,
        Cursor = Cursors.Hand,
        Background = BrushOf(27, 40, 51),
        Foreground = BrushOf(213, 222, 230),
        BorderBrush = BrushOf(58, 76, 92)
    };

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => Coodsz.Ui.ToolPalette.Brush(r, g, b);
}

/// <summary>
/// The crash report entry points every Coodsz tool shows: the always visible Send my logs button,
/// the small Something went wrong? Send your logs link next to Connect or Start server, and the
/// big Send crash report and Send server crash report buttons of the bars. The colors come from
/// the window's Report brushes (ModReportUi), so each tool keeps its own palette.
/// </summary>
public static class CrashReportUi
{
    public const string SendLogsText = "Send my logs";
    public const string SendLogsTip = "Stuck joining, a desync, lag or something else? Sends your cleaned logs to Coodsz and gives you a short code to post in Discord. You see every file before anything is sent.";
    public const string LinkText = "Something went wrong? Send your logs";
    public const string LinkTip = "Opens Send my logs. You see every file before anything is sent.";
    public const string CrashButton = "Send crash report";
    public const string CrashButtonTip = "Packs the cleaned crash logs and shows you every file first. Nothing is sent until you click Send. You get a short code to post in Discord.";
    public const string ServerCrashButton = "Send server crash report";
    public const string ServerCrashButtonTip = "Packs the cleaned server logs and shows you every file first. Nothing is sent until you click Send. You get a short code to post in Discord.";

    public static Brush Red(FrameworkElement scope) => ModReportUi.Red(scope);
    public static Brush Gold(FrameworkElement scope) => ModReportUi.Gold(scope);
    public static Brush Green(FrameworkElement scope) => ModReportUi.Green(scope);

    /// <summary>The name of a tool for the summary, like Coop Client Launcher or TAOM server.</summary>
    public static string ToolName(string tool, string side) => tool switch
    {
        "taom" => "TAOM " + (side == "server" ? "server" : "client"),
        "ad1259" => "AD1259 " + (side == "server" ? "server" : "client"),
        "be" => "Burning Empires " + (side == "server" ? "server" : "client"),
        "rot" => "Realm of Thrones " + (side == "server" ? "server" : "client"),
        _ => side == "server" ? "Coop Server Tool" : "Coop Client Launcher"
    };

    /// <summary>A big filled button, the size of the crash bar's main button.</summary>
    public static Button Big(FrameworkElement scope, string text, string tip, Brush fill, string? icon = null) =>
        ModReportUi.Filled(scope, text, tip, fill, icon, size: 19, padding: new Thickness(26, 12, 26, 12));

    /// <summary>The always visible Send my logs button for the main page.</summary>
    public static Button SendLogsButton(FrameworkElement scope, Action click)
    {
        var button = ModReportUi.Filled(scope, SendLogsText, SendLogsTip, Gold(scope), "📤");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "SendLogsButton");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A small underlined link in one accent color.</summary>
    public static Button Link(FrameworkElement scope, string text, string tip, Brush color, double size = 14, string? icon = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null)
            content.Children.Add(new TextBlock { Text = icon, Tag = UiLanguage.NoTranslate, FontSize = size, Foreground = color, Margin = new Thickness(0, 0, 6, 0) });
        content.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = color, TextDecorations = TextDecorations.Underline });
        return new Button
        {
            Content = content,
            ToolTip = tip,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            Template = ModReportUi.ButtonTemplate()
        };
    }

    /// <summary>The Something went wrong? Send your logs link for next to Connect or Start server.</summary>
    public static Button SomethingWrongLink(FrameworkElement scope, Action click)
    {
        var link = Link(scope, LinkText, LinkTip, Gold(scope), 14, "📤");
        link.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(link, "SendLogsLink");
        link.Click += (_, _) => click();
        return link;
    }

    /// <summary>The small Save a copy instead link of a bar.</summary>
    public static Button SaveCopyLink(FrameworkElement scope, Action click)
    {
        var link = Link(scope, CrashReportWindow.SaveCopyText, CrashReportWindow.SaveCopyTip, Gold(scope), 13);
        link.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetAutomationId(link, "SaveCopyLink");
        link.Click += (_, _) => click();
        return link;
    }

    /// <summary>The one time card text, the same in every tool.</summary>
    public const string IntroTitle = "New: send crash reports right from here";
    public const string IntroText = "Crashed, stuck joining or lagging? Click Send my logs. You see every file first, then you get a short code to post in Discord instead of zip files.";
    public const string IntroButton = "Try it";
}

/// <summary>
/// The bar at the top of a server tool: the one time New: send crash reports right from here card,
/// and after the server crashed or stopped by itself the red bar whose big button is Send server
/// crash report, with the server logs preselected. Nothing is sent from the bar: the button opens
/// the consent window.
/// </summary>
public sealed class ServerCrashBar
{
    public const string CrashTitle = "The server crashed";
    public const string CrashText = "Send the server logs to Coodsz in one click. You see every file first, then you get a short code to post in Discord.";

    private readonly Window _owner;
    private readonly ContentControl _host;
    private readonly string _tool;
    private readonly Func<CrashContext, CrashReportRequest> _request;
    private readonly Action<string> _log;

    public ServerCrashBar(Window owner, ContentControl host, string tool, Func<CrashContext, CrashReportRequest> request, Action<string> log)
    {
        _owner = owner;
        _host = host;
        _tool = tool;
        _request = request;
        _log = log;
        _host.Visibility = Visibility.Collapsed;
    }

    /// <summary>True while the crash bar shows (it replaces the card).</summary>
    public bool ShowsCrash { get; private set; }

    /// <summary>Send my logs, from the button, the link or the card.</summary>
    public void OpenLogs() => CrashReportWindow.Open(_owner, _request(CrashContext.Logs), _log);

    public void ShowIntroOnce()
    {
        // One thing at a time: a start that opens What's new (or the language pick or the license)
        // keeps the card for a later start.
        if (ShowsCrash || UiLanguage.StartupPopupThisStart || !CrashReports.ShouldShowIntro(ModReports.LoadSettings(), _tool, "server"))
            return;
        var green = CrashReportUi.Green(_host);
        var tryIt = ModReportUi.Filled(_host, CrashReportUi.IntroButton, CrashReportUi.SendLogsTip, green, "📤");
        tryIt.Click += (_, _) =>
        {
            IntroDone();
            OpenLogs();
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = CrashReportUi.IntroTitle, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = green, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = CrashReportUi.IntroText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Show(ModReportUi.Frame(_host, green, ModReportUi.GreenBack(_host), text, new[] { (UIElement)tryIt },
            "Hides this card now. Send my logs stays on the page.", IntroDone));
        // Seen once is enough: the card stays for this start and never comes back, also when nobody pressed it.
        _introShown = true;
        MarkIntroSeen();
    }

    /// <summary>The server crashed or stopped by itself: the red bar with Send server crash report.</summary>
    public void ServerCrashed(string? crashLine, string? title = null, int? exitCode = null)
    {
        IntroDone();
        var context = new CrashContext(CrashReason.ServerCrash, crashLine, ExitCode: exitCode, CrashedAtUtc: DateTime.UtcNow);
        var red = CrashReportUi.Red(_host);
        var send = CrashReportUi.Big(_host, CrashReportUi.ServerCrashButton, CrashReportUi.ServerCrashButtonTip, red, "📤");
        System.Windows.Automation.AutomationProperties.SetAutomationId(send, "SendServerCrashReport");
        send.Click += (_, _) => CrashReportWindow.Open(_owner, _request(context), _log);
        var save = CrashReportUi.SaveCopyLink(_host, () => _ = CrashReportWindow.SaveCopyAsync(_owner, _request(context), _log));
        save.Margin = new Thickness(20, 0, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title ?? CrashTitle, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = red });
        text.Children.Add(new TextBlock { Text = CrashText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (!string.IsNullOrWhiteSpace(crashLine))
            text.Children.Add(new TextBlock { Text = CrashReports.CleanCrashLine(crashLine), Tag = UiLanguage.NoTranslate, FontSize = 13, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = red, Margin = new Thickness(0, 4, 0, 0) });
        ShowsCrash = true;
        Show(ModReportUi.Frame(_host, red, ModReportUi.RedBack(_host), text, new UIElement[] { send, save }, "Hides this bar.", Hide, buttonsBelow: true));
    }

    /// <summary>The server started again: the crash bar of the last run goes. The one time card stays.</summary>
    public void ServerStarted()
    {
        if (ShowsCrash)
            Hide();
    }

    public void Hide()
    {
        ShowsCrash = false;
        _host.Content = null;
        _host.Visibility = Visibility.Collapsed;
    }

    private void Show(FrameworkElement content)
    {
        _host.Content = content;
        _host.Visibility = Visibility.Visible;
    }

    private bool _introShown;

    private void IntroDone()
    {
        MarkIntroSeen();
        if (_introShown && !ShowsCrash)
            Hide();
        _introShown = false;
    }

    private void MarkIntroSeen()
    {
        var settings = ModReports.LoadSettings();
        if (!CrashReports.ShouldShowIntro(settings, _tool, "server"))
            return;
        CrashReports.MarkIntroSeen(settings, _tool, "server");
        ModReports.SaveSettings(settings);
    }
}

/// <summary>
/// The WPF side of Add screenshots: reads a picture, checks it is a PNG or JPEG by its first
/// bytes, makes a very large one smaller and encodes it again from bare pixels, so no EXIF, text
/// or color profile from the original file is kept. ReportExtras packs it.
/// </summary>
public static class ReportScreenshotImages
{
    /// <summary>A PNG that comes out larger than this is sent as a JPEG instead.</summary>
    public const long PngKeepBytes = 1_500_000;

    /// <summary>Reads a picture file, refusing one that is far too large before reading it.</summary>
    public static byte[] ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > ReportExtras.MaxScreenshotFileBytes)
            throw new InvalidDataException(ReportExtras.TooLargeText);
        return File.ReadAllBytes(path);
    }

    /// <summary>The picture as it is packed: re-encoded from its pixels, at most MaxScreenshotEdge on its longest side.</summary>
    public static ReportScreenshot Prepare(byte[] bytes, string displayName)
    {
        var kind = ReportExtras.DetectImage(bytes);
        if (kind == ReportImageKind.None)
            throw new InvalidDataException(ReportExtras.OnlyPicturesText);

        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException(ReportExtras.CouldNotReadText);
        BitmapSource source = decoder.Frames[0];
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || (long)source.PixelWidth * source.PixelHeight > ReportExtras.MaxScreenshotPixels)
            throw new InvalidDataException(ReportExtras.TooLargeText);

        var longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest > ReportExtras.MaxScreenshotEdge)
        {
            var scale = (double)ReportExtras.MaxScreenshotEdge / longest;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        var alpha = kind == ReportImageKind.Png;
        var clean = Bare(source, alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24);
        byte[] packed;
        if (kind == ReportImageKind.Png)
        {
            packed = Encode(new PngBitmapEncoder(), clean);
            if (packed.LongLength > PngKeepBytes)
            {
                kind = ReportImageKind.Jpeg;
                packed = Jpeg(Bare(clean, PixelFormats.Bgr24));
            }
        }
        else
        {
            packed = Jpeg(clean);
        }

        packed = ReportExtras.StripMetadata(packed) ?? throw new InvalidDataException(ReportExtras.CouldNotReadText);
        return new ReportScreenshot(string.IsNullOrWhiteSpace(displayName) ? "picture" : displayName, packed, kind);
    }

    /// <summary>A picture from the clipboard as PNG bytes, ready for Prepare.</summary>
    public static byte[] EncodeClipboard(BitmapSource image) => Encode(new PngBitmapEncoder(), Bare(image, PixelFormats.Bgra32));

    /// <summary>A small frozen thumbnail of a packed picture for the list.</summary>
    public static BitmapSource Thumbnail(byte[] bytes)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes, writable: false);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelHeight = 74;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // A JPEG at 90, or at 75 when that is still large.
    private static byte[] Jpeg(BitmapSource source)
    {
        var bytes = Encode(new JpegBitmapEncoder { QualityLevel = 90 }, source);
        return bytes.LongLength > PngKeepBytes ? Encode(new JpegBitmapEncoder { QualityLevel = 75 }, source) : bytes;
    }

    // Copies the pixels into a new bitmap, so nothing but the pixels is carried over.
    private static BitmapSource Bare(BitmapSource source, PixelFormat format)
    {
        var converted = source.Format == format ? source : new FormatConvertedBitmap(source, format, null, 0);
        var stride = (converted.PixelWidth * format.BitsPerPixel + 7) / 8;
        var pixels = new byte[(long)stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var bare = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, format, null, pixels, stride);
        bare.Freeze();
        return bare;
    }

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource source)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
