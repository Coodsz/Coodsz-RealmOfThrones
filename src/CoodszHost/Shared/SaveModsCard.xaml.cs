using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BannerlordCoopLauncher.Core.Modules;

namespace Coodsz.Ui;

/// <summary>
/// Shows a <see cref="SaveModCard"/>. It has no logic of its own; a button raises <see cref="Command"/>
/// with its tag (fix, othersave, anyway, dismiss) and the window runs its own setup and save code.
/// </summary>
public partial class SaveModsCard : UserControl
{
    private SaveModCard? _card;
    private bool _expanded;

    public SaveModsCard()
    {
        InitializeComponent();
        DismissButton.ToolTip = SaveModCard.DismissTip;
        OtherSaveButton.ToolTip = SaveModCard.OtherSaveTip;
        AnywayButton.ToolTip = SaveModCard.StartAnywayTip;
    }

    /// <summary>A button was pressed: its tag.</summary>
    public event Action<string>? Command;

    /// <summary>The TAOM box keeps its Start anyway choice; Coodsz Host never starts a save like that.</summary>
    public bool ShowStartAnyway
    {
        get => AnywayButton.Visibility == Visibility.Visible;
        set => AnywayButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowDismiss
    {
        get => DismissButton.Visibility == Visibility.Visible;
        set => DismissButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowOtherSave
    {
        get => OtherSaveButton.Visibility == Visibility.Visible;
        set => OtherSaveButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public SaveModCard? Card => _card;

    /// <summary>Shows the card, or hides it for null. The same mods keep the +N more choice.</summary>
    public void Show(SaveModCard? card)
    {
        if (card == null)
        {
            _card = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        if (_card == null || _card.SaveName != card.SaveName || !_card.Mods.SequenceEqual(card.Mods))
            _expanded = false;
        _card = card;
        TitleText.Text = card.Title;
        WhyText.Text = card.Why;
        NoteText.Text = card.Note;
        NoteText.Visibility = card.Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FixButton.Content = card.FixButton;
        FixButton.ToolTip = card.FixTip;
        FixButton.Visibility = card.Fix == SaveModFix.None ? Visibility.Collapsed : Visibility.Visible;
        FillChips();
        Visibility = Visibility.Visible;
    }

    private void FillChips()
    {
        ChipPanel.Children.Clear();
        if (_card == null)
            return;
        foreach (var chip in _card.Shown(_expanded))
            ChipPanel.Children.Add(Chip(chip));
        if (!_card.HasMore)
            return;
        var more = new Button
        {
            Content = _card.MoreButton(_expanded),
            ToolTip = _card.MoreTip(_expanded),
            Style = (Style)FindResource("Link"),
            Foreground = (Brush)FindResource("Keep.Accent"),
            FontSize = 12,
            Margin = new Thickness(4, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        more.Click += (_, _) =>
        {
            _expanded = !_expanded;
            FillChips();
        };
        ChipPanel.Children.Add(more);
    }

    private FrameworkElement Chip(SaveModChip chip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (!chip.Installed)
            row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = (Brush)FindResource("Keep.Red"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        // The mod's own name stays as written in every language.
        row.Children.Add(new TextBlock { Text = chip.Name, FontSize = 12, Foreground = (Brush)FindResource("Keep.TextMain"), Tag = UiLanguage.NoTranslate });
        var border = new Border
        {
            Background = (Brush)FindResource("Keep.BgRaised"),
            BorderBrush = (Brush)FindResource(chip.Installed ? "Keep.BorderDim" : "Keep.Red"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 6),
            Child = row
        };
        if (chip.Tip.Length > 0)
            border.ToolTip = chip.Tip;
        return border;
    }

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
            Command?.Invoke(tag);
    }
}
