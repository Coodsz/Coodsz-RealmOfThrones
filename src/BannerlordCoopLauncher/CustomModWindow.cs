using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Profile;

namespace BannerlordCoopLauncher;

public sealed class CustomModWindow : Window
{
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _note = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _searchMiss = new();
    private readonly Button _add = new();
    private readonly Button _refresh = new();
    private readonly Func<IReadOnlyList<FoundMod>> _scan;
    private readonly Func<FoundMod, ModBadge?>? _badge;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FoundMod> _picked = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshPulse = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private List<FoundMod> _mods = new();
    private HashSet<string> _fresh = new(StringComparer.OrdinalIgnoreCase);
    private bool _filling;

    private CustomModWindow(Func<IReadOnlyList<FoundMod>> scan, Func<FoundMod, ModBadge?>? badge = null)
    {
        _scan = scan;
        _badge = badge;
        Title = "Add mods";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        Background = Coodsz.Ui.ToolPalette.Brush(14, 20, 24);
        Foreground = Coodsz.Ui.ToolPalette.Brush(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");

        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 0, 0, 8);
        _note.Foreground = Coodsz.Ui.ToolPalette.Brush(142, 160, 174);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 12);
        _status.FontWeight = FontWeights.SemiBold;
        _status.Foreground = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        _status.Visibility = Visibility.Collapsed;
        _searchMiss.Text = "No mods match that search.";
        _searchMiss.Margin = new Thickness(0, 0, 0, 8);
        _searchMiss.FontWeight = FontWeights.SemiBold;
        _searchMiss.Foreground = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        _searchMiss.Visibility = Visibility.Collapsed;
        _search.MinHeight = 36;
        _search.FontSize = 15;
        _search.Padding = new Thickness(10, 6, 10, 6);
        _search.Background = Coodsz.Ui.ToolPalette.Brush(16, 22, 27);
        _search.Foreground = Coodsz.Ui.ToolPalette.Brush(213, 222, 230);
        _search.BorderBrush = Coodsz.Ui.ToolPalette.Brush(142, 180, 212);
        _search.CaretBrush = Coodsz.Ui.ToolPalette.Brush(213, 222, 230);
        _search.ToolTip = "Type a mod name. The list below shows matches.";
        AutomationProperties.SetAutomationId(_search, "AddModsSearch");
        var searchHint = new TextBlock
        {
            Text = "Search mods",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Coodsz.Ui.ToolPalette.Brush(142, 160, 174),
            IsHitTestVisible = false
        };
        _search.TextChanged += (_, _) =>
        {
            searchHint.Visibility = string.IsNullOrEmpty(_search.Text) ? Visibility.Visible : Visibility.Collapsed;
            ShowList();
        };
        var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchBox.Children.Add(_search);
        searchBox.Children.Add(searchHint);
        _list.SelectionMode = SelectionMode.Extended;
        _list.Background = Coodsz.Ui.ToolPalette.Brush(16, 22, 27);
        _list.BorderBrush = Coodsz.Ui.ToolPalette.Brush(42, 56, 68);
        _list.Foreground = Coodsz.Ui.ToolPalette.Brush(213, 222, 230);
        var selectedYellow = Coodsz.Ui.ToolPalette.Brush(232, 214, 156);
        var selectedText = Coodsz.Ui.ToolPalette.Brush(26, 22, 12);
        _list.Resources[SystemColors.HighlightBrushKey] = selectedYellow;
        _list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selectedYellow;
        _list.Resources[SystemColors.HighlightTextBrushKey] = selectedText;
        _list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = selectedText;

