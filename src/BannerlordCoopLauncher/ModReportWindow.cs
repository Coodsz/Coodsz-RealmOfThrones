using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;

namespace Coodsz.Ui;

/// <summary>A mod the player can rate in the report window.</summary>
public sealed record ModReportChoice(string Id, string Name, string Version);

/// <summary>What the report window is opened with.</summary>
public sealed class ModReportRequest
{
    /// <summary>coop (the community list), taom (the TAOM list) or ad1259 (the AD1259 pack).</summary>
    public string Tool { get; init; } = "coop";
    public string ToolVersion { get; init; } = "";
    public string Coop { get; init; } = "";
    public string Game { get; init; } = "";
    public IReadOnlyList<ModReportChoice> Rate { get; init; } = Array.Empty<ModReportChoice>();

    /// <summary>The setup's mods in load order (ids and versions), sent with every report.</summary>
    public IReadOnlyList<ModReportMod> ModList { get; init; } = Array.Empty<ModReportMod>();

    /// <summary>Opened on its own after a game closed (Share how your mods worked?).</summary>
    public bool AfterGame { get; init; }
    public bool Crashed { get; init; }

    /// <summary>The mods the crash log names. The window marks them Broken; the player still checks and sends.</summary>
    public IReadOnlyList<string> Suggested { get; init; } = Array.Empty<string>();

    /// <summary>The one line of the crash log that says what crashed, for Send crash report.</summary>
    public string? CrashLine { get; init; }

    /// <summary>When the tool saw the crash, so Send crash report picks the crash dump of this crash.</summary>
    public DateTime? CrashedAtUtc { get; init; }

