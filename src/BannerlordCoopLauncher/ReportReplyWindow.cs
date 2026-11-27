using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace Coodsz.Ui;

/// <summary>
/// In tool replies (docs/design/in-tool-replies.md): the My reports button next to Send my logs, the same
/// size and style, with a red count of the reports that have an unread answer and a gold glow while there
/// is one; the banner at the top of the tool until every answer is read; the check for answers to this
/// tool's reports (30 s after start, then on ReplySchedule and never sooner than the worker asks, only while
/// the tool has reply keys); and the Still not working? button.
/// </summary>
public static class ReportRepliesUi
{
    /// <summary>A report was sent or an answer was read, so the My reports button counts again.</summary>
    public static event Action? Changed;

    public static void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // A count that does not refresh never stops the tool.
        }
    }

    /// <summary>
    /// Puts My reports next to (or under, in a server tool's sidebar) the Send my logs button in the
    /// slot and starts the reply check. The button only shows once there is a sent report or an answer.
    /// logsRequest packs the tool's Send my logs, for No, still broken on a report card. bannerHost is the
    /// slot at the top of the tool for the Coodsz answered your report banner.
    /// </summary>
    public static void Attach(Window owner, ContentControl sendLogsSlot, Action openSendLogs, Action<string> log, bool below = false,
        Func<CrashReportRequest>? logsRequest = null, ContentControl? bannerHost = null)
    {
        if (below)
            FitWidth(sendLogsSlot.Content as Button);
        var store = ReportReplyStore.ForThisTool();
        if (store == null)
            return;
        var sendLogs = sendLogsSlot.Content as UIElement;
        sendLogsSlot.Content = null;
        var panel = new StackPanel { Orientation = below ? Orientation.Vertical : Orientation.Horizontal };
        if (sendLogs != null)
            panel.Children.Add(sendLogs);
        var tools = new CardTools(store, logsRequest, log, openSendLogs);
        var button = new MyReportsButton(owner, tools, below);
        // The glow ring around the button is 3 px and takes no room: the button lines up with Send my logs, 8 px under
        // it in a sidebar (as the old link was) or 12 px beside it, and the row or sidebar grows by the button only.
        button.View.Margin = below ? new Thickness(-3, 5, 0, -3) : new Thickness(9, -3, 0, -3);
        if (below)
            FitWidth(button.Button);
        panel.Children.Add(button.View);
        sendLogsSlot.Content = panel;
        var banner = bannerHost == null ? null : new ReplyBannerBar(owner, bannerHost, tools);
        void Refresh() => owner.Dispatcher.BeginInvoke(() => Update(button, banner, store));
        Changed += Refresh;
        owner.Closed += (_, _) => Changed -= Refresh;
        Update(button, banner, store);
        new ReplyChecker(owner, tools, log).Start();
    }

    /// <summary>
    /// A second My reports button in an empty slot, beside a Send my logs button on a page (the Server Tool's Logs page).
    /// It shows and counts like the one Attach puts in the sidebar, without a second reply check or banner.
    /// </summary>
    public static void AddMyReports(Window owner, ContentControl slot, Action openSendLogs, Action<string> log, Func<CrashReportRequest>? logsRequest = null)
    {
        var store = ReportReplyStore.ForThisTool();
        if (store == null)
            return;
        var button = new MyReportsButton(owner, new CardTools(store, logsRequest, log, openSendLogs), below: false);
        // Own ids, so UI tests and a sidebar search for MyReportsButton find the first one only.
        System.Windows.Automation.AutomationProperties.SetAutomationId(button.Button, "MyReportsButtonPage");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button.View, "MyReportsGlowPage");
        button.View.Margin = new Thickness(9, -3, 0, -3);
        slot.Content = button.View;
        void Refresh() => owner.Dispatcher.BeginInvoke(() => Update(button, null, store));
        Changed += Refresh;
        owner.Closed += (_, _) => Changed -= Refresh;
        Update(button, null, store);
    }

    /// <summary>
    /// In a Server Tool's sidebar a long translation (Envoyer mes journaux, Отправить мои логи) scales the button's
    /// icon and words down to fit instead of being cut off at the sidebar's edge. Words that fit stay as they are.
    /// </summary>
    private static void FitWidth(Button? button)
    {
        if (button?.Content is not UIElement content || content is Viewbox)
            return;
        button.Content = null;
        button.Content = new Viewbox { Child = content, StretchDirection = StretchDirection.DownOnly };
    }

    // The button shows once there is something to find; the count, the glow and the banner follow the unread answers.
    private static void Update(MyReportsButton button, ReplyBannerBar? banner, ReportReplyStore store)
    {
        try
        {
            var rows = store.Rows();
            var answers = rows.Where(row => row.Answer != null).Select(row => row.Answer!).ToList();
            var unread = ReportReplies.UnreadReports(answers);
            button.Show(ReportReplies.Look(rows.Count, unread), unread);
            banner?.Show(ReportReplies.Banner(answers));
        }
        catch (Exception)
        {
            // Unreadable files leave the button and the banner as they were.
        }
    }

    /// <summary>
    /// My reports as a real button, the size and style of Send my logs, with a red count on it. While a report
    /// has an unread answer it glows in the tool's gold: a light gold ring around a dark edge, with a soft halo
    /// outside it. It is still, no animation. The ring's room is always there, so nothing moves when it lights.
    /// </summary>
    private sealed class MyReportsButton
    {
        private readonly Button _button;
        private readonly Border _ring;
        private readonly Brush _fill;
        private readonly Brush _ringLit;
        private readonly Brush _edge;
        private readonly DropShadowEffect _glow;
        private readonly Border _badge;
        private readonly TextBlock _count;

        public FrameworkElement View => _ring;

        public Button Button => _button;

        public MyReportsButton(Window owner, CardTools tools, bool below)
        {
            _fill = CrashReportUi.Gold(owner);
            _button = ModReportUi.Filled(owner, ReportReplyText.MyReports, ReportReplyText.MyReportsTip, _fill, "📬");
            System.Windows.Automation.AutomationProperties.SetAutomationId(_button, "MyReportsButton");
            _button.Click += (_, _) => MyReportsWindow.Open(owner, tools);
            var gold = _fill is SolidColorBrush solid ? solid.Color : Coodsz.Ui.ToolPalette.Of(Color.FromRgb(0xE2, 0xC2, 0x7A));
            _ringLit = new SolidColorBrush(Lighter(gold));
            _edge = ModReportUi.Ink(owner);
            _glow = new DropShadowEffect { Color = gold, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 };
            // The button and its count sit in one cell: the count on the button's top right corner, so the button keeps
            // Send my logs' width in every language (Meus relatórios with 99+ did not fit the Server Tools' sidebar).
            var cell = new Grid();
            cell.Children.Add(_button);
            _ring = new Border
            {
                Child = cell,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(_ring, "MyReportsGlow");
            // The count: a bigger red pill on the button's corner, a strong red with white digits so it reads on the gold.
            _count = new TextBlock
            {
                Tag = UiLanguage.NoTranslate,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _badge = new Border
            {
                Background = Coodsz.Ui.ToolPalette.Brush(0xD6, 0x3B, 0x3B),
                BorderBrush = ModReportUi.Ink(owner),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                MinWidth = 24,
                Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                // Over the right edge. In a sidebar up into the 8 px gap under Send my logs; in a launcher's row flush with the
                // button's top, since that row sits right under the banner and a raised count would cover its edge.
                Margin = new Thickness(0, below ? -8 : 0, -10, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = _count
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(_badge, "MyReportsBadge");
            // The count is over the button, not in it: a click on it opens My reports too.
            _badge.MouseLeftButtonUp += (_, _) => MyReportsWindow.Open(owner, tools);
            cell.Children.Add(_badge);
        }

        public void Show(MyReportsLook look, int unread)
        {
            _ring.Visibility = look.Visible ? Visibility.Visible : Visibility.Collapsed;
            _count.Text = look.Badge ?? "";
            _badge.Visibility = look.Badge == null ? Visibility.Collapsed : Visibility.Visible;
            _badge.ToolTip = look.Badge == null ? null : string.Format(Translator.T(ReportReplyText.NewAnswersLabel(unread)), unread);
            // Unlit, the ring has no background, so the 3 px around the button is not a dead spot for clicks.
            _ring.Background = look.Glow ? _ringLit : null;
            _ring.Effect = look.Glow ? _glow : null;
            _button.BorderBrush = look.Glow ? _edge : _fill;
        }

        // Halfway to white: the lit ring.
        private static Color Lighter(Color color) =>
            Color.FromRgb((byte)((color.R + 255) / 2), (byte)((color.G + 255) / 2), (byte)((color.B + 255) / 2));
    }

    /// <summary>
    /// The banner at the top of the tool while an answer is unread: Coodsz answered your report CR-XXXXX (and N
    /// more), or Automatic answer, with Open, which opens that report in My reports. It has no x: it goes once
    /// every answer is read. The reply popup stays as it is.
    /// </summary>
    private sealed class ReplyBannerBar
    {
        private readonly Window _owner;
        private readonly ContentControl _host;
        private readonly CardTools _tools;
        private ReplyBanner? _shown;

        public ReplyBannerBar(Window owner, ContentControl host, CardTools tools)
        {
            _owner = owner;
            _host = host;
            _tools = tools;
            _host.Visibility = Visibility.Collapsed;
        }

        public void Show(ReplyBanner? banner)
        {
            if (banner == _shown)
                return;
            _shown = banner;
            if (banner == null)
            {
                _host.Content = null;
                _host.Visibility = Visibility.Collapsed;
                return;
            }

            var gold = CrashReportUi.Gold(_host);
            var open = ModReportUi.Filled(_host, ReportReplyText.BannerOpen, ReportReplyText.BannerOpenTip, gold, "📬");
            System.Windows.Automation.AutomationProperties.SetAutomationId(open, "ReplyBannerOpen");
            open.Click += (_, _) => MyReportsWindow.Open(_owner, _tools, banner.Code);
            // English: the text hook shows it in the player's language (the code stays as it is).
            var text = new TextBlock
            {
                Text = ReportReplies.BannerText(banner),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = gold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var frame = ModReportUi.Frame(_host, gold, ModReportUi.GoldBack(_host), text, new UIElement[] { open }, null, null);
            System.Windows.Automation.AutomationProperties.SetAutomationId(frame, "ReplyBanner");
            _host.Content = frame;
            _host.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The Still not working? Ask Coodsz on Discord button. say gets the line to show under it.</summary>
    public static Button StillNotWorkingButton(FrameworkElement scope, string? code, Action<string, bool> say)
    {
        var button = CrashReportUi.Link(scope, ReportReplyText.StillNotWorking, ReportReplyText.StillNotWorkingTip, CrashReportUi.Gold(scope), 14, "💬");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "StillNotWorking");
        button.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(ReportReplies.DiscordMessage(code, ToolName(), ToolVersion()));
                say(ReportReplyText.Copied, true);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                say(ReportReplyText.CopyFailed, false);
            }

            OpenDiscord();
        };
        return button;
    }

    /// <summary>Opens the Coop Discord invite, or the server by id when the invite does not open.</summary>
    public static void OpenDiscord()
    {
        foreach (var url in new[] { ReportReplies.CoopDiscordInvite, ReportReplies.CoopDiscordChannel })
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    /// <summary>This program's name, like Coodsz TAOM Client Launcher.</summary>
    public static string ToolName() => Assembly.GetEntryAssembly()?.GetName().Name ?? "";

    public static string ToolVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";

    public static string Date(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The time of an answer from its GitHub date, or when this PC got it.</summary>
    public static string AnswerDate(ReportAnswer answer) => Date(ReportReplies.AnswerTime(answer));

    internal static SolidColorBrush BrushOf(byte r, byte g, byte b) => Coodsz.Ui.ToolPalette.Brush(r, g, b);

    /// <summary>
    /// The check: the first one 30 s after start, then on ReplySchedule (every 30 s for 10 minutes after a report or a
    /// No, still broken, every 2 to 3 minutes while a report is open, every 15 to 30 minutes while the only reports left
    /// are answered and read, never sooner than the worker asks). No check at all while the window is minimized or no
    /// report needs one; a restored window or a sent report brings one within seconds. A new answer opens the reply
    /// window once no other window of the tool is open (What's new, a report window).
    /// </summary>
    private sealed class ReplyChecker
    {
        private readonly Window _owner;
        private readonly CardTools _tools;
        private readonly ReportReplyStore _store;
        private readonly Action<string> _log;
        private readonly DispatcherTimer _timer;
        private readonly Queue<ReplyItem> _waiting = new();
        private bool _checking;
        private ReportReplyWindow? _showing;

        // The worker's last wait (its next, or the wait after a refused check): a sent report never moves a check before it.
        private DateTime _notBeforeUtc;

        // The newest send or No, still broken seen so far: a newer one is a report just sent.
        private DateTime? _lastStart;

        public ReplyChecker(Window owner, CardTools tools, Action<string> log)
        {
            _owner = owner;
            _tools = tools;
            _store = tools.Store;
            _log = log;
            _timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher);
            _timer.Tick += async (_, _) => await TickAsync();
            void OnChanged() => owner.Dispatcher.BeginInvoke(() => Replan(false));
            ReportRepliesUi.Changed += OnChanged;
            // Minimized: no checks. Restored from minimized: one within seconds. Normal to Maximized is no restore.
            var lastState = owner.WindowState;
            void OnState(object? sender, EventArgs e)
            {
                var restored = lastState == WindowState.Minimized && owner.WindowState != WindowState.Minimized;
                lastState = owner.WindowState;
                Replan(restored);
            }
            owner.StateChanged += OnState;
            owner.Closed += (_, _) =>
            {
                ReportRepliesUi.Changed -= OnChanged;
                owner.StateChanged -= OnState;
                _timer.Stop();
                // A key still waiting in memory gets one last try, and is logged if it is lost.
                try
                {
                    _store.Prune();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                foreach (var code in _store.PendingCodes())
                    _log(string.Format(ReportReplies.KeyLostText, code));
                foreach (var code in _store.PendingFollowUpCodes())
                    _log(string.Format(ReportReplies.FollowUpLostText, code));
            };
        }

        // The next check has its own time, apart from the popups: closing a reply never moves it. Null: no check planned.
        private DateTime? _nextCheckUtc;

        private bool Minimized => _owner.WindowState == WindowState.Minimized;

        public void Start()
        {
            // Reports older than 30 days leave the keys file on start, not only after a check.
            try
            {
                _store.Prune();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            try
            {
                var reports = _store.ScheduleReports();
                _lastStart = ReplySchedule.NewestStart(reports);
                _nextCheckUtc = ReplySchedule.Next(DateTime.UtcNow, reports, null, Minimized) == null ? null : DateTime.UtcNow + ReplySchedule.StartWait;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _nextCheckUtc = DateTime.UtcNow + ReplySchedule.StartWait;
            }

            WaitForCheck();
        }

        private void Wait(TimeSpan wait)
        {
            _timer.Stop();
            _timer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
            _timer.Start();
        }

        private void WaitForCheck()
        {
            if (_nextCheckUtc is { } due)
                Wait(due - DateTime.UtcNow);
            else
                _timer.Stop();
        }

        // From the async void Tick handler, so nothing may escape.
        private async Task TickAsync()
        {
            _timer.Stop();
            if (_waiting.Count > 0)
            {
                ShowNext();
                return;
            }

            if (_checking)
                return;
            // A time more than 6 hours ahead means the clock went back: it no longer counts. This comes before the not
            // due yet return below, or a check would wait until the old clock time came round again.
            if (ReplySchedule.ClockWentBack(DateTime.UtcNow, _notBeforeUtc))
                _notBeforeUtc = DateTime.MinValue;
            if (ReplySchedule.ClockWentBack(DateTime.UtcNow, _nextCheckUtc))
                _nextCheckUtc = DateTime.UtcNow;
            if (_nextCheckUtc is not { } due || DateTime.UtcNow < due)
            {
                WaitForCheck();
                return;
            }

            // Never sooner than the worker's last wait, whatever planned this check.
            if (DateTime.UtcNow < _notBeforeUtc)
            {
                _nextCheckUtc = _notBeforeUtc;
                WaitForCheck();
                return;
            }

            // Minimized since, or nothing needs a check any more: stop until a restore or a send. A keys file busy for a
            // moment stops it only while minimized (ReplySchedule.ShouldCheck).
            if (!ReplySchedule.ShouldCheck(DateTime.UtcNow, _store.ScheduleReports, Minimized))
            {
                _nextCheckUtc = null;
                return;
            }

            _checking = true;
            var check = new ReplyCheck([], ReportReplies.DefaultWait, false);
            try
            {
                // An answer is at most a few KB; a bigger body is refused, not buffered.
                using var http = new System.Net.Http.HttpClient
                {
                    Timeout = ReportReplies.Timeout + TimeSpan.FromSeconds(5),
                    MaxResponseContentBufferSize = ReportReplies.MaxResponseBytes
                };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CoodszReplies/1");
                check = await Task.Run(() => ReportReplies.CheckAsync(http, _store));
                foreach (var item in check.NewItems)
                {
                    _log(string.Format(ReportReplyText.LogLine, item.Code));
                    _waiting.Enqueue(item);
                }

                // A new status (Fixed in v1.3.3) refreshes the link too, not only a new answer.
                if (check.Called)
                    NotifyChanged();
            }
            catch (Exception)
            {
                // CheckAsync never throws; anything else waits for the next check.
            }
            finally
            {
                _checking = false;
            }

            var now = DateTime.UtcNow;
            // A keys file busy for a moment gives a retry in 30 to 60 s, never a stop (ReplySchedule.AfterCheck).
            var wait = ReplySchedule.AfterCheck(check, now, _store.ScheduleReports, Minimized);

            _notBeforeUtc = now + (check.Answered ? check.Asked ?? TimeSpan.Zero : check.Called ? check.Next : TimeSpan.Zero);
            _nextCheckUtc = wait == null ? null : now + wait.Wait + Jitter(wait);
            if (_waiting.Count > 0)
                ShowNext();
            else
                WaitForCheck();
        }

        private static TimeSpan Jitter(ReplyWait wait) =>
            TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * wait.Jitter.TotalMilliseconds);

        // The window was minimized or restored (resume), a report was sent or an answer read: ReplySchedule.Replan
        // stops the checks, brings one within seconds, or keeps the planned one.
        private void Replan(bool resume)
        {
            try
            {
                // A newer send or No, still broken than last time is a send. A keys file busy for a moment still plans
                // from the reports that could be read, or keeps a retry (ReplySchedule.ReplanFrom).
                var (next, newest) = ReplySchedule.ReplanFrom(DateTime.UtcNow, _nextCheckUtc, _notBeforeUtc, _lastStart, _store.ScheduleReports,
                    Minimized, resume, Random.Shared.NextDouble());
                _lastStart = newest;
                if (next == _nextCheckUtc)
                    return;
                _nextCheckUtc = next;
                // A check that runs plans its own next one; a reply window or one waiting to show goes first.
                if (!_checking && _showing == null && _waiting.Count == 0)
                    WaitForCheck();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The planned check stays.
            }
        }

        private void ShowNext()
        {
            if (_showing != null)
                return;
            if (_waiting.Count == 0)
            {
                WaitForCheck();
                return;
            }

            // One thing at a time: What's new, the license or a report window first.
            var busy = !_owner.IsVisible || Application.Current?.Windows.OfType<Window>().Any(window => window != _owner && window.IsVisible) == true;
            if (busy)
            {
                Wait(TimeSpan.FromSeconds(15));
                return;
            }

            var item = _waiting.Dequeue();
            _showing = new ReportReplyWindow(item, _tools) { Owner = _owner };
            _showing.Closed += (_, _) =>
            {
                _showing = null;
                NotifyChanged();
                if (_waiting.Count > 0)
                    Wait(TimeSpan.FromSeconds(2));
                else
                    WaitForCheck();
            };
            _showing.Show();
        }
    }
}

/// <summary>What a report card needs from its tool: the store, the Send my logs pack for No, still broken, the log and Send my logs itself.</summary>
public sealed record CardTools(ReportReplyStore Store, Func<CrashReportRequest>? LogsRequest, Action<string> Log, Action? OpenSendLogs = null);

/// <summary>
/// One report as My reports and the reply popup show it (v1.3.3): "Your report CR-XXXXX" with its status
/// badge (Looking into it, Answered, Solved, Fixed in v1.3.3), a muted line with the sent time and the
/// short title, Coodsz's reply as written (numbered steps as a list, the Discord line last), the
/// player's own answers under it, and Did this fix it? with Yes, it's fixed and No, still broken. No
/// opens the form in place; Yes shows the solved card with It broke again. The colors are the tool's own.
/// </summary>
public sealed class ReportCard
{
    private readonly FrameworkElement _scope;
    private readonly CardTools _tools;
    private readonly string _code;
    private readonly ReportAnswer? _answer;
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 8, 0, 0), Tag = UiLanguage.NoTranslate };
    private readonly TextBox _words;
    private bool _formOpen;
    private bool _busy;
    private string? _steps;
    private bool _includeLogs = true;
    private ReportScreenshot? _shot;

    public ReportCard(FrameworkElement scope, CardTools tools, string code, ReportAnswer? answer)
    {
        _scope = scope;
        _tools = tools;
        _code = code;
        _answer = answer;
        _words = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            Padding = new Thickness(8),
            FontSize = 14,
            Tag = UiLanguage.NoTranslate,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = ReportRepliesUi.BrushOf(16, 22, 27),
            Foreground = ReportRepliesUi.BrushOf(213, 222, 230),
            CaretBrush = ReportRepliesUi.BrushOf(213, 222, 230),
            BorderBrush = ReportRepliesUi.BrushOf(58, 76, 92)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_words, "ReplyFormWords");
        // No MaxLength: it cuts a paste in the middle of an emoji. Longer text is cut at a whole character.
        _words.TextChanged += (_, _) =>
        {
            if (_words.Text.Length <= ReportFollowUps.MaxText)
                return;
            _words.Text = ReportFollowUps.Cut(_words.Text, ReportFollowUps.MaxText);
            _words.CaretIndex = _words.Text.Length;
        };
        Render();
    }

    public StackPanel View { get; } = new();

    private static readonly SolidColorBrush MutedBrush = ReportRepliesUi.BrushOf(142, 160, 174);
    private static readonly SolidColorBrush CardBack = ReportRepliesUi.BrushOf(20, 28, 34);
    private static readonly SolidColorBrush CardLine = ReportRepliesUi.BrushOf(58, 76, 92);

    private void Render()
    {
        // The words box keeps what the player typed across redraws.
        (_words.Parent as Panel)?.Children.Remove(_words);
        (_notice.Parent as Panel)?.Children.Remove(_notice);
        View.Children.Clear();

        var hasCode = CrashReports.IsCode(_code);
        ReportStatus status;
        ReplyKeyEntry? entry;
        IReadOnlyList<ReportFollowUp> followUps;
        ReportState? state;
        try
        {
            status = hasCode ? _tools.Store.StatusOf(_code) : new ReportStatus(ReportStatusKind.Answered);
            entry = hasCode ? _tools.Store.Entry(_code) : null;
            followUps = hasCode ? _tools.Store.FollowUps(_code) : [];
            state = hasCode ? _tools.Store.State(_code) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status = new ReportStatus(_answer == null ? ReportStatusKind.Looking : ReportStatusKind.Answered);
            entry = null;
            followUps = [];
            state = null;
        }

        // The header: Your report CR-XXXXX and the badge.
        var head = new WrapPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = hasCode ? string.Format(Translator.T(ReportReplyText.YourReport), _code) : Translator.T(ReportReplyText.KnownFix),
            Tag = UiLanguage.NoTranslate,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (hasCode)
            head.Children.Add(Badge(_scope, status));
        View.Children.Add(head);

        var lastFollowUp = followUps.Count > 0 ? followUps[^1] : null;
        var waiting = status.Kind == ReportStatusKind.Looking && lastFollowUp != null;
        var solved = state?.SolvedAt is { } solvedAt && status.Kind is ReportStatusKind.Solved or ReportStatusKind.Fixed
            && (lastFollowUp == null || solvedAt > lastFollowUp.At);
        if (waiting)
            View.Children.Add(Muted(lastFollowUp!.Logs ? ReportReplyText.FollowUpSent : ReportReplyText.FollowUpSentNoLogs, 13));
        else if (entry != null)
        {
            var title = string.IsNullOrWhiteSpace(entry.Title) ? Translator.T(ReportReplyText.ReasonTitle(entry.Reason)) : entry.Title;
            var line = Muted(string.Format(Translator.T(ReportReplyText.SentOn), ReportRepliesUi.Date(entry.SentAt)) + "  ·  " + title, 13);
            line.Tag = UiLanguage.NoTranslate;
            View.Children.Add(line);
        }

        if (_answer != null)
            View.Children.Add(ReplyCard(_answer));
        else if (status.Kind is not (ReportStatusKind.Solved or ReportStatusKind.Fixed))
            View.Children.Add(Muted(ReportReplyText.NoAnswerYet, 14));

        foreach (var followUp in followUps)
            View.Children.Add(OwnAnswer(followUp));

        // While a send runs the form stays (its buttons off), and after a failed send it is still there with the words.
        var canAct = hasCode && entry != null && ReportReplies.IsKey(entry.Key);
        switch (ReportReplyText.BottomOf(_formOpen, canAct, solved, waiting, _answer != null))
        {
            case CardBottom.Form:
                View.Children.Add(Form());
                break;
            case CardBottom.Solved:
                View.Children.Add(SolvedCard(canAct && _tools.LogsRequest != null));
                break;
            case CardBottom.Question:
                View.Children.Add(FixQuestion());
                break;
        }

        View.Children.Add(_notice);
        // Always a way on, Discord at least. Send my logs again closes this window, so it waits while
        // the No form is open and its words are not sent yet.
        View.Children.Add(OtherRoutes(!_formOpen && (_answer?.LogsButton == true || _tooOld || !canAct)));
    }

    private bool _tooOld;

    // The ways on that do not need this report: Send my logs again (when Coodsz asked for logs, or the
    // report can no longer take an answer) and Still not working? Ask Coodsz on Discord.
    private WrapPanel OtherRoutes(bool sendLogs)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (sendLogs && _tools.OpenSendLogs != null)
        {
            var again = CrashReportUi.Link(_scope, ReportReplyText.SendLogsAgain, ReportReplyText.SendLogsAgainTip, CrashReportUi.Gold(_scope), 13, "📤");
            again.Margin = new Thickness(0, 0, 18, 6);
            System.Windows.Automation.AutomationProperties.SetAutomationId(again, "ReplySendLogsAgain");
            again.Click += (_, _) =>
            {
                // Send my logs is its own window over the tool, so this one closes first, like before.
                Window.GetWindow(_scope)?.Close();
                _tools.OpenSendLogs();
            };
            row.Children.Add(again);
        }

        var still = ReportRepliesUi.StillNotWorkingButton(_scope, CrashReports.IsCode(_code) ? _code : null, Notice);
        still.Margin = new Thickness(0, 0, 0, 6);
        row.Children.Add(still);
        return row;
    }

    /// <summary>The status badge: a small pill in the tool's colors.</summary>
    public static Border Badge(FrameworkElement scope, ReportStatus status)
    {
        var (fore, back) = status.Kind switch
        {
            ReportStatusKind.Answered => (CrashReportUi.Gold(scope), ModReportUi.GoldBack(scope)),
            ReportStatusKind.Solved or ReportStatusKind.Fixed => (CrashReportUi.Green(scope), ModReportUi.GreenBack(scope)),
            _ => (CrashReportUi.Red(scope), ModReportUi.RedBack(scope))
        };
        var label = Translator.T(ReportReplyText.StatusLabel(status.Kind));
        if (status.Kind == ReportStatusKind.Fixed)
            label = string.Format(label, status.FixedIn);
        var badge = new Border
        {
            Background = back,
            BorderBrush = fore,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, Tag = UiLanguage.NoTranslate, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fore }
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(badge, "ReportStatusBadge");
        return badge;
    }

    // Coodsz's reply: the name, the time and the text as written, steps as a numbered list.
    private Border ReplyCard(ReportAnswer answer)
    {
        var inner = new StackPanel();
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        who.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = CrashReportUi.Gold(_scope),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "C", Tag = UiLanguage.NoTranslate, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = ModReportUi.Ink(_scope), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var name = answer.Source switch
        {
            AnswerSource.Known => Translator.T(ReportReplyText.KnownFix),
            AnswerSource.Auto => Translator.T(ReportReplyText.AutoTitle),
            _ => "Coodsz"
        };
        who.Children.Add(new TextBlock { Text = name, Tag = UiLanguage.NoTranslate, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var when = Muted(string.Format(Translator.T(ReportReplyText.RepliedAt), ReportRepliesUi.AnswerDate(answer)), 12);
        when.Tag = UiLanguage.NoTranslate;
        when.Margin = new Thickness(0);
        when.VerticalAlignment = VerticalAlignment.Center;
        who.Children.Add(when);
        inner.Children.Add(who);
        if (answer.Replaced)
            inner.Children.Add(Muted(ReportReplyText.Replaced, 12));

        foreach (var block in ReportReplies.ReplyBlocks(answer.Text))
        {
            var discord = block.Text.Contains("discord", StringComparison.OrdinalIgnoreCase);
            var text = Words(block.Text, discord ? MutedBrush : null);
            if (block.Step is not { } number)
            {
                text.Margin = new Thickness(0, discord ? 8 : 2, 0, 2);
                inner.Children.Add(text);
                continue;
            }

            var row = new Grid { Margin = new Thickness(6, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var digit = new TextBlock { Text = number.ToString(CultureInfo.InvariantCulture) + ".", Tag = UiLanguage.NoTranslate, FontSize = 14, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(digit);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            inner.Children.Add(row);
        }

        return new Border
        {
            Background = CardBack,
            BorderBrush = CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = inner
        };
    }

    // The answer text: selectable to copy, never translated, no links.
    private static TextBox Words(string text, Brush? color) => new()
    {
        Text = text,
        IsReadOnly = true,
        Tag = UiLanguage.NoTranslate,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Foreground = color ?? ReportRepliesUi.BrushOf(213, 222, 230)
    };

    // The player's own No, still broken: You, the time, the steps answer and what was added, then the words.
    private Border OwnAnswer(ReportFollowUp followUp)
    {
        var parts = new List<string> { string.Format(Translator.T(ReportReplyText.You), ReportRepliesUi.Date(followUp.At)), Translator.T(ReportReplyText.StepsLabel(followUp.Steps)) };
        if (followUp.Logs)
            parts.Add(Translator.T(ReportReplyText.NewLogsAdded));
        if (followUp.Screenshot)
            parts.Add(Translator.T(ReportReplyText.ScreenshotAdded));
        var inner = new StackPanel();
        var line = Muted(string.Join("  ·  ", parts), 12);
        line.Tag = UiLanguage.NoTranslate;
        line.Margin = new Thickness(0, 0, 0, 4);
        inner.Children.Add(line);
        inner.Children.Add(Words(followUp.Text, null));
        return new Border
        {
            Background = CardBack,
            BorderBrush = CrashReportUi.Gold(_scope),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 10, 0, 0),
            Child = inner
        };
    }

    // Did this fix it? Yes, it's fixed and No, still broken.
    private FrameworkElement FixQuestion()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(new TextBlock { Text = ReportReplyText.DidThisFixIt, FontSize = 14, Foreground = MutedBrush, Margin = new Thickness(0, 0, 0, 6) });
        var row = new WrapPanel();
        var yes = ModReportUi.Filled(_scope, ReportReplyText.YesFixed, ReportReplyText.YesFixedTip, CrashReportUi.Green(_scope), "✓", 14, new Thickness(14, 7, 14, 7));
        System.Windows.Automation.AutomationProperties.SetAutomationId(yes, "ReplyYesFixed");
        yes.IsEnabled = !_busy;
        yes.Click += async (_, _) => await SolvedAsync();
        row.Children.Add(yes);
        if (_tools.LogsRequest != null)
        {
            var no = ModReportUi.Filled(_scope, ReportReplyText.NoBroken, ReportReplyText.NoBrokenTip, ModReportUi.RedBack(_scope), "✕", 14, new Thickness(14, 7, 14, 7));
            no.BorderBrush = CrashReportUi.Red(_scope);
            no.Foreground = CrashReportUi.Red(_scope);
            no.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetAutomationId(no, "ReplyStillBroken");
            no.IsEnabled = !_busy;
            no.Click += (_, _) => OpenForm();
            row.Children.Add(no);
        }

        panel.Children.Add(row);
        return panel;
    }

    private void OpenForm()
    {
        _formOpen = true;
        Notice("", true);
        Render();
        ShowForm();
    }

    // The form is taller than the card's room in My reports: once it is laid out, the Send row is
    // scrolled into view, and the words box keeps the focus.
    private void ShowForm()
    {
        _words.Focus();
        View.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_formOpen && _sendRow is { IsLoaded: true } row)
                row.BringIntoView();
            // A notice under the form (after a failed send, its error) comes into view too; the Send row is just above it.
            if (_formOpen && _notice.Text.Length > 0 && _notice.IsLoaded)
                _notice.BringIntoView();
        });
    }

    private FrameworkElement? _sendRow;

    // Great, glad it works now, with It broke again.
    private Border SolvedCard(bool canReopen)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = ReportReplyText.SolvedTitle, FontSize = 15, FontWeight = FontWeights.SemiBold });
        var text = Muted(ReportReplyText.SolvedText, 13);
        text.Margin = new Thickness(0, 4, 0, 0);
        inner.Children.Add(text);
        if (canReopen)
        {
            var again = ModReportUi.Outlined(_scope, ReportReplyText.BrokeAgain, ReportReplyText.BrokeAgainTip, ReportRepliesUi.BrushOf(213, 222, 230), CardBack);
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Margin = new Thickness(0, 10, 0, 0);
            System.Windows.Automation.AutomationProperties.SetAutomationId(again, "ReplyBrokeAgain");
            again.IsEnabled = !_busy;
            again.Click += (_, _) => OpenForm();
            inner.Children.Add(again);
        }

        return new Border
        {
            Background = ModReportUi.GreenBack(_scope),
            BorderBrush = CrashReportUi.Green(_scope),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 14, 0, 0),
            Child = inner
        };
    }

    // The No, still broken form, in place under the reply.
    private Border Form()
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = ReportReplyText.FormTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        inner.Children.Add(Label(ReportReplyText.TriedSteps));
        var choices = new WrapPanel();
        foreach (var (steps, text) in new[] { (FollowUpSteps.All, ReportReplyText.StepsAll), (FollowUpSteps.Some, ReportReplyText.StepsSome), (FollowUpSteps.None, ReportReplyText.StepsNone) })
        {
            var picked = _steps == steps;
            var choice = picked
                ? ModReportUi.Filled(_scope, text, text, CrashReportUi.Gold(_scope), size: 13, padding: new Thickness(12, 6, 12, 6))
                : ModReportUi.Outlined(_scope, text, text, ReportRepliesUi.BrushOf(213, 222, 230), CardBack);
            choice.Margin = new Thickness(0, 0, 8, 6);
            System.Windows.Automation.AutomationProperties.SetAutomationId(choice, "ReplySteps_" + steps);
            choice.IsEnabled = !_busy;
            choice.Click += (_, _) =>
            {
                _steps = steps;
                Render();
            };
            choices.Children.Add(choice);
        }

        inner.Children.Add(choices);
        inner.Children.Add(Label(ReportReplyText.WhatNow));
        inner.Children.Add(_words);
        var hint = Muted(ReportReplyText.WhatNowHint, 12);
        hint.Margin = new Thickness(0, 4, 0, 6);
        inner.Children.Add(hint);

        var logs = new CheckBox
        {
            Content = ReportReplyText.AddNewLogs,
            ToolTip = ReportReplyText.AddNewLogsTip,
            IsChecked = _includeLogs,
            Foreground = ReportRepliesUi.BrushOf(213, 222, 230),
            Margin = new Thickness(0, 2, 0, 6)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(logs, "ReplyAddLogs");
        logs.IsEnabled = !_busy;
        logs.Checked += (_, _) => _includeLogs = true;
        logs.Unchecked += (_, _) => _includeLogs = false;
        inner.Children.Add(logs);

        if (_shot == null)
        {
            var add = CrashReportUi.Link(_scope, ReportReplyText.AddScreenshot, ReportExtras.AddScreenshotsTip, CrashReportUi.Gold(_scope), 13, "🖼");
            System.Windows.Automation.AutomationProperties.SetAutomationId(add, "ReplyAddScreenshot");
            add.IsEnabled = !_busy;
            add.Click += async (_, _) => await PickScreenshotAsync();
            inner.Children.Add(add);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = _shot.DisplayName, Tag = UiLanguage.NoTranslate, FontSize = 13, Margin = new Thickness(0, 0, 12, 0) });
            var remove = CrashReportUi.Link(_scope, ReportReplyText.RemoveScreenshot, ReportExtras.RemoveTip, CrashReportUi.Red(_scope), 13);
            remove.IsEnabled = !_busy;
            remove.Click += (_, _) =>
            {
                _shot = null;
                Render();
            };
            row.Children.Add(remove);
            inner.Children.Add(row);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _sendRow = buttons;
        var send = ModReportUi.Filled(_scope, ReportReplyText.SendToCoodsz, ReportReplyText.SendToCoodsz, CrashReportUi.Gold(_scope), size: 14, padding: new Thickness(14, 7, 14, 7));
        System.Windows.Automation.AutomationProperties.SetAutomationId(send, "ReplySendToCoodsz");
        // Off while it sends; a failed send turns it on again with the words still there.
        send.IsEnabled = !_busy;
        _words.IsReadOnly = _busy;
        send.Click += async (_, _) => await SendAsync();
        buttons.Children.Add(send);
        var cancel = ModReportUi.Outlined(_scope, ReportReplyText.Cancel, ReportReplyText.Cancel, ReportRepliesUi.BrushOf(213, 222, 230), CardBack);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.IsEnabled = !_busy;
        cancel.Click += (_, _) =>
        {
            _formOpen = false;
            Notice("", true);
            Render();
        };
        buttons.Children.Add(cancel);
        inner.Children.Add(buttons);

        return new Border
        {
            Background = CardBack,
            BorderBrush = CrashReportUi.Red(_scope),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 14, 0, 0),
            Child = inner
        };
    }

    // From the async void click handler, so nothing may escape.
    private async Task PickScreenshotAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = Translator.T(ReportReplyText.PictureFiles) + "|*.png;*.jpg;*.jpeg|" + Translator.T(ReportReplyText.AllFiles) + "|*.*",
            Title = Translator.T(ReportReplyText.AddScreenshot)
        };
        if (dialog.ShowDialog(Window.GetWindow(_scope)) != true)
            return;
        var path = dialog.FileName;
        try
        {
            // Re-encoded from its pixels and stripped of its metadata, like the screenshots of a report.
            var shot = await Task.Run(() => ReportScreenshotImages.Prepare(ReportScreenshotImages.ReadFile(path), Path.GetFileName(path)));
            if (shot.Size > ReportFollowUps.MaxScreenshotBytes)
            {
                Notice(ReportExtras.TooLargeText, false);
                return;
            }

            _shot = shot;
            Notice("", true);
            Render();
        }
        catch (InvalidDataException ex)
        {
            Notice(ex.Message, false);
        }
        // FileFormatException (a broken picture header) is a FormatException, not an IOException.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException
            or System.Runtime.InteropServices.COMException or InvalidOperationException or OverflowException)
        {
            Notice(ReportExtras.CouldNotReadText, false);
        }
    }

    // Send to Coodsz: packs and sends through the worker, then shows the player's answer under the reply.
    private async Task SendAsync()
    {
        if (_busy)
            return;
        if (_steps == null)
        {
            Notice(ReportReplyText.PickSteps, false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_words.Text))
        {
            Notice(ReportReplyText.WriteFirst, false);
            _words.Focus();
            return;
        }

        ReplyKeyEntry? entry;
        try
        {
            entry = _tools.Store.Entry(_code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file could not be read now; that says nothing about the report's age.
            Notice(ReportReplyText.FollowUpLocalFailed, false);
            return;
        }

        if (entry == null || _tools.LogsRequest == null)
        {
            // Only a report this PC no longer keeps (older than 30 days) is too old.
            _tooOld = entry == null;
            // A report too old for an answer can't take this form: it closes, so Send my logs again shows.
            if (_tooOld)
                _formOpen = false;
            Render();
            Notice(entry == null ? ReportReplyText.FollowUpTooOld : ReportReplyText.FollowUpLocalFailed, false);
            return;
        }

        _busy = true;
        Render();
        Notice(ReportReplyText.Sending, true);
        FollowUpResult result;
        var includeLogs = _includeLogs;
        var shot = _shot;
        try
        {
            // The tool reads its own state for the pack here, on the window thread, like Send my logs.
            var request = _tools.LogsRequest();
            var install = CrashReportWindow.InstallId();
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoodszCrashReports/1");
            result = await ReportFollowUps.SendAsync(http, request, install, new FollowUpForm(_code, entry.Key, _steps, _words.Text, includeLogs, shot));
        }
        catch (Exception ex)
        {
            _tools.Log(string.Format(ReportReplyText.FollowUpErrorLog, _code, ex.GetType().Name, ex.Message));
            // The tool's own pack or the install id, before anything went out: a local failure.
            result = new FollowUpResult(FollowUpStatus.LocalFailed);
        }

        _busy = false;
        if (result.Status == FollowUpStatus.Sent)
        {
            // The worker has it: it is kept here even when the answers file is busy (saved at the next check), never sent twice.
            var saved = _tools.Store.KeepFollowUp(new ReportFollowUp
            {
                Code = _code, At = DateTime.UtcNow, Steps = _steps, Text = result.CleanedText, Logs = includeLogs, Screenshot = shot != null, NewCode = result.NewCode
            });
            _tools.Log(string.Format(ReportReplyText.FollowUpLog, _code));
            if (!saved)
                _tools.Log(string.Format(ReportReplies.FollowUpWaitsText, _code));
            _formOpen = false;
            _words.Text = "";
            _steps = null;
            _shot = null;
            Notice("", true);
            Render();
            ReportRepliesUi.NotifyChanged();
            return;
        }

        // Too old for an answer: Send my logs shows under the line, so the player still has a way on.
        _tooOld = result.Status == FollowUpStatus.TooOld;
        // Too old: the form closes, so the Send my logs again link the message points to shows.
        if (_tooOld)
            _formOpen = false;
        Render();
        if (_formOpen)
            ShowForm();
        Notice(result.Status switch
        {
            FollowUpStatus.Empty => ReportReplyText.WriteFirst,
            FollowUpStatus.TooOld => ReportReplyText.FollowUpTooOld,
            FollowUpStatus.Busy => ReportReplyText.TooManyTries,
            FollowUpStatus.Refused => ReportReplyText.FollowUpRefused,
            FollowUpStatus.LocalFailed => ReportReplyText.FollowUpLocalFailed,
            _ => ReportReplyText.FollowUpFailed
        }, false);
    }

    // Yes, it's fixed: the worker closes or labels the report, and the card shows Solved.
    private async Task SolvedAsync()
    {
        if (_busy)
            return;
        var entry = Try(() => _tools.Store.Entry(_code));
        if (entry == null)
            return;
        _busy = true;
        Render();
        SolvedResult result;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = ReportReplies.Timeout + TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoodszReplies/1");
            result = await ReportReplies.MarkSolvedAsync(http, _code, entry.Key);
        }
        catch (Exception)
        {
            result = SolvedResult.Failed;
        }

        _busy = false;
        // An unknown report (older than 30 days on the worker) is still solved for the player.
        if (result is SolvedResult.Done or SolvedResult.Unknown)
        {
            TryDo(() => _tools.Store.MarkSolved(_code));
            _tools.Log(string.Format(ReportReplyText.SolvedLog, _code));
            Notice("", true);
            Render();
            ReportRepliesUi.NotifyChanged();
            return;
        }

        Render();
        Notice(result == SolvedResult.Busy ? ReportReplyText.TooManyTries : ReportReplyText.SolvedFailed, false);
    }

    private void Notice(string text, bool good)
    {
        _notice.Text = text.Length == 0 ? "" : Translator.T(text);
        _notice.Foreground = good ? CrashReportUi.Green(_scope) : CrashReportUi.Red(_scope);
    }

    private static void TryDo(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static T? Try<T>(Func<T?> work) where T : class
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

    internal static TextBlock Muted(string text, double size) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Foreground = MutedBrush,
        Margin = new Thickness(0, 4, 0, 0)
    };
}

