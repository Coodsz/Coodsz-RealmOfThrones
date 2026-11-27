using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Modules;

namespace Coodsz.Ui;

/// <summary>
/// The Mods card of the side column (redesign D1 "Compact stats"): the mods that are on as one big number with an On
/// chip, a bar and chips for Always on, Coodsz module, Bundled and Works, each only when it has mods (ModTally.Chips).
/// Link is the window's Open Mods button and Detail the line under the number (the setup or the Active Coop line).
/// </summary>
[ContentProperty(nameof(Detail))]
public class SideModsCard : Border
{
    public const string AlwaysOnTip = "Mods this tool keeps on.";
    public const string CoodszTip = "Modules Coodsz wrote for these tools.";
    public const string BundledTip = "Modules by another author, shipped with permission.";
    public const string WorksTip = "Mods the community list says work with Coop.";
    public const string BarTip = "The grey part is mods with no community result yet.";

    private readonly ContentControl _link = SideCard.Slot();
    private readonly ContentControl _detail = SideCard.Slot();
    private readonly TextBlock _count = new() { Text = "0", Tag = UiLanguage.NoTranslate, FontFamily = new FontFamily("Palatino Linotype"), FontSize = 52, FontWeight = FontWeights.Bold, LineHeight = 52, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Grid _bar = new() { Height = 6, Margin = new Thickness(0, 12, 0, 10), ToolTip = BarTip };
    private readonly WrapPanel _chips = new();

    public SideModsCard()
    {
        SideCard.Frame(this);
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Keep.GoldInk");
        var on = new Chip { Text = "On", Kind = ChipKind.Ok, Margin = new Thickness(0, 0, 0, 6), Tip = "The mods that are on in this setup." };
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        side.Children.Add(on);
        side.Children.Add(_detail);
        var number = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_count, Dock.Left);
        number.Children.Add(_count);
        number.Children.Add(side);

        var body = new StackPanel();
        body.Children.Add(SideCard.Header(this, "Keep.IconMods", "M 12 3 l 8 4 l -8 4 l -8 -4 Z M 4 12 l 8 4 l 8 -4 M 4 16 l 8 4 l 8 -4", SideCard.Title("Mods"), _link));
        body.Children.Add(number);
        body.Children.Add(_bar);
        body.Children.Add(_chips);
        Child = body;
        Show(ModTally.Empty);
    }

    /// <summary>The window's Open Mods button, moved whole into the header.</summary>
    public object? Link
    {
        get => _link.Content;
        set => _link.Content = value;
    }

    /// <summary>The line under the On chip (the setup name, the Active Coop line).</summary>
    public object? Detail
    {
        get => _detail.Content;
        set => _detail.Content = value;
    }

    public ModTally Shown { get; private set; } = ModTally.Empty;

    public void Show(ModTally tally)
    {
        if (tally == Shown && _chips.Children.Count > 0)
            return;
        Shown = tally;
        _count.Text = tally.On.ToString();
        var looks = new Dictionary<string, (string Ink, string Fallback, string Tip)>
        {
            [ModTally.AlwaysOnChip] = ("Keep.GoldInk", "#E2C27A", AlwaysOnTip),
            [ModTally.CoodszChip] = ("Keep.OrangeInk", "#F08A4B", CoodszTip),
            [ModTally.BundledChip] = ("Keep.Accent", "#8EB4D4", BundledTip),
            [ModTally.WorksChip] = ("Keep.Success", "#8FCB9B", WorksTip),
        };
        // Only the groups with mods in them: a chip or bar part with 0 ("Works 0" on plain TAOM) says nothing.
        var groups = tally.Chips().Select(chip => (chip.Word, chip.Count, looks[chip.Word].Ink, looks[chip.Word].Fallback, looks[chip.Word].Tip)).ToList();

        _bar.ColumnDefinitions.Clear();
        _bar.Children.Clear();
        var parts = groups.Select(group => (Count: group.Count, Ink: group.Ink, Fallback: group.Fallback)).Append((Count: tally.Other, Ink: "Keep.BorderDim", Fallback: "#3A4C5C")).Where(part => part.Count > 0).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].Count, GridUnitType.Star) });
            var piece = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, i == parts.Count - 1 ? 0 : 3, 0), Background = FrameBrushes.Get(this, parts[i].Ink, parts[i].Fallback) };
            Grid.SetColumn(piece, i);
            _bar.Children.Add(piece);
        }

        _bar.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _chips.Children.Clear();
        foreach (var group in groups)
            _chips.Children.Add(CountChip(group.Word, group.Count, FrameBrushes.Get(this, group.Ink, group.Fallback), group.Tip));
    }

    private static Border CountChip(string word, int count, Brush ink, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = word, Foreground = ink, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = count.ToString(), Tag = UiLanguage.NoTranslate, Foreground = ink, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new Border { Child = row, BorderBrush = ink, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 6), ToolTip = tip };
    }
}