    /// <summary>The same request after a crash, with the mods the crash log names marked as suggested Broken.</summary>
    public ModReportRequest AsCrash(IReadOnlyList<string>? suggested, string? crashLine = null) => new()
    {
        CrashedAtUtc = DateTime.UtcNow,
        Tool = Tool,
        ToolVersion = ToolVersion,
        Coop = Coop,
        Game = Game,
        Rate = Rate,
        ModList = ModList,
        AfterGame = AfterGame,
        Crashed = true,
        CrashLine = crashLine ?? CrashLine,
        Suggested = (suggested ?? Array.Empty<string>())
            .Where(id => Rate.Any(mod => mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList()
    };

    /// <summary>The same request for one mod only, for the Report link on a mod row.</summary>
    public ModReportRequest Only(string id) => new()
    {
        Tool = Tool,
        ToolVersion = ToolVersion,
        Coop = Coop,
        Game = Game,
        Rate = Rate.Where(mod => mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList(),
        ModList = ModList,
        AfterGame = AfterGame,
        Crashed = Crashed,
        CrashLine = CrashLine,
        Suggested = Suggested.Where(suggested => suggested.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList()
    };
}

/// <summary>
/// One click mod reports: Works, Broken or Works only with for each mod, sent anonymously to
/// <see cref="ModReports.Endpoint"/> without a GitHub account. The exact text Send sends is shown in
/// the window from the start and follows each pick, so one Send click sends what the player sees.
/// The Ask me after a game box is the opt in for the prompt after a game closes; it is off until
/// the player ticks it.
/// </summary>
public sealed class ModReportWindow : Window
{
    public const string PreviewLine = "This is exactly what Send sends. Nothing leaves your PC before you click Send.";

    /// <summary>A report was sent from any report window, so the Played with mods? line can go.</summary>
    public static event Action? ReportSent;

    private readonly ModReportRequest _request;
    private readonly ModReportSettings _settings = ModReports.LoadSettings();
    private readonly List<Row> _rows = new();
    private readonly TextBox _preview = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MaxHeight = 170,
        Margin = new Thickness(0, 6, 0, 0),
        Padding = new Thickness(6),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Background = BrushOf(16, 22, 27),
        Foreground = BrushOf(213, 222, 230),
        BorderBrush = BrushOf(58, 76, 92),
        FlowDirection = FlowDirection.LeftToRight
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _send;

    private sealed class Row
    {
        public required ModReportChoice Mod { get; init; }
        public string? Result { get; set; }
        public required ComboBox With { get; init; }
        public required List<Button> Buttons { get; init; }
        public required TextBlock Sent { get; init; }
    }

    public ModReportWindow(ModReportRequest request)
    {
        _request = request;
        Title = request.Crashed ? "Tell everyone which mod broke"
            : request.AfterGame ? "Share how your mods worked?"
            : request.Rate.Count == 1 ? "Report how this mod worked" : "Report how your mods worked";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = BrushOf(14, 20, 24);
        Foreground = BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock
        {
            Text = Title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(Muted(request.Crashed
            ? "The game crashed. If you know which mod did it, mark it Broken. Leave the mods you are not sure about empty."
            : request.AfterGame
                ? "The game closed normally. Mark how the mods you played with worked. Leave a mod empty to skip it."
                : request.Rate.Count == 1
                    ? "Pick what happened with this mod in Coop. No GitHub account needed."
                    : "Pick what happened with each mod in Coop. Leave a mod empty to skip it. No GitHub account needed."));
        if (request.Suggested.Count > 0)
            stack.Children.Add(new TextBlock
            {
                Text = "The crash log names the mods marked Broken below. Check them, then click Send.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushOf(224, 139, 132),
                Margin = new Thickness(0, 8, 0, 0)
            });

        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
        foreach (var mod in request.Rate)
            list.Children.Add(MakeRow(mod));
        foreach (var row in _rows.Where(row => request.Suggested.Contains(row.Mod.Id, StringComparer.OrdinalIgnoreCase)))
            Pick(row, ModReportItem.Broken);
        stack.Children.Add(request.Rate.Count > 6
            ? new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            : list);

        var ask = new CheckBox
        {
            Content = "Ask me after a game closes or crashes",
            IsChecked = _settings.AskAfterGame,
            Foreground = BrushOf(213, 222, 230),
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Off until you tick it. Then this window opens once per run after a game you joined closes, so you can rate the mods you played with."
        };
        ask.Checked += (_, _) => SaveAsk(true);
        ask.Unchecked += (_, _) => SaveAsk(false);
        stack.Children.Add(ask);

        // What is sent, in view from the start: the privacy line, then the exact text, kept up to date with each pick.
        stack.Children.Add(Muted(ModReports.PrivacyText, 12, new Thickness(0, 10, 0, 0)));
        stack.Children.Add(Muted(PreviewLine, 12, new Thickness(0, 8, 0, 0)));
        stack.Children.Add(_preview);
        RefreshPreview();
        stack.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        _send = SmallButton("Send", "Sends your report anonymously. One vote per mod version: sending again for the same version replaces your vote.");
        _send.FontWeight = FontWeights.SemiBold;
        _send.Click += async (_, _) => await SendAsync();
        var close = SmallButton("Close", "Closes this window without sending.");
        close.IsCancel = true;
        close.Margin = new Thickness(8, 0, 0, 0);
        close.Click += (_, _) => Close();
        buttons.Children.Add(_send);
        buttons.Children.Add(close);
        stack.Children.Add(buttons);
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Opens the report window over a window. Does nothing without an endpoint (the
    /// tools open the GitHub form then).</summary>
    public static bool Show(Window? owner, ModReportRequest request)
    {
        if (!ModReports.HasEndpoint() || request.Rate.Count == 0)
            return false;
        var window = new ModReportWindow(request);
        if (owner != null && owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
        return true;
    }

    private UIElement MakeRow(ModReportChoice mod)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var head = new DockPanel { LastChildFill = true };
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(choices, Dock.Right);
        head.Children.Add(choices);
        head.Children.Add(new TextBlock
        {
            Text = mod.Version.Length > 0 ? mod.Name + "  " + mod.Version : mod.Name,
            Tag = UiLanguage.NoTranslate,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = mod.Id
        });
        panel.Children.Add(head);

        var with = new ComboBox
        {
            Visibility = Visibility.Collapsed,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "The other mod it only works together with."
        };
        foreach (var other in _request.ModList.Where(other => !other.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)))
            with.Items.Add(new ComboBoxItem { Tag = other.Id, Content = new TextBlock { Text = other.Id, Tag = UiLanguage.NoTranslate } });
        panel.Children.Add(with);

        var sent = new TextBlock { FontSize = 12, Foreground = BrushOf(142, 160, 174), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(sent);

        var row = new Row { Mod = mod, With = with, Buttons = new List<Button>(), Sent = sent };
        foreach (var (result, label, tip) in new[]
        {
            (ModReportItem.Works, "Works", "It worked in Coop for you."),
            (ModReportItem.Broken, "Broken", "It crashed, gave errors or stopped you from joining."),
            (ModReportItem.WorksWith, "Works only with...", "It only worked together with another mod of your list. Pick that mod below.")
        })
        {
            var button = SmallButton(label, tip);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Click += (_, _) => Pick(row, row.Result == result ? null : result);
            row.Buttons.Add(button);
            choices.Children.Add(button);
        }

        with.SelectionChanged += (_, _) => RefreshPreview();
        if (with.Items.Count == 0)
            row.Buttons[2].IsEnabled = false;
        _rows.Add(row);
        ShowSent(row);
        return panel;
    }

    private void Pick(Row row, string? result)
    {
        row.Result = result;
        string[] results = { ModReportItem.Works, ModReportItem.Broken, ModReportItem.WorksWith };
        for (var i = 0; i < row.Buttons.Count; i++)
        {
            var on = results[i] == result;
            row.Buttons[i].Background = on ? BrushOf(31, 78, 121) : BrushOf(27, 40, 51);
            row.Buttons[i].BorderBrush = on ? BrushOf(142, 180, 212) : BrushOf(58, 76, 92);
        }

        row.With.Visibility = result == ModReportItem.WorksWith ? Visibility.Visible : Visibility.Collapsed;
        if (result == ModReportItem.WorksWith && row.With.SelectedIndex < 0 && row.With.Items.Count > 0)
            row.With.SelectedIndex = 0;
        RefreshPreview();
    }

    private void ShowSent(Row row)
    {
        var sent = ModReports.SentFor(_settings, row.Mod.Id, row.Mod.Version, _request.Tool);
        row.Sent.Text = sent == null ? "" : sent.Result switch
        {
            ModReportItem.Broken => "You already sent Broken for this version.",
            ModReportItem.WorksWith => "You already sent Works only with " + sent.With + " for this version.",
            _ => "You already sent Works for this version."
        };
        row.Sent.Visibility = sent == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveAsk(bool on)
    {
        _settings.AskAfterGame = on;
        ModReports.SaveSettings(_settings);
    }

    private ModReport BuildReport()
    {
        var items = _rows
            .Where(row => row.Result != null)
            .Select(row => new ModReportItem
            {
                Id = row.Mod.Id,
                Name = row.Mod.Name,
                Version = row.Mod.Version,
                Result = row.Result!,
                With = row.Result == ModReportItem.WorksWith ? (row.With.SelectedItem as ComboBoxItem)?.Tag as string : null
            });
        var report = ModReports.Build(ModReports.InstallId(_settings), _request.Tool, _request.ToolVersion, _request.Coop, _request.Game, items, _request.ModList);
        report.Reports = ModReports.NotSentYet(_settings, report.Reports, _request.Tool);
        return report;
    }

    // The exact text Send sends right now, the same report SendAsync builds.
    private void RefreshPreview() => _preview.Text = ModReports.PreviewText(BuildReport());

    private async Task SendAsync()
    {
        var report = BuildReport();
        if (_rows.All(row => row.Result == null))
        {
            Say("Pick Works, Broken or Works only with for at least one mod.", good: false);
            return;
        }

        if (report.Reports.Count == 0)
        {
            Say("You already sent these results for these versions.", good: true);
            return;
        }

        // The text in view is the text that goes: one click sends it.
        _preview.Text = ModReports.PreviewText(report);
        _send.IsEnabled = false;
        Say("Sending the report.", good: true);
        ModReportSendResult result;
        using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CoodszModReports/1");
            result = await ModReports.SendAsync(http, report);
        }

        if (result.Status == ModReportSendStatus.Sent)
        {
            ModReports.RememberSent(_settings, report.Reports, _request.Tool);
            _settings.PreviewSeen = true;
            ModReports.SaveSettings(_settings);
            foreach (var row in _rows)
                ShowSent(row);
            RefreshPreview();
            ReportSent?.Invoke();
        }

        _send.IsEnabled = true;
        Say(result.Message, result.Status is ModReportSendStatus.Sent or ModReportSendStatus.AlreadySent);
    }

    private void Say(string text, bool good)
    {
        _status.Text = BannerlordCoopLauncher.Core.Localization.Translator.T(text);
        _status.Foreground = good ? BrushOf(143, 203, 155) : BrushOf(224, 139, 132);
    }

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
        Cursor = System.Windows.Input.Cursors.Hand,
        Background = BrushOf(27, 40, 51),
        Foreground = BrushOf(213, 222, 230),
        BorderBrush = BrushOf(58, 76, 92)
    };

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => Coodsz.Ui.ToolPalette.Brush(r, g, b);
}

/// <summary>
/// The report entry points every Coodsz client shows: the Report a mod button at the top, the
/// Report how a mod worked row on the mods page, the Mod crashed? Report it link under Connect and
/// the green and red count chips next to a mod. The colors come from the window's ReportRed,
/// ReportRedBack, ReportGold, ReportGoldBack, ReportGreen, ReportGreenBack and ReportInk brushes,
/// so each tool keeps its own palette.
/// </summary>
public static class ModReportUi
{
    public const string RedKey = "ReportRed";
    public const string RedBackKey = "ReportRedBack";
    public const string GoldKey = "ReportGold";
    public const string GoldBackKey = "ReportGoldBack";
    public const string GreenKey = "ReportGreen";
    public const string GreenBackKey = "ReportGreenBack";
    public const string InkKey = "ReportInk";

    public const string MainButtonText = "Report a mod";
    public const string MainButtonTip = "Tell other players which mod works or broke for you. You pick Works or Broken for each mod, see exactly what is sent, then send it. No GitHub account needed.";
    public const string CrashLinkText = "Mod crashed? Report it";
    public const string CrashLinkTip = "Opens the report window for the mods of this setup, so you can mark the one that crashed as Broken.";
    public const string ChipTip = "What players using Coodsz tools reported for this mod. Add yours with Report a mod.";
    public const string HintText = "Played with mods? Tell others if they worked.";
    public const string HintCloseTip = "Hides this line for good. Report a mod stays on the Logs and help page.";
    public const string RowLinkText = "Report";

    /// <summary>A brush of the window's palette, or the fallback color when the window has none.</summary>
    public static Brush Paint(FrameworkElement scope, string key, Color fallback) =>
        scope.TryFindResource(key) as Brush ?? new SolidColorBrush(ToolPalette.Of(fallback));

    public static Brush Red(FrameworkElement scope) => Paint(scope, RedKey, Color.FromRgb(0xE0, 0x8B, 0x84));
    public static Brush RedBack(FrameworkElement scope) => Paint(scope, RedBackKey, Color.FromRgb(0x3A, 0x24, 0x22));
    public static Brush Gold(FrameworkElement scope) => Paint(scope, GoldKey, Color.FromRgb(0xE2, 0xC2, 0x7A));
    public static Brush GoldBack(FrameworkElement scope) => Paint(scope, GoldBackKey, Color.FromRgb(0x3A, 0x34, 0x20));
    public static Brush Green(FrameworkElement scope) => Paint(scope, GreenKey, Color.FromRgb(0x8F, 0xCB, 0x9B));
    public static Brush GreenBack(FrameworkElement scope) => Paint(scope, GreenBackKey, Color.FromRgb(0x1E, 0x33, 0x26));
    public static Brush Ink(FrameworkElement scope) => Paint(scope, InkKey, Color.FromRgb(0x10, 0x12, 0x14));

    /// <summary>A filled button in one accent color with an optional icon in front of the text.</summary>
    public static Button Filled(FrameworkElement scope, string text, string tip, Brush fill, string? icon = null, double size = 15, Thickness? padding = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null)
            content.Children.Add(new TextBlock
            {
                Text = icon,
                Tag = UiLanguage.NoTranslate,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
                FontSize = size,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        content.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        return new Button
        {
            Content = content,
            ToolTip = tip,
            Background = fill,
            BorderBrush = fill,
            Foreground = Ink(scope),
            BorderThickness = new Thickness(2),
            Padding = padding ?? new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = ButtonTemplate()
        };
    }

    /// <summary>An outlined button in one accent color.</summary>
    public static Button Outlined(FrameworkElement scope, string text, string tip, Brush color, Brush back)
    {
        var button = Filled(scope, text, tip, back, size: 14, padding: new Thickness(14, 7, 14, 7));
        button.BorderBrush = color;
        button.Foreground = color;
        return button;
    }

    /// <summary>The Report a mod button for the top of a main window.</summary>
    public static Button MainButton(FrameworkElement scope, Action click)
    {
        var button = Filled(scope, MainButtonText, MainButtonTip, Red(scope), "📣");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "ReportModButton");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// The calm line under the title: Played with mods? Tell others if they worked. Report a mod, with
    /// a small x. Null once the player sent a report or closed it; it also goes the moment a report is sent.
    /// </summary>
    public static FrameworkElement? Hint(FrameworkElement scope, Action click)
    {
        if (!ModReports.ShowsHint(ModReports.LoadSettings()))
            return null;
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(panel, "ReportHint");
        panel.Children.Add(new TextBlock
        {
            Text = HintText,
            FontSize = 14,
            Foreground = Paint(scope, "Muted", Color.FromRgb(0x8E, 0xA0, 0xAE)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var link = TextLink(MainButtonText, MainButtonTip, Gold(scope), 14, click);
        link.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(link);
        var x = new Button
        {
            Content = new TextBlock { Text = "✕", Tag = UiLanguage.NoTranslate, FontSize = 12 },
            ToolTip = HintCloseTip,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Paint(scope, "Muted", Color.FromRgb(0x8E, 0xA0, 0xAE)),
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = ButtonTemplate()
        };
        void Gone()
        {
            panel.Visibility = Visibility.Collapsed;
            ModReportWindow.ReportSent -= Gone;
        }
        x.Click += (_, _) =>
        {
            var settings = ModReports.LoadSettings();
            settings.HintClosed = true;
            ModReports.SaveSettings(settings);
            Gone();
        };
        panel.Children.Add(x);
        ModReportWindow.ReportSent += Gone;
        return panel;
    }

    /// <summary>The small Report link on a mod row: opens the report window for that mod.</summary>
    public static Button RowLink(FrameworkElement scope, Action click)
    {
        var link = TextLink(RowLinkText, ModReports.ClickTip, Gold(scope), 12, click);
        link.VerticalAlignment = VerticalAlignment.Center;
        link.Margin = new Thickness(0, 1, 6, 1);
        return link;
    }

    // An underlined text button in one color.
    private static Button TextLink(string text, string tip, Brush color, double size, Action click)
    {
        var link = new Button
        {
            Content = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = color, TextDecorations = TextDecorations.Underline },
            ToolTip = tip,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = ButtonTemplate()
        };
        link.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return link;
    }

    /// <summary>The Mod crashed? Report it link line for under Connect.</summary>
    public static Button CrashLink(FrameworkElement scope, Action click)
    {
        var red = Red(scope);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = "⚠", Tag = UiLanguage.NoTranslate, FontSize = 14, Foreground = red, Margin = new Thickness(0, 0, 6, 0) });
        content.Children.Add(new TextBlock { Text = CrashLinkText, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = red, TextDecorations = TextDecorations.Underline });
        var link = new Button
        {
            Content = content,
            ToolTip = CrashLinkTip,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            Template = ButtonTemplate()
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(link, "CrashReportLink");
        link.Click += (_, _) => click();
        return link;
    }

    /// <summary>The green and red count chips (12 say works, 2 say broken), or null when nobody reported the mod.</summary>
    public static FrameworkElement? CountChips(FrameworkElement scope, int works, int broken)
    {
        var worksText = ModReports.WorksChip(works);
        var brokenText = ModReports.BrokenChip(broken);
        if (worksText == null && brokenText == null)
            return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = ChipTip };
        if (worksText != null)
            panel.Children.Add(Chip(worksText, Green(scope), GreenBack(scope)));
        if (brokenText != null)
            panel.Children.Add(Chip(brokenText, Red(scope), RedBack(scope)));
        return panel;
    }

    /// <summary>
    /// The count chips with ModderLords' verdict chip in front (Tested: works, Works with settings or Tested:
    /// broken, dimmed when tested on an older version), or null when there is neither.
    /// </summary>
    public static FrameworkElement? CountChips(FrameworkElement scope, ModCompatEntry? entry, CompatTestedChip? tested)
    {
        var counts = entry == null ? null : CountChips(scope, entry.Works, entry.Broken);
        if (tested == null)
            return counts;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(TestedChip(scope, tested.Translated()));
        if (counts != null)
            panel.Children.Add(counts);
        return panel;
    }

    private static Border TestedChip(FrameworkElement scope, CompatTestedChip tested)
    {
        var (color, back) = tested.Kind switch
        {
            "Broken" => (Red(scope), RedBack(scope)),
            "NeedsRecipe" => ((Brush)Coodsz.Ui.ToolPalette.Brush(0xD2, 0xB3, 0x6A), (Brush)Coodsz.Ui.ToolPalette.Brush(0x33, 0x30, 0x1E)),
            _ => (Green(scope), GreenBack(scope))
        };
        var chip = Chip(tested.Text, color, back);
        chip.ToolTip = tested.Tip;
        chip.MaxWidth = 170;
        if (chip.Child is TextBlock text)
            text.TextTrimming = TextTrimming.CharacterEllipsis;
        if (tested.OlderVersion)
            chip.Opacity = 0.6;
        return chip;
    }

    private static Border Chip(string text, Brush color, Brush back) => new()
    {
        Background = back,
        BorderBrush = color,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(7, 1, 7, 1),
        Margin = new Thickness(0, 1, 6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = color }
    };

    /// <summary>The colored frame of a bar: the text, the buttons (beside it, or under it for a crash bar) and the x. With no close there is no x.</summary>
    public static Border Frame(FrameworkElement scope, Brush color, Brush back, FrameworkElement text, IEnumerable<UIElement> buttons, string? closeTip, Action? close, bool buttonsBelow = false)
    {
        var x = close == null ? null : new Button
        {
            Content = new TextBlock { Text = "✕", Tag = UiLanguage.NoTranslate, FontSize = 14, FontWeight = FontWeights.Bold },
            ToolTip = closeTip,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = color,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(12, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Template = ButtonTemplate()
        };
        if (x != null)
            x.Click += (_, _) => close!();
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in buttons)
            row.Children.Add(button);
        var dock = new DockPanel { LastChildFill = true };
        if (x != null)
        {
            DockPanel.SetDock(x, Dock.Right);
            dock.Children.Add(x);
        }

        if (buttonsBelow)
        {
            row.Margin = new Thickness(0, 10, 0, 0);
            row.HorizontalAlignment = HorizontalAlignment.Left;
            var stack = new StackPanel();
            stack.Children.Add(text);
            stack.Children.Add(row);
            dock.Children.Add(stack);
        }
        else
        {
            row.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(row, Dock.Right);
            dock.Children.Add(row);
            dock.Children.Add(text);
        }

        return new Border
        {
            Background = back,
            BorderBrush = color,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 12, 12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = dock
        };
    }

    /// <summary>A plain rounded button look that keeps the colors it is given.</summary>
    public static ControlTemplate ButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Control.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Control.Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "Bd"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7, "Bd"));
        var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(off);
        return template;
    }
}

/// <summary>
/// The report bar at the top of a main window, in one ContentControl slot: the one time cards
/// (New: send crash reports right from here, then New: report mods right here), How did your mods
/// work? after a game closed normally, and after a crash the crash bar. The crash bar's biggest
/// button is Send crash report, Tell everyone which mod broke is the second action and Save a copy
/// instead is the small link. Nothing is sent from here: Send crash report opens the consent
/// window and the mod buttons open the report window, which both show what is sent first.
/// </summary>
public sealed class ModReportBar
{
    public const string IntroTitle = "New: report mods right here";
    public const string IntroText = "Did a mod work or crash for you? Click Report a mod, pick Works or Broken, and send it. No GitHub account needed, and everyone using Coodsz tools sees the results.";
    public const string IntroButton = "Try it";
    public const string AfterGameText = "How did your mods work? Report them so other players can see.";
    public const string AfterGameButton = "Report";
    public const string CrashTitle = "The game crashed";
    public const string CrashText = "Tell everyone which mod broke, so other players see it next to that mod before they install it.";
    public const string CrashButton = "Tell everyone which mod broke";
    public const string CrashSendText = "Send a crash report so Coodsz gets your cleaned logs, then post only the code in Discord.";
    public const string CrashModText = "Know which mod did it? Tell everyone which mod broke too.";

    private readonly Window _owner;
    private readonly ContentControl _host;
    private readonly Func<ModReportRequest> _current;
    private readonly Action<string> _log;
    private readonly Action? _saveLogs;

    public ModReportBar(Window owner, ContentControl host, Func<ModReportRequest> current, Action<string> log, Action? saveLogs = null)
    {
        _owner = owner;
        _host = host;
        _current = current;
        _log = log;
        _saveLogs = saveLogs;
        _host.Visibility = Visibility.Collapsed;
    }

    /// <summary>The tool the one time cards are kept for: coop, taom or ad1259.</summary>
    public string Tool { get; init; } = "coop";

    /// <summary>Builds the crash report for a crash or for Send my logs. Without it the bar shows no crash report buttons.</summary>
    public Func<CrashContext, CrashReportRequest>? CrashRequest { get; init; }

    public ModReportBarKind Kind { get; private set; }

    /// <summary>The last run that crashed while this tool is open, for the link under Connect and after a log zip.</summary>
    public ModReportRequest? LastCrash { get; private set; }

    // The crash endpoint, or none when this tool gave no way to build a crash report.
    private string? CrashEndpoint => CrashRequest == null ? null : CrashReports.Endpoint;

    /// <summary>The report window for the enabled mods of the setup, or a short message when there is none to rate.</summary>
    public void OpenCurrent() => Open(_current());

    /// <summary>The report window for the last crash of this run, or for the enabled mods ready to mark one Broken.</summary>
    public void OpenCrash() => Open(LastCrash ?? _current().AsCrash(null));

    // Always visible feedback: with nothing to rate (like plain TAOM) a short message says so.
    private void Open(ModReportRequest request)
    {
        if (ModReports.NothingToRate(request.Rate.Count) is { } nothing)
        {
            _log(nothing);
            UiLanguage.Message(nothing, ModReportUi.MainButtonText, MessageBoxButton.OK, MessageBoxImage.Information, _owner.IsLoaded ? _owner : null);
            return;
        }

        ModReportWindow.Show(_owner, request);
    }

    /// <summary>Send my logs: the consent window for the logs of this PC, for a problem that is not a crash.</summary>
    public void OpenLogs()
    {
        if (CrashRequest != null)
            CrashReportWindow.Open(_owner, CrashRequest(CrashContext.Logs), _log);
    }

    /// <summary>The one time card on the first start after the update: the crash report card first, then
    /// the mod report card on a later start. Each is gone for good once closed or used.</summary>
    public void ShowIntroOnce()
    {
        // One thing at a time: a start that opens What's new (or the language pick or the license)
        // keeps the card for a later start.
        if (UiLanguage.StartupPopupThisStart)
            return;
        var settings = ModReports.LoadSettings();
        switch (CrashReports.IntroFor(settings, Tool, CrashEndpoint))
        {
            case IntroCard.CrashReports:
                Put(ModReportBarKind.Intro, BuildCrashIntro());
                // Seen once is enough: the card stays for this start and never comes back, also when nobody pressed it.
                if (Kind != ModReportBarKind.Intro)
                    break;
                CrashReports.MarkIntroSeen(settings, Tool, "client");
                ModReports.SaveSettings(settings);
                break;
            case IntroCard.ModReports:
                Put(ModReportBarKind.Intro, BuildIntro());
                if (Kind != ModReportBarKind.Intro)
                    break;
                settings.IntroSeen = true;
                ModReports.SaveSettings(settings);
                break;
        }
    }

    /// <summary>After a game closed: the window (when the player turned it on), the crash bar after a
    /// crash, or the small bar after a normal close, by <see cref="ModReports.AfterGame"/>. A crash
    /// always brings the crash bar, also with no mod to rate, so the crash report can be sent.</summary>
    public void AfterGame(ModReportRequest request, bool crashed)
    {
        var prompt = ModReports.AfterGame(ModReports.LoadSettings(), request.Rate.Count, crashed);
        if (crashed && prompt != ModReportPrompt.CrashBar)
            Crash(request);
        switch (prompt)
        {
            case ModReportPrompt.Window:
                ModReports.MarkAsked();
                if (crashed)
                    LastCrash = request;
                ModReportWindow.Show(_owner, request);
                break;
            case ModReportPrompt.CrashBar:
                Crash(request);
                break;
            case ModReportPrompt.Bar:
                ModReports.MarkBarShown();
                Put(ModReportBarKind.AfterGame, BuildAfterGame(request));
                break;
        }
    }

    /// <summary>The game crashed: the crash bar with Send crash report and Tell everyone which mod broke for that run's mods.</summary>
    public void Crash(ModReportRequest request)
    {
        if (!CrashReports.ShowsClientCrashBar(request.Rate.Count, CrashEndpoint))
            return;
        LastCrash = request;
        Put(ModReportBarKind.Crash, BuildCrash(request));
    }

    /// <summary>The log zip was saved: after a crash this run the crash bar comes back, so the player can report the mod too.</summary>
    public void AfterLogZip()
    {
        if (LastCrash != null)
            Put(ModReportBarKind.Crash, BuildCrash(LastCrash));
    }

    public void Hide()
    {
        Kind = ModReportBarKind.None;
        _host.Content = null;
        _host.Visibility = Visibility.Collapsed;
    }

    private void Put(ModReportBarKind kind, FrameworkElement content)
    {
        if (!ModReports.Replaces(kind, Kind))
            return;
        Kind = kind;
        _host.Content = content;
        _host.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildIntro()
    {
        var green = ModReportUi.Green(_host);
        var tryIt = ModReportUi.Filled(_host, IntroButton, ModReportUi.MainButtonTip, green, "📣");
        tryIt.Click += (_, _) =>
        {
            IntroDone();
            OpenCurrent();
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = IntroTitle, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = green, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = IntroText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return Frame(green, ModReportUi.GreenBack(_host), text, new[] { (UIElement)tryIt },
            "Hides this card now. Report a mod stays in this window.", IntroDone);
    }

    private void IntroDone()
    {
        var settings = ModReports.LoadSettings();
        settings.IntroSeen = true;
        ModReports.SaveSettings(settings);
        if (Kind == ModReportBarKind.Intro)
            Hide();
    }

    // New: send crash reports right from here. Try it opens Send my logs.
    private FrameworkElement BuildCrashIntro()
    {
        var green = ModReportUi.Green(_host);
        var tryIt = ModReportUi.Filled(_host, CrashReportUi.IntroButton, CrashReportUi.SendLogsTip, green, "📤");
        System.Windows.Automation.AutomationProperties.SetAutomationId(tryIt, "CrashIntroTryIt");
        tryIt.Click += (_, _) =>
        {
            CrashIntroDone();
            OpenLogs();
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = CrashReportUi.IntroTitle, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = green, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = CrashReportUi.IntroText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return Frame(green, ModReportUi.GreenBack(_host), text, new[] { (UIElement)tryIt },
            "Hides this card now. Send my logs stays on the page.", CrashIntroDone);
    }

    private void CrashIntroDone()
    {
        var settings = ModReports.LoadSettings();
        CrashReports.MarkIntroSeen(settings, Tool, "client");
        ModReports.SaveSettings(settings);
        if (Kind == ModReportBarKind.Intro)
            Hide();
    }

    private FrameworkElement BuildAfterGame(ModReportRequest request)
    {
        var gold = ModReportUi.Gold(_host);
        var report = ModReportUi.Filled(_host, AfterGameButton, "Opens the report window for the mods you just played with. Nothing is sent until you click Send.", gold, "📣");
        report.Click += (_, _) =>
        {
            var settings = ModReports.LoadSettings();
            ModReports.UsedBar(settings);
            ModReports.SaveSettings(settings);
            Hide();
            ModReportWindow.Show(_owner, request);
        };
        var text = new TextBlock { Text = AfterGameText, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = gold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        return Frame(gold, ModReportUi.GoldBack(_host), text, new[] { (UIElement)report },
            "Hides this bar. Close it three times in a row and it stays hidden.", () =>
            {
                var settings = ModReports.LoadSettings();
                ModReports.CloseBar(settings);
                ModReports.SaveSettings(settings);
                Hide();
            });
    }

    // The crash report for this crash: its crash line and the first mod the crash log names.
    private CrashReportRequest CrashReportFor(ModReportRequest request) =>
        CrashRequest!(new CrashContext(CrashReason.Crash, request.CrashLine, request.Suggested.FirstOrDefault(), CrashedAtUtc: request.CrashedAtUtc));

    private const string TellTip = "Opens the report window with the mods of that run. Mark the mod that crashed as Broken. Nothing is sent until you click Send.";

    private FrameworkElement BuildCrash(ModReportRequest request)
    {
        var red = ModReportUi.Red(_host);
        var buttons = new List<UIElement>();
        foreach (var kind in CrashReports.CrashBarButtons(request.Rate.Count, CrashEndpoint))
        {
            switch (kind)
            {
                case CrashBarButton.SendCrashReport:
                    // The biggest button: the cleaned logs go to Coodsz, the player gets a code for Discord.
                    var send = ModReportUi.Filled(_host, CrashReportUi.CrashButton, CrashReportUi.CrashButtonTip, red, "📤", size: 21, padding: new Thickness(28, 14, 28, 14));
                    System.Windows.Automation.AutomationProperties.SetAutomationId(send, "SendCrashReport");
                    send.Click += (_, _) => CrashReportWindow.Open(_owner, CrashReportFor(request), _log);
                    buttons.Add(send);
                    break;
                case CrashBarButton.TellWhichMod:
                    // The second action, or the biggest one when crash reports are off.
                    var tell = buttons.Count == 0
                        ? ModReportUi.Filled(_host, CrashButton, TellTip, red, "⚠", size: 19, padding: new Thickness(24, 12, 24, 12))
                        : ModReportUi.Outlined(_host, CrashButton, TellTip, red, ModReportUi.RedBack(_host));
                    if (buttons.Count > 0)
                        tell.Margin = new Thickness(14, 0, 0, 0);
                    tell.Click += (_, _) => ModReportWindow.Show(_owner, request);
                    buttons.Add(tell);
                    break;
                case CrashBarButton.SaveCopy:
                    // The old Save logs for help, now the small offline way: the same cleaned zip, saved here.
                    if (CrashRequest == null && _saveLogs == null)
                        break;
                    var save = CrashReportUi.SaveCopyLink(_host, () =>
                    {
                        if (CrashRequest != null)
                            _ = CrashReportWindow.SaveCopyAsync(_owner, CrashReportFor(request), _log);
                        else
                            _saveLogs?.Invoke();
                    });
                    save.Margin = new Thickness(20, 0, 0, 0);
                    buttons.Add(save);
                    break;
            }
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = CrashTitle, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = red });
        var offersSend = CrashReports.HasEndpoint(CrashEndpoint);
        text.Children.Add(new TextBlock { Text = offersSend ? CrashSendText : CrashText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (offersSend && ModReports.OffersCrashReport(request.Rate.Count))
            text.Children.Add(new TextBlock { Text = CrashModText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var named = request.Rate.Where(mod => request.Suggested.Contains(mod.Id, StringComparer.OrdinalIgnoreCase)).Select(mod => mod.Name).ToList();
        if (named.Count > 0)
            text.Children.Add(new TextBlock
            {
                Text = "The crash log names " + string.Join(", ", named) + ".",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = red,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        return Frame(red, ModReportUi.RedBack(_host), text, buttons, "Hides this bar.", Hide, buttonsBelow: true);
    }

    private Border Frame(Brush color, Brush back, FrameworkElement text, IEnumerable<UIElement> buttons, string closeTip, Action close, bool buttonsBelow = false) =>
        ModReportUi.Frame(_host, color, back, text, buttons, closeTip, close, buttonsBelow);
}