/// <summary>
/// Coodsz replied to your report: the new answer as its report card (the badge, the reply, Did this fix
/// it?) and OK. It does not block the tool. The answer stays in My reports either way.
/// </summary>
public sealed class ReportReplyWindow : Window
{
    public ReportReplyWindow(ReplyItem item, CardTools tools)
    {
        Title = item.Auto ? ReportReplyText.AutoTitle : ReportReplyText.Title;
        Width = 660;
        SizeToContent = SizeToContent.Height;
        // Never taller than the screen; the content scrolls inside.
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 20);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = ReportRepliesUi.BrushOf(14, 20, 24);
        Foreground = ReportRepliesUi.BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "ReportReplyWindow");
        // It was centred at its small height: when the No form opens and it grows, it moves up so Send
        // to Coodsz stays on screen (a 768p screen too).
        SizeChanged += (_, _) => KeepOnScreen(this);

        var answer = FindAnswer(tools.Store, item) ?? new ReportAnswer
        {
            Code = item.Code, Id = item.Id, At = item.At, SavedAt = DateTime.UtcNow, Text = item.Text, LogsButton = item.LogsButton,
            Source = item.Auto ? AnswerSource.Auto : AnswerSource.Reply
        };
        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock
        {
            Text = item.Auto ? ReportReplyText.AutoTitle : ReportReplyText.Title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Foreground = CrashReportUi.Gold(this),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(new ReportCard(this, tools, item.Code, answer).View);
        var ok = CrashReportUi.Big(this, "OK", ReportReplyText.OkTip, CrashReportUi.Green(this));
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.IsDefault = true;
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "ReplyOk");
        ok.Click += (_, _) =>
        {
            Read(tools.Store, item);
            Close();
        };
        stack.Children.Add(ok);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Moves the window up (never above the work area) when its bottom passes the work area's.</summary>
    public static void KeepOnScreen(Window window)
    {
        var area = SystemParameters.WorkArea;
        if (double.IsNaN(window.Top) || window.ActualHeight <= 0)
            return;
        if (window.Top + window.ActualHeight > area.Bottom)
            window.Top = Math.Max(area.Top, area.Bottom - window.ActualHeight);
    }

    private static ReportAnswer? FindAnswer(ReportReplyStore store, ReplyItem item)
    {
        try
        {
            return store.Answers().FirstOrDefault(other => other.Code == item.Code && other.Id == item.Id && other.At == item.At);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // OK reads the answer; the close button leaves it unread in My reports.
    private static void Read(ReportReplyStore store, ReplyItem item)
    {
        try
        {
            if (FindAnswer(store, item) is { } answer)
                store.MarkRead(answer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// My reports: every report this PC sent (and the other exe of the same product) with the answers, newest
/// first, unread in bold with the status badge, a search over the code and the text, and Mark all read.
/// Clicking one shows its report card and marks the answer read.
/// </summary>
public sealed class MyReportsWindow : Window
{
    private readonly CardTools _tools;
    private readonly ReportReplyStore _store;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly StackPanel _detail = new() { Margin = new Thickness(16, 0, 0, 0) };
    private IReadOnlyList<MyReportRow> _rows = [];

    /// <summary>Opens My reports. select is the report the banner's Open shows (its unread answer), or null for none.</summary>
    public static void Open(Window? owner, CardTools tools, string? select = null)
    {
        var window = new MyReportsWindow(tools);
        if (owner != null && owner.IsLoaded)
            window.Owner = owner;
        if (select != null)
            window.Select(select);
        window.ShowDialog();
        ReportRepliesUi.NotifyChanged();
    }

    // The report's unread answer, else its newest row. Selecting it shows the card and marks it read.
    private void Select(string code)
    {
        var rows = _list.Items.OfType<ListBoxItem>().Where(item => item.Tag is MyReportRow row && row.Code == code).ToList();
        var pick = rows.FirstOrDefault(item => item.Tag is MyReportRow { Answer.Read: false }) ?? rows.FirstOrDefault();
        if (pick == null)
            return;
        _list.SelectedItem = pick;
        _list.ScrollIntoView(pick);
    }

    public MyReportsWindow(CardTools tools)
    {
        _tools = tools;
        _store = tools.Store;
        Title = ReportReplyText.MyReports;
        Width = 980;
        Height = Math.Min(680, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = ReportRepliesUi.BrushOf(14, 20, 24);
        Foreground = ReportRepliesUi.BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "MyReportsWindow");

        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var heading = new TextBlock { Text = ReportReplyText.MyReports, FontFamily = new FontFamily("Palatino Linotype"), FontSize = 26, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(heading, Dock.Left);
        top.Children.Add(heading);
        var markAll = CrashReportUi.Link(this, ReportReplyText.MarkAllRead, ReportReplyText.MarkAllReadTip, CrashReportUi.Gold(this), 13);
        markAll.VerticalAlignment = VerticalAlignment.Center;
        markAll.Margin = new Thickness(16, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(markAll, "MyReportsMarkAllRead");
        markAll.Click += (_, _) =>
        {
            Try(_store.MarkAllRead);
            Fill();
            ReportRepliesUi.NotifyChanged();
        };
        DockPanel.SetDock(markAll, Dock.Right);
        top.Children.Add(markAll);
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        searchRow.Children.Add(new TextBlock { Text = "Search", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _search = new TextBox
        {
            Width = 240,
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            Tag = UiLanguage.NoTranslate,
            Background = ReportRepliesUi.BrushOf(16, 22, 27),
            Foreground = ReportRepliesUi.BrushOf(213, 222, 230),
            CaretBrush = ReportRepliesUi.BrushOf(213, 222, 230),
            BorderBrush = ReportRepliesUi.BrushOf(58, 76, 92)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_search, "MyReportsSearch");
        _search.TextChanged += (_, _) => Fill();
        searchRow.Children.Add(_search);
        top.Children.Add(searchRow);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _list = new ListBox
        {
            Background = ReportRepliesUi.BrushOf(16, 22, 27),
            Foreground = ReportRepliesUi.BrushOf(213, 222, 230),
            BorderBrush = ReportRepliesUi.BrushOf(58, 76, 92),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_list, "MyReportsList");
        _list.SelectionChanged += (_, _) => ShowDetail(_list.SelectedItem is ListBoxItem { Tag: MyReportRow row } ? row : null);
        Grid.SetColumn(_list, 0);
        body.Children.Add(_list);
        var detail = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(detail, 1);
        body.Children.Add(detail);
        root.Children.Add(body);
        Content = root;
        Fill();
    }

    private IReadOnlyDictionary<string, ReportStatus> _statuses = new Dictionary<string, ReportStatus>();

    private void Fill()
    {
        try
        {
            _rows = _store.Rows();
            _statuses = _store.Statuses();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _rows = [];
            _statuses = new Dictionary<string, ReportStatus>();
        }

        var shown = ReportReplies.Search(_rows, _search.Text);
        _list.Items.Clear();
        foreach (var row in shown)
            _list.Items.Add(new ListBoxItem { Content = RowView(row), Tag = row, Padding = new Thickness(8, 6, 8, 6) });
        ShowDetail(null);
        if (shown.Count == 0)
            _detail.Children.Add(ReportCard.Muted(_rows.Count == 0 ? ReportReplyText.Empty : ReportReplyText.NoMatch, 14));
    }

    private FrameworkElement RowView(MyReportRow row)
    {
        var unread = row.Answer is { Read: false };
        var panel = new StackPanel();
        var first = new StackPanel { Orientation = Orientation.Horizontal };
        first.Children.Add(new TextBlock
        {
            Text = row.Code.Length > 0 ? row.Code : Translator.T(ReportReplyText.NotSent),
            Tag = UiLanguage.NoTranslate,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            FontWeight = unread ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (row.Code.Length > 0 && _statuses.TryGetValue(row.Code, out var status) && row.Answer?.Replaced != true)
        {
            var badge = ReportCard.Badge(this, status);
            badge.Margin = new Thickness(8, 0, 0, 0);
            first.Children.Add(badge);
        }

        panel.Children.Add(first);
        // When Coodsz replied, like the card says, not when this PC saved it.
        var when = ReportReplies.RowTime(row);
        panel.Children.Add(new TextBlock
        {
            Text = Translator.T(row.Answer == null ? ReportReplyText.NoAnswerYet : ReportReplyText.SourceLabel(row.Answer.Source))
                + (when is { } at ? "  ·  " + ReportRepliesUi.Date(at) : ""),
            Tag = UiLanguage.NoTranslate,
            FontSize = 12,
            FontWeight = unread ? FontWeights.Bold : FontWeights.Normal,
            Foreground = unread ? CrashReportUi.Gold(this) : ReportRepliesUi.BrushOf(142, 160, 174),
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (row.Answer != null)
        {
            var line = row.Answer.Text.Replace('\n', ' ');
            panel.Children.Add(new TextBlock
            {
                Text = line.Length > 90 ? line[..87] + "..." : line,
                Tag = UiLanguage.NoTranslate,
                FontSize = 12,
                FontWeight = unread ? FontWeights.Bold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        return panel;
    }

    private void ShowDetail(MyReportRow? row)
    {
        _detail.Children.Clear();
        if (row == null)
        {
            if (_list.Items.Count > 0)
                _detail.Children.Add(ReportCard.Muted(ReportReplyText.PickOne, 14));
            return;
        }

        _detail.Children.Add(new ReportCard(this, _tools, row.Code, row.Answer).View);
        if (row.Answer is { Read: false } answer)
        {
            Try(() => _store.MarkRead(answer));
            if (_list.SelectedItem is ListBoxItem item)
                item.Content = RowView(row);
            // The badge on the main window counts again now, not only when My reports closes.
            ReportRepliesUi.NotifyChanged();
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
