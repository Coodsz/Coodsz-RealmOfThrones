using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace RealmOfThronesHost;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string body, string? details = null, string? okText = null)
    {
        InitializeComponent();
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        Title = title;
        TitleBlock.Text = title;
        BodyBlock.Text = body;
        if (!string.IsNullOrEmpty(details))
        {
            DetailsBox.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
            Frame.Width = 640;
        }

        if (!string.IsNullOrEmpty(okText))
            OkButton.Content = okText;
    }

    public static bool Ask(Window owner, string title, string body, string? details = null, string? okText = null)
    {
        var window = new ConfirmWindow(title, body, details, okText) { Owner = owner };
        return window.ShowDialog() == true;
    }

    /// <summary>Shows text with one button, for a note that does not start anything.</summary>
    public static void Show(Window owner, string title, string body, string okText = "Got it")
    {
        var window = new ConfirmWindow(title, body, null, okText) { Owner = owner };
        window.CancelButton.Visibility = Visibility.Collapsed;
        window.ShowDialog();
    }

    /// <summary>A question with a tidy list of (name, id) rows, a small note and a warning line.</summary>
    public static bool AskList(Window owner, string title, string question, IEnumerable<(string Name, string Id)> items,
        string note, string warning, string okText, string okTip)
    {
        var window = new ConfirmWindow(title, question, null, okText) { Owner = owner };
        window.FillList(items, note, warning, okTip);
        return window.ShowDialog() == true;
    }

    internal void FillList(IEnumerable<(string Name, string Id)> items, string note, string warning, string okTip)
    {
        foreach (var (name, id) in items)
        {
            // The friendly name, with the module id small beside it when that is different.
            var row = new TextBlock { FontSize = 15, Foreground = BodyBlock.Foreground, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
            row.Inlines.Add(new Run("\u2022  " + name));
            if (!string.Equals(name, id, StringComparison.OrdinalIgnoreCase))
                row.Inlines.Add(new Run("   " + id) { FontSize = 12, Foreground = NoteBlock.Foreground });
            ListItems.Children.Add(row);
        }

        NoteBlock.Text = note;
        WarnBlock.Text = warning;
        ListPanel.Visibility = Visibility.Visible;
        OkButton.ToolTip = okTip;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
