using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Coodsz.Ui;

/// <summary>
/// The Join my server card (redesign D2): a gold line on top (Keep.ToolEdge when the colour file has one), the castle icon and the title, an inset block with the
/// window's Realm lines (Body), and under it the window's buttons (Actions: the solid gold Join on Discord, Learn more,
/// and the clients' starter reward and Copy invite). The handlers and x:Names stay in each window's markup.
/// </summary>
[ContentProperty(nameof(Body))]
public class RealmCard : Border
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(RealmCard), new PropertyMetadata("Join my server", (d, e) => ((RealmCard)d)._title.Text = (string)e.NewValue));

    private readonly TextBlock _title = SideCard.Title("Join my server");
    private readonly ContentControl _body = SideCard.Slot();
    private readonly ContentControl _actions = SideCard.Slot();

    public RealmCard()
    {
        SideCard.Frame(this);
        Padding = new Thickness(0);
        var top = new Border { Height = 3, CornerRadius = new CornerRadius(9, 9, 0, 0) };
        top.SetResourceReference(Border.BackgroundProperty, ToolAccent.OwnEdge ? ToolAccent.EdgeKey : "Keep.GoldInk");
        var inset = new Border { Child = _body, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        inset.SetResourceReference(Border.BackgroundProperty, "Keep.BgDeep");
        inset.SetResourceReference(Border.BorderBrushProperty, "Keep.Line");

        var inner = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        inner.Children.Add(SideCard.Header(this, "Keep.IconRealm", "M 3 21 V 9 l 3 -2 V 4 l 3 2 l 3 -2 l 3 2 l 3 -2 v 3 l 3 2 v 12 Z M 10 21 v -5 h 4 v 5", _title, null));
        inner.Children.Add(inset);
        inner.Children.Add(_actions);
        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(inner);
        Child = body;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The Realm lines in the inset block.</summary>
    public object? Body
    {
        get => _body.Content;
        set => _body.Content = value;
    }

    /// <summary>The buttons under the inset block.</summary>
    public object? Actions
    {
        get => _actions.Content;
        set => _actions.Content = value;
    }
}