        _add.Content = "Add selected";
        _add.Padding = new Thickness(18, 8, 18, 8);
        _add.Margin = new Thickness(0, 12, 8, 0);
        _add.Background = Coodsz.Ui.ToolPalette.Brush(31, 78, 121);
        _add.Foreground = Coodsz.Ui.ToolPalette.Brush(213, 222, 230);
        _add.BorderBrush = Coodsz.Ui.ToolPalette.Brush(142, 180, 212);
        _add.Click += (_, _) =>
        {
            // Nothing picked used to close the window as if mods were added.
            if (_list.SelectedItems.Count == 0)
            {
                _status.Text = "Pick at least one mod, then click Add selected.";
                _status.Visibility = Visibility.Visible;
                return;
            }
            if (SelectedOverhauls().Count > 0 &&
                Coodsz.Ui.UiLanguage.Message(
                    "Total overhaul mods will not work at all. They have a very high chance of breaking your server and your game.\n\nAdd them anyway?",
                    "Total overhaul mods",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    this) != MessageBoxResult.Yes)
                return;
            DialogResult = true;
            Close();
        };
        _refresh.Content = "Refresh";
        _refresh.Padding = new Thickness(18, 8, 18, 8);
        _refresh.Margin = new Thickness(0, 12, 8, 0);
        _refresh.Background = Coodsz.Ui.ToolPalette.Brush(58, 52, 32);
        _refresh.Foreground = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        _refresh.BorderBrush = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        _refresh.BorderThickness = new Thickness(2);
        _refresh.FontWeight = FontWeights.SemiBold;
        _refresh.Click += (_, _) => LoadMods(fromRefresh: true);
        _refreshPulse.Tick += (_, _) =>
        {
            _refreshPulse.Stop();
            _refresh.Content = "Refresh";
            _refresh.Background = Coodsz.Ui.ToolPalette.Brush(58, 52, 32);
            _refresh.Foreground = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        };
        var close = new Button
        {
            Content = "Close",
            ToolTip = "Closes this window.",
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 12, 0, 0),
            Background = Coodsz.Ui.ToolPalette.Brush(27, 40, 51),
            Foreground = Coodsz.Ui.ToolPalette.Brush(213, 222, 230),
            BorderBrush = Coodsz.Ui.ToolPalette.Brush(58, 76, 92)
        };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_refresh);
        buttons.Children.Add(_add);
        buttons.Children.Add(close);

        var stack = new DockPanel { Margin = new Thickness(18) };
        var warning = new TextBlock
        {
            Text = "Total overhaul mods will not work at all. They have a very high chance of breaking your server and your game.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = FontWeights.SemiBold,
            Foreground = Coodsz.Ui.ToolPalette.Brush(232, 176, 176)
        };
        var header = new StackPanel();
        header.Children.Add(_note);
        header.Children.Add(warning);
        header.Children.Add(_status);
        header.Children.Add(new TextBlock
        {
            Text = "Search",
            Foreground = Coodsz.Ui.ToolPalette.Brush(142, 160, 174),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });
        header.Children.Add(searchBox);
        header.Children.Add(_searchMiss);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        stack.Children.Add(header);
        stack.Children.Add(buttons);
        stack.Children.Add(_list);
        Content = stack;
        LoadMods(fromRefresh: false);
    }

    private void LoadMods(bool fromRefresh)
    {
        var mods = _scan();
        var fresh = mods.Where(mod => !_seen.Contains(mod.ModuleId)).Select(mod => mod.ModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _mods = mods.ToList();
        _seen.Clear();
        foreach (var mod in _mods)
            _seen.Add(mod.ModuleId);
        if (fromRefresh)
            _fresh = fresh;
        _add.IsEnabled = _mods.Count > 0;
        _note.Text = _mods.Count == 0
            ? "No extra mods were found. This checks Steam Workshop and the Modules folder in your Bannerlord install. Click Refresh after a download. If a mod is not listed in Steam Workshop and is not already in that Modules folder, unzip it, then drag the folder next to this launcher. Drag the folder that has SubModule.xml in it, then click Refresh."
            : "Select one or more. Workshop mods are labeled Workshop. Mods already in your Bannerlord Modules folder, or a folder next to this launcher, are labeled From Nexus or ModDB. Click Refresh after a new download. Search finds a mod by name.";
        var firstNew = ShowList();

        if (!fromRefresh)
        {
            _status.Visibility = Visibility.Collapsed;
            return;
        }

        _status.Visibility = Visibility.Visible;
        _status.Text = fresh.Count == 0
            ? "Refreshed. No new mods."
            : fresh.Count == 1
                ? "Refreshed. 1 new mod was found. It is highlighted."
                : "Refreshed. " + fresh.Count + " new mods were found. They are highlighted.";
        if (firstNew != null)
            _list.ScrollIntoView(firstNew);
        _refresh.Content = "Checked";
        _refresh.Background = Coodsz.Ui.ToolPalette.Brush(226, 194, 122);
        _refresh.Foreground = Coodsz.Ui.ToolPalette.Brush(26, 22, 12);
        _refreshPulse.Stop();
        _refreshPulse.Start();
    }

    private ListBoxItem? ShowList()
    {
        var mods = CustomModCatalog.Matching(_mods, _search.Text);
        _searchMiss.Visibility = _mods.Count > 0 && mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _filling = true;
        _list.Items.Clear();
        ListBoxItem? firstNew = null;
        foreach (var mod in mods)
        {
            var isNew = _fresh.Contains(mod.ModuleId);
            var overhaul = IsTotalOverhaul(mod.ModuleId, mod.Name);
            var background = Coodsz.Ui.ToolPalette.Of(overhaul ? Color.FromRgb(62, 34, 38) : isNew ? Color.FromRgb(58, 52, 32) : Color.FromRgb(16, 22, 27));
            var accent = Coodsz.Ui.ToolPalette.Of(overhaul ? Color.FromRgb(196, 132, 132) : isNew ? Color.FromRgb(226, 194, 122) : Color.FromRgb(16, 22, 27));
            var text = Coodsz.Ui.ToolPalette.Of(overhaul ? Color.FromRgb(232, 176, 176) : isNew ? Color.FromRgb(226, 194, 122) : Color.FromRgb(213, 222, 230));
            var label = new TextBlock
            {
                Text = mod.Name + "  —  " + mod.Source + (overhaul ? "  —  total overhaul, will not work" : "") + (isNew ? "  —  new" : ""),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(text),
                FontWeight = overhaul || isNew ? FontWeights.SemiBold : FontWeights.Normal
            };
            var row = new Border
            {
                Background = new SolidColorBrush(background),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(overhaul || isNew ? 2 : 0),
                Padding = new Thickness(8, 6, 8, 6),
                Child = WithBadge(label, mod)
            };
            var item = new ListBoxItem
            {
                Content = row,
                Tag = mod,
                Padding = new Thickness(0),
                FocusVisualStyle = null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            item.Selected += (_, _) =>
            {
                if (!_filling)
                    _picked[mod.ModuleId] = mod;
                PaintSelected(row, label, true, background, accent, text, overhaul || isNew);
            };
            item.Unselected += (_, _) =>
            {
                if (!_filling)
                    _picked.Remove(mod.ModuleId);
                PaintSelected(row, label, false, background, accent, text, overhaul || isNew);
            };
            _list.Items.Add(item);
            item.IsSelected = _picked.ContainsKey(mod.ModuleId);
            if (isNew && firstNew == null)
                firstNew = item;
        }

        _filling = false;
        return firstNew;
    }

    private static void PaintSelected(Border row, TextBlock label, bool selected, Color background, Color accent, Color text, bool marked)
    {
        row.Background = new SolidColorBrush(Coodsz.Ui.ToolPalette.Of(selected ? Color.FromRgb(232, 214, 156) : background));
        row.BorderBrush = new SolidColorBrush(Coodsz.Ui.ToolPalette.Of(selected ? Color.FromRgb(212, 184, 96) : accent));
        row.BorderThickness = new Thickness(selected || marked ? 2 : 0);
        label.Foreground = new SolidColorBrush(Coodsz.Ui.ToolPalette.Of(selected ? Color.FromRgb(26, 22, 12) : text));
        label.FontWeight = selected || marked ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private List<FoundMod> SelectedOverhauls() =>
        _picked.Values.Where(mod => IsTotalOverhaul(mod.ModuleId, mod.Name)).ToList();

    // Only the modules TAOM installs; TAOM community mods such as TAOM_PlayerVoices are not overhauls.
    private static bool IsTotalOverhaul(string moduleId, string name) =>
        BannerlordCoopLauncher.Core.Modules.TotalOverhaul.Is(moduleId);

    public static IReadOnlyList<FoundMod> Choose(Window owner, Func<IReadOnlyList<FoundMod>> scan) => Choose(owner, scan, null);

    // badge: an optional green, yellow or red mark per mod (the TAOM tools use it for their
    // compatibility list). Without it the window looks as it always did.
    public static IReadOnlyList<FoundMod> Choose(Window owner, Func<IReadOnlyList<FoundMod>> scan, Func<FoundMod, ModBadge?>? badge)
    {
        var window = new CustomModWindow(scan, badge) { Owner = owner };
        if (window.ShowDialog() != true)
            return Array.Empty<FoundMod>();
        return CustomModCatalog.Matching(window._picked.Values, "").ToList();
    }

    private UIElement WithBadge(TextBlock label, FoundMod mod)
    {
        var badge = _badge?.Invoke(mod);
        if (badge == null)
            return label;
        var mark = new Border
        {
            Background = new SolidColorBrush(badge.Color),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = string.IsNullOrWhiteSpace(badge.Note) ? null : badge.Note,
            Child = new TextBlock { Text = badge.Text, Foreground = Coodsz.Ui.ToolPalette.Brush(16, 18, 20), FontWeight = FontWeights.SemiBold, FontSize = 12 }
        };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(mark, Dock.Right);
        panel.Children.Add(mark);
        if (badge.Mine != null)
        {
            var mine = ModPills.Mine(badge.Mine, new Thickness(8, 0, 0, 0));
            DockPanel.SetDock(mine, Dock.Right);
            panel.Children.Add(mine);
        }

        panel.Children.Add(label);
        if (!string.IsNullOrWhiteSpace(badge.Note))
            panel.ToolTip = badge.Note;
        return panel;
    }
}

/// <summary>A small colored mark with a short text and a note shown on hover, and this PC's own
/// result for the mod as a second pill when there is one.</summary>
public sealed record ModBadge(string Text, Color Color, string Note, BannerlordCoopLauncher.Core.Modules.ModResultPill? Mine = null);

/// <summary>The badges the Add mods list and the TAOM mod rows draw the same way.</summary>
public static class ModPills
{
    /// <summary>The neutral grey of "Unknown, not tested".</summary>
    public static readonly Color UnknownGrey = Coodsz.Ui.ToolPalette.Of(Color.FromRgb(0x9A, 0xA6, 0xB0));

    public static ModBadge Unknown(string tip, BannerlordCoopLauncher.Core.Modules.ModResultPill? mine) =>
        new(BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Text, UnknownGrey, tip, mine);

    /// <summary>Worked for you or Passed on your server in green, Crashed or Failed in red; an older
    /// version's result is dimmed. One line, smaller than the list badge.</summary>
    public static Border Mine(BannerlordCoopLauncher.Core.Modules.ModResultPill pill, Thickness margin)
    {
        var ink = Coodsz.Ui.ToolPalette.Of(pill.Good ? Color.FromRgb(0x8F, 0xCB, 0x9B) : Color.FromRgb(0xE0, 0x8B, 0x84));
        return new Border
        {
            Background = new SolidColorBrush(Coodsz.Ui.ToolPalette.Of(pill.Good ? Color.FromRgb(0x1E, 0x33, 0x26) : Color.FromRgb(0x3A, 0x24, 0x22))),
            BorderBrush = new SolidColorBrush(ink),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = margin,
            Opacity = pill.Older ? 0.5 : 1,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = pill.Hover,
            Child = new TextBlock { Text = pill.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(ink) }
        };
    }

    /// <summary>The version in a mod folder's SubModule.xml, or empty.</summary>
    public static string VersionIn(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        try
        {
            return System.IO.Directory.Exists(directory) ? BannerlordCoopLauncher.Core.Parsing.ModuleManifestReader.Read(directory)?.Version ?? "" : "";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "";
        }
    }
}
