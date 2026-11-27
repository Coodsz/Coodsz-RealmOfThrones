using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace Coodsz.Ui;

/// <summary>
/// Every Coop command from bannerlordcoop.com/cheats with one box per parameter, the line they make
/// and the How to use guide. The tool that shows it says how a command is sent (<see cref="Send"/>),
/// how it goes into the command box (<see cref="PutInBox"/>) and which player the examples use.
/// Shared by Coodsz Host, the AD1259 server tool and the TAOM server tool.
/// </summary>
public partial class CoopCommandBrowser : UserControl
{
    private readonly List<TextBox> _paramBoxes = new();
    private string _category = "";
    private string _side = "";
    private CoopCommandInfo? _command;

    /// <summary>A category of the command list: its name and how many commands it has.</summary>
    public sealed record CategoryItem(string Key, string Name, int Count);

    public CoopCommandBrowser()
    {
        InitializeComponent();
        var commands = CoopCommandCatalog.All;
        var items = new List<CategoryItem> { new("", Translator.T("All"), commands.Count) };
        items.AddRange(CoopCommandCatalog.Categories(commands)
            .Select(category => new CategoryItem(category, CoopCommandCatalog.Pretty(category), commands.Count(c => c.Category == category))));
        ConsoleCategories.ItemsSource = items;
        ConsoleCategories.SelectedIndex = 0;
        ConsoleSearch.Tag = Translator.T("Search commands...");
        Filter();
    }

    /// <summary>Sends a finished command line (the tool checks it, sends it and shows the reply).</summary>
    public Func<string, Task>? Send { get; set; }

    /// <summary>Puts a command line in the tool's command box so it can be changed before sending.</summary>
    public Action<string>? PutInBox { get; set; }

    /// <summary>The player the examples of the guide use: (name, SteamID64), either can be null.</summary>
    public Func<(string? Name, string? SteamId)>? GuidePlayer { get; set; }

