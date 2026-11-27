using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

/// <summary>
/// The Cheats page of the Realm of Thrones server tool: pick a player card, a category chip and a cheat, fill its
/// form and press Run. Like Coodsz Host's Cheats page, but it uses Coop's own commands only (the server runs Coodsz
/// Server Tools for the host rules, RotServerTools, and the coodsz commands are not wired here yet), so every
/// cheat is one of Coop's own coop.debug commands (<see cref="PlayerCheats"/> with tools off) and
/// the ids come from coop.debug.players.list. Each change the server takes goes into the admin log,
/// with its undo when Coop has one.
/// </summary>
public partial class CoopCheatsPage : UserControl
{
    // Coop's own commands only; the coodsz commands of Coodsz Server Tools are not used on this page.
    private const bool Tools = false;

    private readonly ObservableCollection<PlayerCard> _cards = new();
    private readonly ObservableCollection<CheatRow> _cheats = new();
    private readonly ObservableCollection<LogRow> _log = new();
    private readonly Dictionary<string, Func<string>> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<CheatInputKind, IReadOnlyList<CoopListItem>> _lists = new();
    private readonly AuditLog _audit = new(Path.Combine(BannerlordCoopLauncher.Core.IO.ToolData.RoamingRoot, "rot-admin-log.json"));
    private IReadOnlyList<CoopPlayerRow> _coopPlayers = Array.Empty<CoopPlayerRow>();
    private IReadOnlyList<string> _online = Array.Empty<string>();
    private Func<string>? _blockReason;
    private Func<string, Task<IReadOnlyList<string>>>? _run;
    private Action<string>? _remember;
    private Func<IReadOnlyList<string>>? _readFavourites;
    private Action<IReadOnlyList<string>>? _saveFavourites;
    private PlayerCheat? _cheat;
    private AuditEntry? _lastEntry;
    private string _category = nameof(CheatCategory.Gold);
    private string _picked = "";
    private bool _loading;
    private bool _busy;

