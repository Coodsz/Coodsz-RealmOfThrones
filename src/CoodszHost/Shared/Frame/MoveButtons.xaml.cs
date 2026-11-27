using System.Windows;
using System.Windows.Automation;
using BannerlordCoopLauncher.Core.Localization;

namespace Coodsz.Ui;

/// <summary>
/// The Move up and Move down buttons of a mod row. ItemName goes into what a screen reader says ("Move Harmony up");
/// CanMoveUp and CanMoveDown turn a button off at the ends of the list or for a row that can't move.
/// </summary>
public partial class MoveButtons
{
    public static readonly DependencyProperty ItemNameProperty = DependencyProperty.Register(
        nameof(ItemName), typeof(string), typeof(MoveButtons), new PropertyMetadata("", (d, _) => ((MoveButtons)d).SayNames()));

    public static readonly DependencyProperty CanMoveUpProperty = DependencyProperty.Register(
        nameof(CanMoveUp), typeof(bool), typeof(MoveButtons), new PropertyMetadata(true, (d, e) => ((MoveButtons)d).UpButton.IsEnabled = (bool)e.NewValue));

    public static readonly DependencyProperty CanMoveDownProperty = DependencyProperty.Register(
        nameof(CanMoveDown), typeof(bool), typeof(MoveButtons), new PropertyMetadata(true, (d, e) => ((MoveButtons)d).DownButton.IsEnabled = (bool)e.NewValue));

    public event RoutedEventHandler? MoveUp;
    public event RoutedEventHandler? MoveDown;

    public MoveButtons()
    {
        InitializeComponent();
        SayNames();
        // Only while shown: mod rows come and go, and a row must not stay alive through the language event.
        Loaded += (_, _) => Translator.Changed += LanguageChanged;
        Unloaded += (_, _) => Translator.Changed -= LanguageChanged;
    }

    private void LanguageChanged() => Dispatcher.BeginInvoke(SayNames);

    public string ItemName { get => (string)GetValue(ItemNameProperty); set => SetValue(ItemNameProperty, value); }
    public bool CanMoveUp { get => (bool)GetValue(CanMoveUpProperty); set => SetValue(CanMoveUpProperty, value); }
    public bool CanMoveDown { get => (bool)GetValue(CanMoveDownProperty); set => SetValue(CanMoveDownProperty, value); }

    private void SayNames()
    {
        // A row with no name yet still says what the button does.
        var unnamed = string.IsNullOrWhiteSpace(ItemName);
        AutomationProperties.SetName(UpButton, unnamed ? Translator.T("Move this mod up") : Translator.T($"Move {ItemName} up"));
        AutomationProperties.SetName(DownButton, unnamed ? Translator.T("Move this mod down") : Translator.T($"Move {ItemName} down"));
    }

    private void Up_Click(object sender, RoutedEventArgs e) => MoveUp?.Invoke(this, e);

    private void Down_Click(object sender, RoutedEventArgs e) => MoveDown?.Invoke(this, e);
}