    private void ConsoleCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        _category = (ConsoleCategories.SelectedItem as CategoryItem)?.Key ?? "";
        if (ConsoleCommands != null)
            Filter();
    }

    /// <summary>The picked command's form sits right of the list, or under it when the window is narrow.</summary>
    private void ConsoleBrowser_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 760;
        ConsoleDetailColumn.Width = narrow ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        ConsoleDetailRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(ConsoleDetailHost, narrow ? 1 : 2);
        Grid.SetRow(ConsoleDetailHost, narrow ? 1 : 0);
        ConsoleDetailHost.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(14, 0, 0, 0);
        ConsoleDetailHost.MaxHeight = narrow ? 260 : double.PositiveInfinity;
    }

    private void ConsoleSide_Checked(object sender, RoutedEventArgs e)
    {
        _side = (sender as FrameworkElement)?.Tag as string ?? "";
        if (ConsoleCommands != null)
            Filter();
    }

    private void ConsoleSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (ConsoleCommands != null)
            Filter();
    }

    private void Filter()
    {
        var list = CoopCommandCatalog.Search(CoopCommandCatalog.All, ConsoleSearch.Text, _category.Length == 0 ? null : _category)
            .Where(c => _side.Length == 0 || c.Side == _side)
            .ToList();
        var picked = _command;
        ConsoleCommands.ItemsSource = list;
        if (picked != null && list.Contains(picked))
            ConsoleCommands.SelectedItem = picked;
        ConsoleCount.Text = list.Count + " " + Translator.T("of") + " " + CoopCommandCatalog.All.Count + " " + Translator.T("commands");
    }

    private void ConsoleCommand_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ConsoleCommands.SelectedItem is not CoopCommandInfo command || ReferenceEquals(command, _command))
            return;
        _command = command;
        ShowCommand();
    }

    /// <summary>The picked command: what it does, its usage, one box per parameter and the line they make.</summary>
    private void ShowCommand()
    {
        ConsoleParams.Children.Clear();
        _paramBoxes.Clear();
        var command = _command;
        ConsolePickHint.Visibility = command == null ? Visibility.Visible : Visibility.Collapsed;
        ConsoleDetail.Visibility = command == null ? Visibility.Collapsed : Visibility.Visible;
        if (command == null)
            return;

        ConsoleDetailName.Text = command.Name;
        ConsoleDetailInfo.Text = command.Description + "  ·  " + command.CategoryName + "  ·  " + command.SideName + "  ·  " + CoopCommandCatalog.Pretty(command.Type);
        ConsoleDetailUsage.Text = command.Usage;
        ConsoleClientText.Text = Translator.T(CoopCommandCatalog.ClientOnlyNote);
        ConsoleClientNote.Visibility = command.IsClientOnly ? Visibility.Visible : Visibility.Collapsed;
        ConsoleRunButton.IsEnabled = !command.IsClientOnly;
        foreach (var parameter in command.Parameters)
        {
            ConsoleParams.Children.Add(new TextBlock
            {
                Text = parameter.Optional ? parameter.Name + "  (" + Translator.T("optional") + ")" : parameter.Name,
                Style = (Style)FindResource("Keep.Label"),
                Margin = new Thickness(0, 0, 0, 4),
            });
            var box = new TextBox
            {
                Style = (Style)FindResource("Keep.Text"),
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = parameter.Name,
            };
            box.TextChanged += (_, _) => ShowBuilt();
            box.KeyDown += (_, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    ConsoleRun_Click(box, args);
                    args.Handled = true;
                }
            };
            ConsoleParams.Children.Add(box);
            _paramBoxes.Add(box);
        }

        ShowBuilt();
        ShowGuide(command);
    }

    /// <summary>
    /// The "How to use" guide under the form: what the command does, each value with an example and
    /// where to find it, an example line with "Use example", and "Find ids" for the list commands.
    /// </summary>
    private void ShowGuide(CoopCommandInfo command)
    {
        ConsoleGuide.Children.Clear();
        var (playerName, steamId) = GuidePlayer?.Invoke() ?? (null, null);
        var guide = CoopCommandHelp.Guide(command, playerName, null, string.IsNullOrEmpty(steamId) ? null : steamId);
        var text = (Brush)FindResource("Keep.TextMain");
        var dim = (Brush)FindResource("Keep.TextDim");
        var gold = (Brush)FindResource("Keep.Warning");

        TextBlock Line(string value, Brush brush, double size = 13.5, bool bold = false, double top = 0) => new()
        {
            Text = value,
            Foreground = brush,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, 0),
        };

        ConsoleGuide.Children.Add(Line(Translator.T("How to use"), text, 16, true));
        ConsoleGuide.Children.Add(Line(guide.Plain, text, 13.5, false, 6));
        foreach (var (parameter, help) in guide.Parameters)
        {
            ConsoleGuide.Children.Add(Line(parameter.Optional ? parameter.Name + "  (" + Translator.T("optional") + ")" : parameter.Name, gold, 13.5, true, 12));
            ConsoleGuide.Children.Add(Line(help.What, text, 13.5, false, 2));
            ConsoleGuide.Children.Add(Line(Translator.T("Example") + "  " + help.Example, dim, 13, false, 2));
            ConsoleGuide.Children.Add(Line(help.Where, dim, 13, false, 2));
        }

        ConsoleGuide.Children.Add(Line(Translator.T("Example"), text, 14, true, 14));
        ConsoleGuide.Children.Add(new TextBox
        {
            Text = guide.ExampleLine,
            Style = (Style)FindResource("Keep.Text"),
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = guide.ExampleLine,
        });
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (guide.Parameters.Count > 0)
        {
            var use = new Button
            {
                Content = Translator.T("Use example"),
                Style = (Style)FindResource("Keep.Button"),
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = Translator.T("Fills the boxes above with the example values."),
            };
            use.Click += (_, _) =>
            {
                for (var i = 0; i < _paramBoxes.Count && i < guide.ExampleValues.Count; i++)
                    _paramBoxes[i].Text = guide.ExampleValues[i];
            };
            buttons.Children.Add(use);
        }

        foreach (var list in guide.FindCommands)
        {
            var name = list;
            var find = new Button
            {
                Content = Translator.T("Find ids") + "  " + name,
                Style = (Style)FindResource("Keep.Button"),
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = name,
            };
            find.Click += async (_, _) =>
            {
                ConsoleDetailProblem.Text = Translator.T("The list shows in the output above. Copy the id you need from there into the box.");
                if (Send != null)
                    await Send(name);
            };
            buttons.Children.Add(find);
        }

        if (buttons.Children.Count > 0)
            ConsoleGuide.Children.Add(buttons);
        if (guide.FindCommands.Count > 0)
            ConsoleGuide.Children.Add(Line(Translator.T("Find ids runs the list command. The list shows in the output above. Copy the id you need from there into the box."), dim, 13, false, 2));
    }

    private IReadOnlyList<string?> Values() => _paramBoxes.Select(b => (string?)b.Text).ToList();

    private void ShowBuilt()
    {
        if (_command is not { } command)
            return;
        ConsoleBuilt.Text = CoopCommandCatalog.Build(command, Values());
        ConsoleDetailProblem.Text = "";
    }

    private async void ConsoleRun_Click(object sender, RoutedEventArgs e)
    {
        if (_command is not { } command)
            return;
        if (command.IsClientOnly)
        {
            ConsoleDetailProblem.Text = Translator.T(CoopCommandCatalog.ClientOnlyNote);
            return;
        }

        if (CoopCommandCatalog.Problem(command, Values()) is { } problem)
        {
            ConsoleDetailProblem.Text = Translator.T(problem);
            return;
        }

        ConsoleDetailProblem.Text = "";
        if (Send != null)
            await Send(CoopCommandCatalog.Build(command, Values()));
    }

    private void ConsolePutInBox_Click(object sender, RoutedEventArgs e)
    {
        if (_command is { } command)
            PutInBox?.Invoke(CoopCommandCatalog.Build(command, Values()));
    }
}