    public CoopCheatsPage()
    {
        InitializeComponent();
        PlayerList.ItemsSource = _cards;
        CheatList.ItemsSource = _cheats;
        LogList.ItemsSource = _log;
        _audit.Changed += () => Dispatcher.BeginInvoke(ShowLog);
        NoteBox.Tag = Translator.T("Note (optional), e.g. troops lost to a cheater");
        Translator.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            NoteBox.Tag = Translator.T("Note (optional), e.g. troops lost to a cheater");
            ShowCheats();
            ShowForm();
        });
        ShowLog();
        ShowCheats();
        ShowForm();
    }

    /// <summary>
    /// Called once by MainWindow: why commands cannot be sent now ("" when they can), how a command
    /// is sent and its reply read, the console line writer and where the favourites are kept.
    /// </summary>
    public void Configure(Func<string> blockReason, Func<string, Task<IReadOnlyList<string>>> run, Action<string> remember,
        Func<IReadOnlyList<string>> readFavourites, Action<IReadOnlyList<string>> saveFavourites)
    {
        _blockReason = blockReason;
        _run = run;
        _remember = remember;
        _readFavourites = readFavourites;
        _saveFavourites = saveFavourites;
    }

    private string BlockReason() => _blockReason?.Invoke() ?? Translator.T("Start the server first. Cheats work while the server is running.");

    private bool CanSend => BlockReason().Length == 0;

    /// <summary>A server start: the ids and lists of the last run are read again.</summary>
    public void ServerStarting()
    {
        _lists.Clear();
        _coopPlayers = Array.Empty<CoopPlayerRow>();
    }

    /// <summary>The page was opened, or the connected players changed while it is open.</summary>
    public async void Shown(IReadOnlyList<string> online)
    {
        _online = online;
        await LoadPlayersAsync(askCoop: true);
    }

    /// <summary>The host's player list changed: the cards follow, and Coop is asked again when names came or went.</summary>
    public async void PlayersChanged(IReadOnlyList<string> online)
    {
        var changed = !online.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(_online.OrderBy(n => n, StringComparer.Ordinal));
        _online = online;
        if (changed && IsVisible)
            await LoadPlayersAsync(askCoop: true);
        else
            ShowServerLine();
    }

    private void ShowServerLine()
    {
        var blocked = BlockReason();
        ServerLine.Text = blocked.Length > 0 ? blocked : Translator.T("Cheats work while the server is running.");
    }

    private void Say(string message, bool ok)
    {
        StatusLine.Text = message;
        StatusLine.Foreground = (Brush)FindResource(ok ? "Keep.Success" : "Keep.Warning");
        StatusLine.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Sends one Coop command and says whether it worked: a reply that reads like a failure is not ok.</summary>
    private async Task<(bool Ok, IReadOnlyList<string> Lines)> SendCoopAsync(string command)
    {
        if (_run == null || !CanSend)
            return (false, new[] { BlockReason() });
        var lines = (await _run(command)).Where(l => l.Trim().Length > 0).ToList();
        // A slow console (ConsoleSlow) is not a success either: the command may or may not land, so no Done and no undone.
        var ok = !lines.Any(l => CommandReply.Tone(l) != CommandReplyTone.Normal)
                 && CheatActions.UnknownCommandNote(command, lines) == null
                 && !ServerCommandQueue.Unconfirmed(lines);
        return (ok, lines);
    }

    // --- players -----------------------------------------------------------

    private PlayerCard? Picked => _cards.FirstOrDefault(c => c.IsTarget);

    private async Task LoadPlayersAsync(bool askCoop)
    {
        if (_loading)
            return;
        _loading = true;
        try
        {
            ShowServerLine();
            if (askCoop && CanSend)
            {
                var (_, lines) = await SendCoopAsync(CoopReplies.PlayersCommand);
                var rows = CoopReplies.Players(lines);
                if (rows.Count > 0)
                    _coopPlayers = rows;
            }

            var roster = CheatRoster.Merge(_online, _coopPlayers);
            var picked = Picked?.Name ?? _picked;
            _cards.Clear();
            foreach (var player in roster)
                _cards.Add(new PlayerCard(player) { IsTarget = string.Equals(player.Name, picked, StringComparison.OrdinalIgnoreCase) });
            if (Picked == null && _cards.Count > 0)
                (_cards.FirstOrDefault(c => c.Online) ?? _cards[0]).IsTarget = true;
            _picked = Picked?.Name ?? "";
            NobodyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowCheats();
            ShowForm();
        }
        finally
        {
            _loading = false;
        }
    }

    private async void RefreshPlayers_Click(object sender, RoutedEventArgs e)
    {
        _lists.Clear();
        await LoadPlayersAsync(askCoop: true);
        if (_cheat != null)
            BuildFields();
    }

    private void Player_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlayerCard card)
            return;
        foreach (var c in _cards)
            c.IsTarget = ReferenceEquals(c, card);
        _picked = card.Name;
        ShowCheats();
        ShowForm();
    }

    private CheatTarget? Target() => Picked is { } card ? CheatRoster.Target(card.Player) : null;

    // --- categories and the cheat list -------------------------------------

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
            _category = tag;
        if (CheatList == null)
            return;
        ShowCheats();
        ShowForm();
    }

    private HashSet<string> Favourites() => new(_readFavourites?.Invoke() ?? Array.Empty<string>(), StringComparer.Ordinal);

    private void ShowCheats()
    {
        if (CheatList == null)
            return;
        var target = Target();
        var favourites = Favourites();
        IEnumerable<PlayerCheat> list = _category == "Favourites"
            ? PlayerCheats.Favourites(favourites, target, Tools)
            : Enum.TryParse<CheatCategory>(_category, out var category) ? PlayerCheats.In(category, target, Tools) : Array.Empty<PlayerCheat>();
        _cheats.Clear();
        foreach (var cheat in list)
            _cheats.Add(new CheatRow(cheat, favourites.Contains(cheat.Id)) { IsPicked = cheat.Id == _cheat?.Id });
        NoFavourites.Visibility = _category == "Favourites" && _cheats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_cheat != null && _cheats.All(r => r.Cheat.Id != _cheat.Id))
        {
            _cheat = null;
            BuildFields();
        }
    }

    private void Cheat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CheatRow row)
            return;
        _cheat = row.Cheat;
        foreach (var r in _cheats)
            r.IsPicked = ReferenceEquals(r, row);
        _lastEntry = null;
        ResultBar.Visibility = Visibility.Collapsed;
        BuildFields();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CheatRow row)
            return;
        var favourites = Favourites();
        if (!favourites.Remove(row.Cheat.Id))
            favourites.Add(row.Cheat.Id);
        row.IsFavourite = favourites.Contains(row.Cheat.Id);
        _saveFavourites?.Invoke(PlayerCheats.All.Select(c => c.Id).Where(favourites.Contains).ToList());
        if (_category == "Favourites")
            ShowCheats();
    }

    // --- the form ------------------------------------------------------------

    /// <summary>Makes the boxes of the picked cheat: numbers with preset chips, dropdowns and tick boxes.</summary>
    private void BuildFields()
    {
        FormFields.Children.Clear();
        _values.Clear();
        if (_cheat == null)
        {
            ShowForm();
            return;
        }

        foreach (var input in _cheat.Inputs)
        {
            switch (input.Kind)
            {
                case CheatInputKind.Number:
                    AddNumber(input);
                    break;
                case CheatInputKind.Toggle:
                    AddToggle(input);
                    break;
                default:
                    AddList(input);
                    break;
            }
        }

        ShowForm();
    }

    private TextBlock Label(string text) =>
        new() { Text = Translator.T(text), Style = (Style)FindResource("Keep.Label"), Margin = new Thickness(0, 0, 0, 4) };

    private void AddNumber(CheatInput input)
    {
        FormFields.Children.Add(Label(input.Label));
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var box = new TextBox
        {
            Text = input.Default.ToString(CultureInfo.InvariantCulture),
            Style = (Style)FindResource("Keep.Text"),
            Width = 150,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = Translator.T("From") + " " + input.Min.ToString("N0", CultureInfo.CurrentCulture) + " " + Translator.T("to") + " " + input.Max.ToString("N0", CultureInfo.CurrentCulture),
        };
        box.TextChanged += (_, _) => ShowPreview();
        row.Children.Add(box);
        foreach (var preset in input.Presets ?? Array.Empty<int>())
        {
            var value = preset;
            var chip = new Button
            {
                Content = value.ToString("N0", CultureInfo.CurrentCulture),
                Style = (Style)FindResource("Keep.Small"),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Translator.T("Puts this number in the box."),
            };
            chip.Click += (_, _) => box.Text = value.ToString(CultureInfo.InvariantCulture);
            row.Children.Add(chip);
        }

        FormFields.Children.Add(row);
        _values[input.Key] = () => box.Text;
    }

    private void AddToggle(CheatInput input)
    {
        var check = new CheckBox
        {
            Content = Translator.T(input.Label),
            IsChecked = input.Default != 0,
            Style = (Style)FindResource("Keep.Check"),
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = Translator.T("Ticked sends true, not ticked sends false."),
        };
        check.Click += (_, _) => ShowPreview();
        FormFields.Children.Add(check);
        _values[input.Key] = () => check.IsChecked == true ? "1" : "0";
    }

    private void AddList(CheatInput input)
    {
        FormFields.Children.Add(Label(input.Label));
        var combo = new ComboBox
        {
            Style = (Style)FindResource("Keep.Combo"),
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = Translator.T("Pick one. The server's own list fills it."),
        };
        combo.SelectionChanged += (_, _) => ShowPreview();
        var note = new TextBlock { Style = (Style)FindResource("Keep.Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var longList = input.Kind is CheatInputKind.Hero or CheatInputKind.Town or CheatInputKind.Village;
        TextBox? search = null;
        if (longList)
        {
            search = new TextBox { Style = (Style)FindResource("Keep.Text"), Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Tag = Translator.T("Search...") };
            FormFields.Children.Add(search);
        }

        FormFields.Children.Add(combo);
        FormFields.Children.Add(note);
        _values[input.Key] = () => (combo.SelectedItem as Choice)?.Key ?? "";

        IReadOnlyList<Choice> all = Array.Empty<Choice>();
        void Fill()
        {
            var q = search?.Text.Trim() ?? "";
            var selected = combo.SelectedItem as Choice;
            var shown = all.Where(c => q.Length == 0 || c.Value.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Key.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(400).ToList();
            combo.ItemsSource = shown;
            combo.SelectedItem = selected != null && shown.Contains(selected) ? selected : shown.Count == 1 || !longList ? shown.FirstOrDefault() : null;
        }

        if (search != null)
            search.TextChanged += (_, _) => Fill();

        if (input.Kind == CheatInputKind.Skill)
        {
            all = PlayerCheats.Skills.Select(s => new Choice(s.Id, Translator.T(s.Name))).ToList();
            Fill();
            return;
        }

        _ = LoadListAsync(input.Kind, note, items =>
        {
            all = items.Select(i => new Choice(i.Id, i.Name.Length > 0 ? i.Name + "  (" + i.Id + ")" : i.Id)).ToList();
            Fill();
        });
    }

    private async Task LoadListAsync(CheatInputKind kind, TextBlock note, Action<IReadOnlyList<CoopListItem>> done)
    {
        if (_lists.TryGetValue(kind, out var cached))
        {
            done(cached);
            return;
        }

        if (!CanSend)
        {
            note.Text = Translator.T("Start the server to fill this list.");
            return;
        }

        var (command, read) = kind switch
        {
            CheatInputKind.Kingdom => (CoopReplies.KingdomsCommand, (Func<IEnumerable<string>, IReadOnlyList<CoopListItem>>)CoopReplies.Kingdoms),
            CheatInputKind.Town => (CoopReplies.TownsCommand, CoopReplies.IdThenName),
            CheatInputKind.Village => (CoopReplies.VillagesCommand, CoopReplies.IdThenName),
            _ => (CoopReplies.HeroesCommand, CoopReplies.Heroes),
        };
        note.Text = Translator.T("Reading the list from the server...");
        var (_, lines) = await SendCoopAsync(command);
        var items = read(lines);
        if (kind == CheatInputKind.Hero && Target() is { HeroId.Length: > 0 } target)
            items = items.Where(i => i.Id != target.HeroId).ToList();
        if (items.Count > 0)
            _lists[kind] = items;
        note.Text = items.Count == 0
            ? Translator.T("The server sent no list. Press refresh above and try again.")
            : "";
        done(items);
    }

    private Dictionary<string, string> Values() => _values.ToDictionary(v => v.Key, v => v.Value(), StringComparer.Ordinal);

    /// <summary>The title, help, offline note and the command line of the picked cheat.</summary>
    private void ShowForm()
    {
        if (FormPanel == null)
            return;
        PickCheat.Visibility = _cheat == null ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = _cheat == null ? Visibility.Collapsed : Visibility.Visible;
        if (_cheat == null)
            return;
        var target = Target();
        FormTitle.Text = _cheat.NotAPlayer
            ? Translator.T(_cheat.Title)
            : Translator.T(_cheat.Title) + " " + Translator.T("for") + " " + (Picked?.Name ?? Translator.T("nobody yet"));
        FormHelp.Text = Translator.T(_cheat.Help);
        var notes = new List<string>();
        if (!_cheat.NotAPlayer && target == null)
            notes.Add(Translator.T("Pick a player above first."));
        if (PlayerCheats.OfflineNote(_cheat, target) is { } offline)
            notes.Add(Translator.T(offline));
        if (_cheat.Id == "skills.reset")
            notes.Add(Translator.T("This can't be undone. The hero's skills, attributes and focus all go back to the start."));
        FormNoteText.Text = string.Join(" ", notes);
        FormNote.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (_cheat == null)
            return;
        try
        {
            CommandPreview.Text = PlayerCheats.Build(_cheat, _cheat.NotAPlayer ? null : Target(), Values()).Command;
            CommandPreview.Foreground = (Brush)FindResource("Keep.TextMain");
        }
        catch (ArgumentException ex)
        {
            CommandPreview.Text = Translator.T(ex.Message);
            CommandPreview.Foreground = (Brush)FindResource("Keep.TextDim");
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        BuildFields();
        ResultBar.Visibility = Visibility.Collapsed;
    }

    private void ShowResult(string text, bool ok, bool canUndo = false)
    {
        ResultLine.Text = text;
        ResultLine.Foreground = (Brush)FindResource(ok ? "Keep.Success" : "Keep.Warning");
        ResultBar.Visibility = Visibility.Visible;
        UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_cheat == null || _busy)
            return;
        var blocked = BlockReason();
        if (blocked.Length > 0)
        {
            ShowResult(blocked, false);
            return;
        }

        var cheat = _cheat;
        var target = cheat.NotAPlayer ? null : Target();
        CheatBuild build;
        try
        {
            build = PlayerCheats.Build(cheat, target, Values());
        }
        catch (ArgumentException ex)
        {
            ShowResult(Translator.T(ex.Message), false);
            return;
        }

        var who = target == null ? Translator.T("World") : target.Name;
        if (cheat.Id == "skills.reset" && Window.GetWindow(this) is { } owner
            && !ConfirmWindow.Ask(owner, Translator.T("Reset skills"), Translator.T("Reset the skills of") + " " + who + "? " + Translator.T("This can't be undone."), null, Translator.T("Reset")))
            return;

        _busy = true;
        RunButton.IsEnabled = false;
        try
        {
            var (ok, lines) = await SendCoopAsync(build.Command);
            var reply = lines.Count == 0 ? Translator.T("The server printed nothing after that command.") : string.Join(" ", lines.Take(4));
            // The console was slow and the cheat may land a moment later: shown as that, never as Done, and not in the admin
            // log, since nothing confirms it.
            if (ServerCommandQueue.Unconfirmed(lines))
            {
                ShowResult(Translator.T(ServerCommandQueue.ConsoleSlow), false);
                return;
            }

            if (!ok)
            {
                ShowResult(CheatActions.UnknownCommandNote(build.Command, lines) ?? reply, false);
                return;
            }

            var undo = build.Undo;
            var title = Translator.T(cheat.Title);
            _lastEntry = _audit.Add(who, build.Summary + " · " + build.Command, NoteBox.Text.Trim(), undo == null ? null : async () =>
            {
                var (back, backLines) = await SendCoopAsync(undo);
                if (back)
                    _audit.Add(who, Translator.T("undo") + " " + title + " · " + undo);
                var said = backLines.Count == 0 ? "" : " " + string.Join(" ", backLines.Take(2));
                Say(back ? who + ": " + Translator.T("undone") + said : who + ":" + said, back);
            });
            _remember?.Invoke("[Cheats] " + who + ": " + build.Summary);
            ShowResult(Translator.T("Done.") + " " + reply + (build.Note == null ? "" : " " + Translator.T(build.Note)), true, undo != null);
        }
        finally
        {
            _busy = false;
            RunButton.IsEnabled = true;
        }
    }

    private async void UndoLast_Click(object sender, RoutedEventArgs e)
    {
        if (_lastEntry == null)
            return;
        var blocked = BlockReason();
        if (blocked.Length > 0)
        {
            ShowResult(blocked, false);
            return;
        }

        var entry = _lastEntry;
        _lastEntry = null;
        UndoButton.Visibility = Visibility.Collapsed;
        await _audit.UndoAsync(entry);
        ShowResult(StatusLine.Text, true);
    }

    // --- admin log ----------------------------------------------

    private void LogToggle_Click(object sender, RoutedEventArgs e) =>
        LogPanel.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void ShowLog()
    {
        _log.Clear();
        foreach (var entry in _audit.Entries.Take(80))
            _log.Add(new LogRow(entry, _audit.CanUndo(entry)));
        LogEmpty.Visibility = _log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogCount.Text = _audit.Entries.Count == 0 ? "" : "(" + _audit.Entries.Count + ")";
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LogRow row)
            return;
        var blocked = BlockReason();
        if (blocked.Length > 0)
        {
            Say(blocked, false);
            return;
        }

        await _audit.UndoAsync(row.Entry);
    }

    // --- rows ------------------------------------------------------------------

    /// <summary>A player card: the name, the online dot and the hero id Coop gave.</summary>
    public sealed class PlayerCard : Bindable
    {
        private bool _isTarget;

        public PlayerCard(CheatPlayer player) => Player = player;

        public CheatPlayer Player { get; }
        public string Name => Player.Name;
        public bool Online => Player.Online;
        public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        public string Detail => Player.HeroId.Length > 0 ? Player.HeroId : Translator.T("Hero not known yet");
        public bool IsTarget { get => _isTarget; set => Set(ref _isTarget, value); }
    }

    /// <summary>A cheat in the list of the picked category, with its favourite star.</summary>
    public sealed class CheatRow : Bindable
    {
        private bool _isPicked;
        private bool _isFavourite;

        public CheatRow(PlayerCheat cheat, bool favourite)
        {
            Cheat = cheat;
            _isFavourite = favourite;
        }

        public PlayerCheat Cheat { get; }
        public string Title => Translator.T(Cheat.Title);
        public string Help => Translator.T(Cheat.Help);

        public string Tags => string.Join(" · ", new[]
        {
            Cheat.NotAPlayer ? Translator.T("Not a player") : "",
            Cheat.Id == "skills.reset" ? Translator.T("Can't be undone") : "",
        }.Where(t => t.Length > 0));

        public bool HasTags => Tags.Length > 0;
        public bool IsPicked { get => _isPicked; set => Set(ref _isPicked, value); }

        public bool IsFavourite
        {
            get => _isFavourite;
            set
            {
                if (Set(ref _isFavourite, value))
                    OnChanged(nameof(StarFill));
            }
        }

        public Brush StarFill => _isFavourite ? (Brush)Application.Current.FindResource("Keep.Warning") : Brushes.Transparent;
    }

    /// <summary>A line of the admin log list.</summary>
    public sealed class LogRow
    {
        public LogRow(AuditEntry entry, bool canUndo)
        {
            Entry = entry;
            CanUndo = canUndo;
        }

        public AuditEntry Entry { get; }
        public bool CanUndo { get; }
        public string When => Entry.DayText + "  " + Entry.TimeText;
        public string Text => Entry.Line;
        public string UndoText => Translator.T("Undo");
    }

    /// <summary>A dropdown choice: an id and the text shown.</summary>
    public sealed record Choice(string Key, string Value)
    {
        public override string ToString() => Value;
    }

    public abstract class Bindable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnChanged(name);
            return true;
        }
    }
}
