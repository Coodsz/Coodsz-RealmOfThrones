using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace Coodsz.Ui;

/// <summary>
/// The Guides page of the TAOM server tool and the TAOM client launcher: the tool's own help pages
/// (<see cref="GuideLibrary.For"/>), a search on the left and the picked guide's steps on the right,
/// drawn in the dark TAOM look. Every guide line is shown through <see cref="Translator.T"/>, so the
/// guide parts are kept away from the window's own translation pass.
/// </summary>
public sealed class TaomGuidesPage : UserControl
{
    private static readonly Brush Ivory = Frozen(0xE8, 0xE6, 0xE1);
    private static readonly Brush Green = Frozen(0x8F, 0xBF, 0x86);
    private static readonly Brush Blue = Frozen(0x7E, 0xA4, 0xC4);
    private static readonly Brush Hint = Frozen(0x7D, 0xAA, 0xA4);
    private static readonly Brush Gold = Frozen(0xE2, 0xC2, 0x7A);
    private static readonly Brush Amber = Frozen(0xD2, 0xA3, 0x6A);
    private static readonly Brush Brown = Frozen(0x8C, 0x73, 0x55);
    private static readonly Brush Field = Frozen(0x12, 0x14, 0x16);
    private static readonly Brush FieldEdge = Frozen(0x3A, 0x3E, 0x44);
    private static readonly Brush CardEdge = Frozen(0x5C, 0x60, 0x58);
    private static readonly Brush CardBack = Frozen(0x1C, 0x20, 0x26, 0xC0);
    private static readonly Brush PickedBack = Frozen(42, 36, 30);
    private static readonly Brush PickedEdge = Frozen(138, 122, 98);
    private static readonly Brush HoverBack = Frozen(0xF6, 0xED, 0xE0, 0x33);
    private static readonly Brush NumberBack = Frozen(0x3A, 0x34, 0x20);
    private static readonly Brush TipBack = Frozen(0x1C, 0x18, 0x10, 0xC0);
    private static readonly Brush TipEdge = Frozen(0x6A, 0x5C, 0x48);
    private static readonly FontFamily Title = new("Palatino Linotype");

    private readonly IReadOnlyList<Guide> _guides;
    private readonly string _intro;
    private readonly TextBox _search;
    private readonly TextBlock _searchHint;
    private readonly StackPanel _list = new() { Tag = UiLanguage.NoTranslate };
    private readonly TextBlock _nothing;
    private readonly TextBlock _introText = new() { FontSize = 16, Foreground = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Tag = UiLanguage.NoTranslate };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _view = new() { Margin = new Thickness(26, 20, 26, 20), Tag = UiLanguage.NoTranslate };
    private Guide? _picked;

