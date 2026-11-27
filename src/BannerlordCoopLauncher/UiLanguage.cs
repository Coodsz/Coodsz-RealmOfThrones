using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace Coodsz.Ui;

/// <summary>
/// Shows every window in the picked language without changing how the windows are
/// written. When an element loads, its text, button caption, header, tool tip, or
/// window title goes through <see cref="Translator"/>, and later changes from code are
/// translated the same way. The English text is kept, so switching language or reading
/// the English back (<see cref="English"/>) still works. Text boxes are left alone:
/// they hold what the player typed and the logs.
/// </summary>
public static class UiLanguage
{
    private sealed class Shown
    {
        public string English = "";
        public string Text = "";
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Shown>> Texts = new();
    private static readonly HashSet<DependencyObject> Live = new();
    private static bool _started;
    private static bool _askFirst;
    private static int _applying;

    /// <summary>Call once in App.OnStartup, before the first window opens.</summary>
    public static void Start()
    {
        if (_started)
            return;
        _started = true;
        Translator.Use(Translator.StartupLanguage());
        _askFirst = !Translator.HasSavedLanguage;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler(OnToolTipOpening), true);
        // A context menu (the More buttons) opens in its own popup, outside the window walk.
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened), true);
        // A tool tip opens in its own popup too and never takes the window's language, so it gets the font here.
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnToolTipOpened), true);
        Translator.Changed += Refresh;
        HoverTips();
    }

    /// <summary>
    /// Hover tips come up a little sooner than the Windows default, stay while they are read,
    /// and still show on a button that is off, so it can say what it does (or why it is off).
    /// The look is the ToolTip style in each tool's App.xaml.
    /// </summary>
    private static void HoverTips()
    {
        try
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(400));
            ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(30000));
            ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
        }
        catch (ArgumentException)
        {
            // Already set for this process.
        }
    }

    /// <summary>The English text behind a translated text block, for code that compares it.</summary>
    public static string English(TextBlock block) => English(block, TextBlock.TextProperty);

    public static string English(DependencyObject element, DependencyProperty property)
    {
        var value = element.GetValue(property) as string ?? "";
        return Texts.TryGetValue(element, out var map) && map.TryGetValue(property, out var shown) && shown.Text == value
            ? shown.English
            : value;
    }

    // Log boxes show the picked language, but log zips and problem reports keep the
    // English, so support can read every report. Each text box keeps its English copy here.
    private static readonly ConditionalWeakTable<TextBox, StringBuilder> EnglishLogs = new();

    /// <summary>Adds a log line: translated on screen, English in <see cref="EnglishLog"/>.</summary>
    public static void AppendLog(TextBox box, string prefix, string line, string suffix = "")
    {
        var english = EnglishLogs.GetValue(box, _ => new StringBuilder(box.Text));
        english.Append(prefix).Append(line).Append(suffix);
        if (english.Length > 400_000)
            english.Remove(0, english.Length - 300_000);
        box.AppendText(prefix + TranslateLines(line) + suffix);
    }

    /// <summary>Replaces a log box's text (a log file read back in).</summary>
    public static void SetLog(TextBox box, string english)
    {
        EnglishLogs.AddOrUpdate(box, new StringBuilder(english));
        box.Text = TranslateLines(english);
    }

    /// <summary>The English text of a log box, for zips and reports.</summary>
    public static string EnglishLog(TextBox box) =>
        EnglishLogs.TryGetValue(box, out var english) ? english.ToString() : box.Text;

    /// <summary>A log run that shows translated text (or the shown text given) and remembers the English.</summary>
    public static Run LogRun(string english, string? shown = null)
    {
        shown ??= TranslateLines(english);
        return shown == english ? new Run(english) : new Run(shown) { Tag = english };
    }

    /// <summary>The English text of a rich log box (runs made with <see cref="LogRun"/>).</summary>
    public static string EnglishText(RichTextBox box)
    {
        var text = new StringBuilder();
        foreach (var block in box.Document.Blocks)
        {
            if (block is Paragraph paragraph)
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is Run run)
                        text.Append(run.Tag as string ?? run.Text);
                    else if (inline is LineBreak)
                        text.Append(Environment.NewLine);
                }

            text.Append(Environment.NewLine);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Translates each line on its own; server output has no entry and stays as written.</summary>
    private static string TranslateLines(string text)
    {
        if (Translator.Current == Translator.English || text.Length == 0)
            return text;
        if (!text.Contains('\n'))
            return Translator.T(text);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var cr = line.EndsWith('\r');
            var core = cr ? line[..^1] : line;
            lines[i] = (core.Length == 0 ? core : Translator.T(core)) + (cr ? "\r" : "");
        }

        return string.Join("\n", lines);
    }

    /// <summary>MessageBox.Show with the text and caption translated.</summary>
    public static MessageBoxResult Message(string text, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, Window? owner = null, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        owner != null
            ? MessageBox.Show(owner, Translator.T(text), Translator.T(caption), button, icon, defaultResult,
                Translator.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None)
            : MessageBox.Show(Translator.T(text), Translator.T(caption), button, icon, defaultResult,
            Translator.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);

    /// <summary>
    /// First start on this PC: asks for the language over the first window, with every
    /// choice written in its own language so a player who reads no English finds theirs.
    /// </summary>
    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window owner)
            return;
        // Windows that keep their own words (the language list, the license) still need the font.
        EastAsianFont(owner);
        if (owner.Tag is NoTranslate)
            return;
        Watch(owner);
        if (!_askFirst && _whatsNew == null && _licenseTool == null)
            return;
        _popupThisStart |= StartupPopupPending();
        var ask = _askFirst;
        var whatsNew = _whatsNew;
        var licenseTool = _licenseTool;
        _askFirst = false;
        _whatsNew = null;
        _licenseTool = null;
        // Nothing in the window can be used until the license is accepted.
        if (licenseTool != null)
            owner.IsEnabled = false;
        // Language first, so the license and the notes come up in the language the player just picked.
        owner.Dispatcher.BeginInvoke(() =>
        {
            if (ask)
                AskLanguage(owner);
            if (licenseTool != null)
            {
                if (!AskLicense(owner, licenseTool))
                {
                    Application.Current?.Shutdown();
                    return;
                }

                owner.IsEnabled = true;
            }

            if (whatsNew is { } notes)
                ShowWhatsNewNow(owner, notes.Tool, notes.Version, notes.Entries);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static (string Tool, string Version, IReadOnlyList<WhatsNewEntry> Entries)? _whatsNew;

    private static bool _popupThisStart;

    /// <summary>
    /// True when this start opens the language pick, the license or the What's new popup. The one
    /// time cards (New: send crash reports, New: report mods) then wait for a later start, so a
    /// player gets one thing at a time.
    /// </summary>
    public static bool StartupPopupThisStart => _popupThisStart || StartupPopupPending();

    private static bool StartupPopupPending() =>
        _askFirst || _licenseTool != null
        || (_whatsNew is { } notes && WhatsNew.Since(notes.Entries, WhatsNew.LastSeen(notes.Tool)).Count > 0);

    /// <summary>The tool that still has to ask for the license, or null when it was accepted.</summary>
    private static string? _licenseTool;

    /// <summary>
    /// Call in App.OnStartup after <see cref="Start"/>: when this Windows user has not
    /// accepted the current Coodsz Coop Tools License in any of the tools, the first window
    /// asks for it (after the language pick) and stays locked until I agree. Exit closes the tool.
    /// </summary>
    public static void LicenseOnce(string tool)
    {
        if (!BannerlordCoopLauncher.Core.Legal.ToolLicense.IsAccepted())
            _licenseTool = tool;
    }

    /// <summary>The one time license screen. True when the player pressed I agree.</summary>
    public static bool AskLicense(Window? owner, string tool)
    {
        var gold = Coodsz.Ui.ToolPalette.Brush(0xE2, 0xC2, 0x7A);
        var ivory = Coodsz.Ui.ToolPalette.Brush(0xF1, 0xEA, 0xD8);
        var line = Coodsz.Ui.ToolPalette.Brush(0x3A, 0x4C, 0x5C);
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Width = 680 };
        panel.Children.Add(new TextBlock
        {
            Text = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.Title),
            Tag = NoTranslate,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Foreground = gold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.Intro),
            Tag = NoTranslate,
            FontSize = 16,
            Foreground = ivory,
            TextWrapping = TextWrapping.Wrap,
        });
        if (Translator.Current != Translator.English)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.EnglishOnly),
                Tag = NoTranslate,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = Coodsz.Ui.ToolPalette.Brush(0x9F, 0xB3, 0xBF),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        // The license itself stays English: it is the legal text. The third party notices follow it,
        // because the zips no longer carry THIRD-PARTY-NOTICES.md beside the exe.
        var notices = BannerlordCoopLauncher.Core.Legal.ThirdPartyNotices.Text;
        panel.Children.Add(new TextBox
        {
            Text = BannerlordCoopLauncher.Core.Legal.ToolLicense.Text + (notices.Length == 0 ? "" : "\r\n\r\n\r\n" + notices),
            Tag = NoTranslate,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = Math.Min(420, Math.Max(240, SystemParameters.WorkArea.Height - 300)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 14, 0, 0),
            Background = Coodsz.Ui.ToolPalette.Brush(0x0B, 0x10, 0x14),
            Foreground = ivory,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            FlowDirection = FlowDirection.LeftToRight,
        });

        var ownerShown = owner is { IsVisible: true };
        var window = new Window
        {
            Title = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.Title),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = ownerShown ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Coodsz.Ui.ToolPalette.Brush(0x0E, 0x14, 0x18),
            FontFamily = new FontFamily("Segoe UI"),
            FlowDirection = Translator.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            ShowInTaskbar = !ownerShown,
            Tag = NoTranslate,
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(window);
        var agreed = false;
        var exit = new Button
        {
            Content = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.Exit),
            ToolTip = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.ExitTip),
            Tag = NoTranslate,
            FontSize = 16,
            MinWidth = 120,
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = ivory,
            Background = Coodsz.Ui.ToolPalette.Brush(0x1B, 0x28, 0x33),
            BorderBrush = line,
            IsCancel = true,
        };
        exit.Click += (_, _) => window.Close();
        var agree = new Button
        {
            Content = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.Agree),
            ToolTip = Translator.T(BannerlordCoopLauncher.Core.Legal.ToolLicense.AgreeTip),
            Tag = NoTranslate,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 140,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(10, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Brushes.Black,
            Background = gold,
            BorderBrush = gold,
        };
        agree.Click += (_, _) =>
        {
            agreed = true;
            BannerlordCoopLauncher.Core.Legal.ToolLicense.Accept(tool);
            window.Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(exit);
        buttons.Children.Add(agree);
        panel.Children.Add(buttons);
        window.Content = panel;
        if (ownerShown)
            window.Owner = owner;
        // Closing the window without an answer is the same as Exit.
        window.ShowDialog();
        return agreed;
    }

    /// <summary>
    /// Call in App.OnStartup after <see cref="Start"/>: once after each update, the first
    /// window shows what this version changed, in the player's language.
    /// </summary>
    public static void WhatsNewOnce(string tool, string version, IReadOnlyList<WhatsNewEntry> entries)
    {
        _thisTool = (version, entries);
        _whatsNewTool = tool;
        NewBadges.Start(tool, version);
        if (WhatsNew.Since(entries, WhatsNew.LastSeen(tool)).Count > 0)
            _whatsNew = (tool, version, entries);
        else
            WhatsNew.MarkSeen(tool, version);
    }

    /// <summary>The notes <see cref="WhatsNewOnce"/> was given, for the What's new button.</summary>
    private static (string Version, IReadOnlyList<WhatsNewEntry> Entries)? _thisTool;

    // The tool id WhatsNewOnce was given: a client launcher and a server tool share one list, so it picks
    // which picture an entry shows (WhatsNew.PictureFor).
    private static string _whatsNewTool = "";

    /// <summary>
    /// The What's new button: opens the same window on demand, with this version first and
    /// the few before it. It does not change what the popup after an update shows.
    /// </summary>
    public static void ShowWhatsNew(Window? owner)
    {
        if (_thisTool is not { } notes)
            return;
        var shown = WhatsNew.Recent(notes.Entries, notes.Version);
        if (shown.Count > 0)
            ShowWhatsNewWindow(owner, shown, hint: false);
    }

    private static void ShowWhatsNewNow(Window owner, string tool, string version, IReadOnlyList<WhatsNewEntry> entries)
    {
        var shown = WhatsNew.Since(entries, WhatsNew.LastSeen(tool));
        WhatsNew.MarkSeen(tool, version);
        if (shown.Count == 0)
            return;
        ShowWhatsNewWindow(owner, shown, hint: true);
    }

    /// <summary>An entry's picture, or null when it is missing or broken.</summary>
    private static System.Windows.Media.Imaging.BitmapImage? LoadPicture(string name)
    {
        try
        {
            using var stream = WhatsNew.OpenPicture(name);
            if (stream is null)
                return null;
            var picture = new System.Windows.Media.Imaging.BitmapImage();
            picture.BeginInit();
            picture.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            picture.StreamSource = stream;
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ShowWhatsNewWindow(Window? owner, IReadOnlyList<WhatsNewEntry> shown, bool hint)
    {
#if DEBUG
        // Debug builds only, for a screenshot: any COODSZ_WHATSNEW_PREVIEW value puts the Donate picture under the newest entry, as this tool's own copy (WhatsNew.PictureFor).
        if (Environment.GetEnvironmentVariable("COODSZ_WHATSNEW_PREVIEW") is { Length: > 0 } && shown.Count > 0)
        {
            shown = [shown[0] with { Picture = WhatsNew.DonatePicture }, .. shown.Skip(1)];
        }
#endif
        var gold = Coodsz.Ui.ToolPalette.Brush(0xE2, 0xC2, 0x7A);
        var ivory = Coodsz.Ui.ToolPalette.Brush(0xF1, 0xEA, 0xD8);
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24), MaxWidth = 560 };
        // A big release has many lines; they scroll so the window always fits the screen and Got it stays in view.
        var notes = new StackPanel();
        foreach (var entry in shown)
        {
            notes.Children.Add(new TextBlock
            {
                Text = string.Format(Translator.T(WhatsNew.Heading), entry.Version),
                Tag = NoTranslate,
                FontFamily = new FontFamily("Palatino Linotype"),
                FontSize = 22,
                Foreground = gold,
                Margin = new Thickness(0, notes.Children.Count == 0 ? 0 : 14, 0, 8),
            });
            foreach (var line in entry.Lines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var dot = new TextBlock { Text = "•", Tag = NoTranslate, FontSize = 16, Foreground = gold, Margin = new Thickness(0, 0, 10, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = Translator.T(line), Tag = NoTranslate, FontSize = 16, Foreground = ivory, TextWrapping = TextWrapping.Wrap });
                notes.Children.Add(row);
            }
            // A picture only shows when it loads; it shrinks to fit the window but never grows past its own size.
            if (WhatsNew.PictureFor(entry.Picture, _whatsNewTool) is { } name && LoadPicture(name) is { } picture)
            {
                var image = new Image
                {
                    Source = picture,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Thickness(0, 4, 0, 8),
                };
                System.Windows.Automation.AutomationProperties.SetName(image, Translator.T(WhatsNew.PictureLabel(name)));
                notes.Children.Add(image);
            }
        }
        panel.Children.Add(new ScrollViewer
        {
            Content = notes,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 260),
        });
        if (hint)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Translator.T(WhatsNew.OpenAgainHint),
                Tag = NoTranslate,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = Coodsz.Ui.ToolPalette.Brush(0x9F, 0xB3, 0xBF),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });
        }

        var ownerShown = owner is { IsVisible: true };
        var window = new Window
        {
            Title = Translator.T(WhatsNew.Title),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = ownerShown ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Coodsz.Ui.ToolPalette.Brush(0x0E, 0x14, 0x18),
            FontFamily = new FontFamily("Segoe UI"),
            FlowDirection = Translator.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Tag = NoTranslate,
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(window);
        var close = new Button
        {
            Content = Translator.T(WhatsNew.Close),
            ToolTip = Translator.T(WhatsNew.CloseTip),
            Tag = NoTranslate,
            FontSize = 16,
            MinWidth = 140,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Brushes.Black,
            Background = gold,
            BorderBrush = gold,
            IsDefault = true,
        };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;
        if (ownerShown)
            window.Owner = owner;
        window.ShowDialog();
    }

    /// <summary>
    /// The small What's new button that sits next to the language button. It opens the
    /// notes again on demand (<see cref="ShowWhatsNew"/>).
    /// </summary>
    public static Button MakeWhatsNewButton()
    {
        var gold = KeepBrush("Keep.GoldInk", 0xE2, 0xC2, 0x7A);
        var label = new TextBlock { Tag = NoTranslate, FontSize = 14, FontWeight = FontWeights.SemiBold };
        void Show() => label.Text = "✦  " + Translator.T(WhatsNew.Title);
        Show();
        var button = new Button
        {
            Content = label,
            Tag = NoTranslate,
            ToolTip = Translator.T(WhatsNew.ButtonTip),
            Foreground = gold,
            Background = KeepBrush("Keep.SideGoldBack", 0x1B, 0x28, 0x33),
            BorderBrush = gold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => ShowWhatsNew(Window.GetWindow(button));
        Translator.Changed += () => button.Dispatcher.Invoke(() =>
        {
            Show();
            button.ToolTip = Translator.T(WhatsNew.ButtonTip);
        });
        NewBadges.Attach(button, NewFeatures.WhatsNewButton);
        return button;
    }

    /// <summary>
    /// A small heart button next to What's new that opens Coodsz's donation page in the browser. The tools
    /// never ask for money anywhere else.
    /// </summary>
    public static Button MakeDonateButton()
    {
        var gold = KeepBrush("Keep.DonateInk", 0xE2, 0xC2, 0x7A);
        var title = new Run();
        var label = new TextBlock { Tag = NoTranslate, FontSize = 14, FontWeight = FontWeights.SemiBold };
        label.Inlines.Add(new Run("♥") { Foreground = Coodsz.Ui.ToolPalette.Brush(0xE0, 0x5A, 0x6B) });
        label.Inlines.Add(title);
        void Show() => title.Text = "  " + Translator.T(Donate.Title);
        Show();
        // A soft rose glow and a warmer fill than its neighbours, a little stronger under the mouse. It never moves.
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0xE0, 0x5A, 0x6B),
            ShadowDepth = 0,
            BlurRadius = 12,
            Opacity = 0.7,
        };
        var button = new Button
        {
            Content = label,
            Tag = NoTranslate,
            ToolTip = Translator.T(Donate.ButtonTip),
            Foreground = gold,
            Background = KeepBrush("Keep.DonateBack", 0x33, 0x23, 0x2A),
            BorderBrush = KeepBrush("Keep.DonateLine", 0xE2, 0xC2, 0x7A),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = glow,
        };
        button.MouseEnter += (_, _) => (glow.BlurRadius, glow.Opacity) = (18, 1.0);
        button.MouseLeave += (_, _) => (glow.BlurRadius, glow.Opacity) = (12, 0.7);
        // A click lists the Linktree first and Buy Me a Coffee under it. They are names, so the menu stays as
        // written. The dark menu of the client windows, carried here since the Server Tools have no menu styles.
        var menu = new ContextMenu { Tag = NoTranslate, Style = (Style)((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ToolPalette.InXaml(DonateMenuStyles)))["DonateMenu"] };
        foreach (var (name, url) in Donate.Links)
        {
            var item = new MenuItem { Header = name, Tag = NoTranslate, ToolTip = Translator.T(Donate.LinkTip) };
            item.Click += (_, _) => OpenDonateLink(url, button);
            menu.Items.Add(item);
        }

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        Translator.Changed += () => button.Dispatcher.Invoke(() =>
        {
            Show();
            button.ToolTip = Translator.T(Donate.ButtonTip);
            foreach (var line in menu.Items.OfType<MenuItem>())
                line.ToolTip = Translator.T(Donate.LinkTip);
        });
        return button;
    }

    private static void OpenDonateLink(string url, Button button)
    {
        if (ShellOpen.TryOpen(url, out _))
            return;
        // OpenProblemText is translated already. Message would translate it again, and the wide
        // "Could not open {0}. {1}" template would take the whole text and leave it half English.
        var owner = Window.GetWindow(button);
        var options = Translator.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
        if (owner != null)
            MessageBox.Show(owner, Donate.OpenProblemText(url), Translator.T(Donate.Title), MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, options);
        else
            MessageBox.Show(Donate.OpenProblemText(url), Translator.T(Donate.Title), MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, options);
    }

    private const string DonateMenuStyles = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Style x:Key="DonateItem" TargetType="MenuItem">
                <Setter Property="Foreground" Value="#FFE8E6E1"/>
                <Setter Property="FontSize" Value="14"/>
                <Setter Property="Cursor" Value="Hand"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="MenuItem">
                            <Border x:Name="Row" Background="Transparent" Padding="14,8" MinWidth="180">
                                <ContentPresenter ContentSource="Header" RecognizesAccessKey="False" TextElement.Foreground="{TemplateBinding Foreground}"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsHighlighted" Value="True">
                                    <Setter TargetName="Row" Property="Background" Value="#FF22303C"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style x:Key="DonateMenu" TargetType="ContextMenu">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="HasDropShadow" Value="False"/>
                <Setter Property="ItemContainerStyle" Value="{StaticResource DonateItem}"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ContextMenu">
                            <Border Background="#FF12181E" BorderBrush="#FFE2C27A" BorderThickness="1" Padding="0,4">
                                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
        """;

    public static void AskLanguage(Window? owner)
    {
        var gold = Coodsz.Ui.ToolPalette.Brush(0xE2, 0xC2, 0x7A);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "🌐",
            FontSize = 40,
            Foreground = gold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = NoTranslate,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pick your language\nاختر لغتك · Sprache wählen · Choisissez votre langue\nEscolha seu idioma · Выберите язык · Dilini seç\n言語を選択 · 언어 선택 · 选择语言",
            Tag = NoTranslate,
            FontSize = 15,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 6, 0, 16),
        });
        var window = new Window
        {
            Title = "Language",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Coodsz.Ui.ToolPalette.Brush(0x0E, 0x14, 0x18),
            FontFamily = new FontFamily("Segoe UI"),
            ShowInTaskbar = owner == null,
            Tag = NoTranslate,
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(window);
        if (owner != null && owner.IsVisible)
            window.Owner = owner;
        // Two columns, so all ten languages fit on a short or scaled screen without scrolling.
        var choices = new UniformGrid { Columns = 2 };
        panel.Children.Add(choices);
        foreach (var language in Translator.Languages)
        {
            var button = new Button
            {
                Content = language.NativeName,
                Tag = NoTranslate,
                FontSize = 18,
                MinWidth = 220,
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(4, 0, 4, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = language.Code == Translator.Current ? Brushes.Black : gold,
                Background = language.Code == Translator.Current ? gold : Coodsz.Ui.ToolPalette.Brush(0x1B, 0x28, 0x33),
                BorderBrush = gold,
                FlowDirection = FlowDirection.LeftToRight,
            };
            var code = language.Code;
            // 日本語 and 简体中文 share characters drawn differently in each language.
            if (code is "ja" or "ko" or Translator.SimplifiedChinese)
                button.Language = System.Windows.Markup.XmlLanguage.GetLanguage(code);
            button.Click += (_, _) =>
            {
                Translator.Use(code, save: true);
                window.Close();
            };
            choices.Children.Add(button);
        }

        window.ShowDialog();
        // Closing the window without a pick keeps the current language and stops the question.
        if (!Translator.HasSavedLanguage)
            Translator.Use(Translator.Current, save: true);
    }

    /// <summary>
    /// The always-visible language button for a window's header: a globe and the current
    /// language in its own words. Clicking it opens the same list as the first start.
    /// </summary>
    public static Button MakeButton()
    {
        var gold = KeepBrush("Keep.GoldInk", 0xE2, 0xC2, 0x7A);
        var label = new TextBlock { Tag = NoTranslate, FontSize = 14, FontWeight = FontWeights.SemiBold };
        void Show() => label.Text = "🌐  " + (Translator.Languages.FirstOrDefault(language => language.Code == Translator.Current)?.NativeName ?? "English") + "  ▾";
        Show();
        var button = new Button
        {
            Content = label,
            Tag = NoTranslate,
            ToolTip = "Language · اللغة · Sprache · Langue · Idioma · Язык · Dil · 言語 · 언어 · 语言",
            Foreground = gold,
            Background = KeepBrush("Keep.SideGoldBack", 0x3A, 0x34, 0x20),
            BorderBrush = gold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            FlowDirection = FlowDirection.LeftToRight,
        };
        button.Click += (_, _) => AskLanguage(Window.GetWindow(button));
        Translator.Changed += () => button.Dispatcher.Invoke(Show);
        return button;
    }

    /// <summary>A Keep colour of the tool (its KeepColors file), or this one when the tool has no such key.</summary>
    private static Brush KeepBrush(string key, byte r, byte g, byte b) =>
        Application.Current?.TryFindResource(key) as Brush ?? Coodsz.Ui.ToolPalette.Brush(r, g, b);

    /// <summary>Tag value for elements that must stay as written (language names).</summary>
    public const string NoTranslate = "coodsz:no-translate";

    /// <summary>
    /// WPF only raises Loaded on elements that listen for it, so each window is walked
    /// instead: once when it opens, then again shortly after its layout changes (a tab
    /// opened, a panel shown, a list filled). Elements already seen are skipped.
    /// </summary>
    private static void Watch(Window window)
    {
        // After the window's own Loaded work, so a setting it fills in is seen as it is.
        window.Dispatcher.BeginInvoke(() => SafeScan(window), System.Windows.Threading.DispatcherPriority.Loaded);
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SafeScan(window);
        };
        window.LayoutUpdated += (_, _) =>
        {
            if (!timer.IsEnabled)
                timer.Start();
        };
        window.Closed += (_, _) => timer.Stop();
    }

    /// <summary>Translation must never stop a tool from working, so a failure only shows English.</summary>
    private static void SafeScan(DependencyObject root)
    {
        try
        {
            Scan(root);
        }
        catch (Exception exception)
        {
            Report(exception);
        }
    }

    private static readonly HashSet<string> Reported = new();

    private static void Report(Exception exception)
    {
        try
        {
            if (Reported.Add(exception.GetType().Name + exception.TargetSite?.Name))
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "coodsz-language-errors.txt"), DateTime.Now.ToString("u") + " " + exception + Environment.NewLine);
        }
        catch (IOException)
        {
        }
    }

    private static void Scan(DependencyObject node)
    {
        if (node is FrameworkElement { Tag: NoTranslate })
            return;
        if (node is FrameworkElement element)
        {
            try
            {
                Hook(element);
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            Scan(VisualTreeHelper.GetChild(node, i));
    }

    private static void Hook(FrameworkElement element)
    {
        if (!Live.Add(element))
            return;
        element.Unloaded += OnUnloaded;
        foreach (var property in PropertiesOf(element))
        {
            DependencyPropertyDescriptor.FromProperty(property, element.GetType())?.AddValueChanged(element, OnTextChanged);
            Apply(element, property);
        }

        if (element is ComboBox box)
            HookDropdown(box);

        if (element is TextBlock block)
            ApplyInlines(block);
        Direction(element);
    }

    private static readonly ConditionalWeakTable<ComboBox, object> Dropdowns = new();
    private static readonly List<WeakReference<ComboBox>> TextDropdowns = new();

    /// <summary>
    /// Dropdown choices live in their own popup, outside the window, so they are handled
    /// here: written ComboBoxItems are translated in place, a list of plain English words
    /// gets a template that shows them translated (the items stay English for the code),
    /// and the popup is walked each time it opens.
    /// </summary>
    private static void HookDropdown(ComboBox box)
    {
        foreach (var item in box.Items)
            if (item is FrameworkElement written and not { Tag: NoTranslate })
                foreach (var property in PropertiesOf(written))
                    Apply(written, property);

        if (Dropdowns.TryGetValue(box, out _))
            return;
        Dropdowns.Add(box, new object());
        if (box.ItemTemplate == null && box.ItemTemplateSelector == null && string.IsNullOrEmpty(box.DisplayMemberPath)
            && box.Items.Count > 0 && box.Items.Cast<object>().All(item => item is string))
        {
            box.ItemTemplate = TranslatedText();
            TextDropdowns.Add(new WeakReference<ComboBox>(box));
        }

        box.DropDownOpened += (_, _) => box.Dispatcher.BeginInvoke(() =>
        {
            foreach (var item in box.Items)
                if (box.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container)
                    SafeScan(container);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private sealed class TranslateConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string text ? Translator.T(text) : value;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private static DataTemplate TranslatedText()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new TranslateConverter() });
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !Live.Remove(element))
            return;
        element.Unloaded -= OnUnloaded;
        foreach (var property in PropertiesOf(element))
            DependencyPropertyDescriptor.FromProperty(property, element.GetType())?.RemoveValueChanged(element, OnTextChanged);
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu and not { Tag: NoTranslate })
        {
            SafeScan(menu);
            menu.FlowDirection = Translator.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            EastAsianFont(menu);
        }
    }

    private static void OnToolTipOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ToolTip tip)
            EastAsianFont(tip);
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement element and not { Tag: NoTranslate })
            Apply(element, FrameworkElement.ToolTipProperty);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (_applying > 0 || sender is not FrameworkElement element)
            return;
        foreach (var property in PropertiesOf(element))
            Apply(element, property);
        if (element is TextBlock block)
            ApplyInlines(block);
    }

    private static IEnumerable<DependencyProperty> PropertiesOf(FrameworkElement element)
    {
        switch (element)
        {
            case TextBlock:
                yield return TextBlock.TextProperty;
                break;
            case Window:
                yield return Window.TitleProperty;
                yield return ContentControl.ContentProperty;
                break;
            case HeaderedContentControl:
                yield return HeaderedContentControl.HeaderProperty;
                yield return ContentControl.ContentProperty;
                break;
            case HeaderedItemsControl:
                yield return HeaderedItemsControl.HeaderProperty;
                break;
            case ContentControl:
                yield return ContentControl.ContentProperty;
                break;
        }

        yield return FrameworkElement.ToolTipProperty;
    }

    private static void Apply(DependencyObject element, DependencyProperty property)
    {
        if (element.GetValue(property) is not string value || value.Length == 0)
            return;
        // Leave bound text alone: setting it here would break the binding. Bound text
        // shows something that is translated where it comes from (a button caption).
        var source = DependencyPropertyHelper.GetValueSource(element, property);
        // Text written into a template (a label inside a list row) counts as written text too.
        if (source.IsExpression || source.BaseValueSource is not (BaseValueSource.Local or BaseValueSource.ParentTemplate))
            return;
        if (element is TextBlock block && !IsPlain(block))
            return;

        var map = Texts.GetOrCreateValue(element);
        if (!map.TryGetValue(property, out var shown))
            map[property] = shown = new Shown();
        if (shown.Text != value || shown.English.Length == 0)
            shown.English = value;
        var text = Translator.T(shown.English);
        shown.Text = text;
        if (text == value)
            return;
        _applying++;
        try
        {
            element.SetValue(property, text);
        }
        finally
        {
            _applying--;
        }
    }

    private static bool IsPlain(TextBlock block) =>
        block.Inlines.Count == 0 || block.Inlines.Count == 1 && block.Inlines.FirstInline is Run;

    private static void ApplyInlines(TextBlock block)
    {
        if (IsPlain(block))
            return;
        foreach (var run in Runs(block.Inlines).ToList())
            Apply(run, Run.TextProperty);
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
                yield return run;
            else if (inline is Span span)
                foreach (var inner in Runs(span.Inlines))
                    yield return inner;
        }
    }

    private static void Direction(FrameworkElement element)
    {
        var rightToLeft = Translator.RightToLeft;
        switch (element)
        {
            case Window window:
                window.FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                EastAsianFont(window);
                break;
            // Pictures would be mirrored, and typed text, paths, and logs read better left to right.
            case System.Windows.Controls.Image:
            case TextBoxBase:
            case PasswordBox:
                if (rightToLeft)
                    element.FlowDirection = FlowDirection.LeftToRight;
                else
                    element.ClearValue(FrameworkElement.FlowDirectionProperty);
                break;
        }
    }

    // Segoe UI has no Chinese, Japanese or Korean letters, so WPF takes them from a fallback font. The
    // window's language tells it which one: the same character is drawn differently in Japanese and
    // Chinese, and Korean needs a Hangul font. Other languages keep the default, as before.
    private static void EastAsianFont(FrameworkElement element)
    {
        if (Translator.EastAsian)
            element.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Translator.Current);
        else
            element.ClearValue(FrameworkElement.LanguageProperty);
    }

    private static void Refresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Refresh);
            return;
        }

        // A new template makes the dropdowns build their rows again in the new language.
        foreach (var weak in TextDropdowns.ToList())
        {
            if (weak.TryGetTarget(out var box))
                box.ItemTemplate = TranslatedText();
            else
                TextDropdowns.Remove(weak);
        }

        foreach (var element in Live.ToList())
        {
            if (element is not FrameworkElement framework)
                continue;
            foreach (var property in PropertiesOf(framework))
                Apply(framework, property);
            if (framework is TextBlock block)
                ApplyInlines(block);
            if (framework is ComboBox box)
                HookDropdown(box);
            Direction(framework);
        }
    }
}