    /// <param name="guides">The guides to show (the tool's set, or the render check's own).</param>
    /// <param name="intro">The English line under the page title.</param>
    /// <param name="back">When set, a Back button is shown that runs it (the client launcher has no side menu).</param>
    public TaomGuidesPage(IReadOnlyList<Guide> guides, string intro, Action? back = null)
    {
        _guides = guides;
        _intro = intro;
        _picked = guides.FirstOrDefault();
        Foreground = Ivory;
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (back != null)
        {
            var backButton = new Button
            {
                Content = "Back",
                MinWidth = 110,
                Padding = new Thickness(14, 8, 14, 8),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ivory,
                Background = Frozen(0x16, 0x18, 0x1C, 0xCC),
                BorderBrush = Frozen(0x4A, 0x4E, 0x54),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Template = HoverTemplate(HorizontalAlignment.Center),
                ToolTip = "Closes the guides and goes back to the launcher page.",
            };
            backButton.Click += (_, _) => back();
            DockPanel.SetDock(backButton, Dock.Right);
            header.Children.Add(backButton);
        }

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Guides", FontFamily = Title, FontSize = 30, Foreground = Green });
        heading.Children.Add(_introText);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The list of guides with the search on top.
        _search = new TextBox
        {
            Background = Field,
            Foreground = Ivory,
            BorderBrush = FieldEdge,
            CaretBrush = Ivory,
            FontSize = 16,
            Padding = new Thickness(8, 6, 8, 6),
        };
        _search.TextChanged += (_, _) => Search_Changed();
        _searchHint = new TextBlock
        {
            FontSize = 16,
            Foreground = Frozen(0x9A, 0xA0, 0x9A),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Tag = UiLanguage.NoTranslate,
        };
        var searchSlot = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchSlot.Children.Add(_search);
        searchSlot.Children.Add(_searchHint);

        _nothing = new TextBlock { FontSize = 15, Foreground = Hint, Margin = new Thickness(8, 6, 8, 6), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Tag = UiLanguage.NoTranslate };
        var listStack = new StackPanel();
        listStack.Children.Add(_list);
        listStack.Children.Add(_nothing);
        var listPanel = new DockPanel();
        DockPanel.SetDock(searchSlot, Dock.Top);
        listPanel.Children.Add(searchSlot);
        listPanel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listStack });
        body.Children.Add(Card(listPanel, new Thickness(10)));

        // The picked guide.
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _view };
        var viewCard = Card(_scroll, new Thickness(0));
        Grid.SetColumn(viewCard, 2);
        body.Children.Add(viewCard);
        root.Children.Add(body);
        Content = root;

        Show();
        Translator.Changed += () => Dispatcher.BeginInvoke(Show);
    }

    /// <summary>The guide on screen (for the tests and the render check).</summary>
    public Guide? Picked => _picked;

    /// <summary>Types a search, as the box does.</summary>
    public void SearchFor(string text) => _search.Text = text;

    /// <summary>Opens one guide by id.</summary>
    public void Open(string id)
    {
        if (_guides.FirstOrDefault(g => g.Id == id) is not { } guide)
            return;
        _picked = guide;
        _search.Text = "";
        Show();
    }

    private void Search_Changed()
    {
        var shown = GuideLibrary.Search(_guides, _search.Text, Translator.T);
        // The first match opens, so a search answers straight away.
        if (shown.Count > 0 && (_picked == null || !shown.Contains(_picked)))
            _picked = shown[0];
        Show();
    }

    private void Show()
    {
        _introText.Text = Translator.T(_intro);
        _searchHint.Text = Translator.T("Search the guides...");
        _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _nothing.Text = Translator.T("No guide has that word.");

        var shown = GuideLibrary.Search(_guides, _search.Text, Translator.T);
        _list.Children.Clear();
        foreach (var guide in shown)
            _list.Children.Add(Row(guide));
        _nothing.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowGuide();
    }

    private Button Row(Guide guide)
    {
        var picked = guide == _picked;
        var text = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        text.Children.Add(new TextBlock { Text = Translator.T(guide.Title), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = picked ? Ivory : Blue, TextWrapping = TextWrapping.Wrap });
        if (guide.Summary.Length > 0)
            text.Children.Add(new TextBlock { Text = Translator.T(guide.Summary), FontSize = 13.5, Foreground = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var row = new Button
        {
            Content = text,
            Tag = UiLanguage.NoTranslate,
            Background = picked ? PickedBack : Brushes.Transparent,
            BorderBrush = picked ? PickedEdge : Brushes.Transparent,
            BorderThickness = picked ? new Thickness(3, 0, 0, 0) : new Thickness(0),
            Padding = new Thickness(12, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Template = HoverTemplate(HorizontalAlignment.Stretch),
            ToolTip = Translator.T("Shows this guide."),
        };
        row.Click += (_, _) =>
        {
            if (guide == _picked)
                return;
            _picked = guide;
            Show();
        };
        return row;
    }

    private void ShowGuide()
    {
        _view.Children.Clear();
        if (_picked == null)
            return;
        _view.Children.Add(new TextBlock { Text = Translator.T(_picked.Title), FontFamily = Title, FontSize = 26, Foreground = Green, TextWrapping = TextWrapping.Wrap });
        if (_picked.Summary.Length > 0)
            _view.Children.Add(new TextBlock { Text = Translator.T(_picked.Summary), FontSize = 15, Foreground = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
        foreach (var section in _picked.Sections)
        {
            var part = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            if (section.Heading.Length > 0)
                part.Children.Add(new TextBlock { Text = Translator.T(section.Heading), FontFamily = Title, FontSize = 20, Foreground = Blue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            for (var i = 0; i < section.Steps.Count; i++)
                part.Children.Add(Step(i + 1, Translator.T(section.Steps[i])));
            if (section.Tip.Length > 0)
                part.Children.Add(new Border
                {
                    Background = TipBack,
                    BorderBrush = TipEdge,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 2, 0, 0),
                    Child = new TextBlock { Text = Translator.T(section.Tip), FontSize = 15, Foreground = Amber, TextWrapping = TextWrapping.Wrap },
                });
            _view.Children.Add(part);
        }

        _scroll.ScrollToTop();
    }

    private static Grid Step(int number, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = NumberBack,
            BorderBrush = Gold,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Gold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var line = new TextBlock { Text = text, FontSize = 16, LineHeight = 23, Foreground = Ivory, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    private static Border Card(UIElement child, Thickness padding) => new()
    {
        Background = CardBack,
        BorderBrush = CardEdge,
        BorderThickness = new Thickness(1),
        Padding = padding,
        ClipToBounds = true,
        Child = child,
    };

    /// <summary>The tools' hover look: a light wash and a brown edge while the mouse is over it.</summary>
    private static ControlTemplate HoverTemplate(HorizontalAlignment content)
    {
        var template = new ControlTemplate(typeof(Button));
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        outer.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        outer.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var hover = new FrameworkElementFactory(typeof(Border)) { Name = "HoverEdge" };
        hover.SetValue(Border.BackgroundProperty, HoverBack);
        hover.SetValue(Border.BorderBrushProperty, Brown);
        hover.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        hover.SetValue(OpacityProperty, 0.0);
        hover.SetValue(IsHitTestVisibleProperty, false);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(MarginProperty, new TemplateBindingExtension(PaddingProperty));
        presenter.SetValue(HorizontalAlignmentProperty, content);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(ForegroundProperty));
        grid.AppendChild(hover);
        grid.AppendChild(presenter);
        outer.AppendChild(grid);
        template.VisualTree = outer;
        var trigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(OpacityProperty, 1.0, "HoverEdge"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
