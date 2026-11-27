using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Parsing;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesLauncher;

// Auto sort and the checks before Connect, from each mod's SubModule.xml and the Realm of Thrones rules, and
// the chips of each mod row from the Realm of Thrones list (RotCompatList): the list status, the green and red
// counts of the reports sent with tool rot and the Report link.
public partial class MainWindow
{
    // ----- The UIExtenderEx prefabs fix. Realm of Thrones has no dependency fix: the library mods
    // load in the core, so a mod keeps its own SubModule.xml. -----

    private List<string> ModRoots()
    {
        var roots = Workshops().Select(library => library.ContentDirectory).ToList();
        var modules = GameModulesFolder();
        if (modules != null)
            roots.Add(modules);
        return roots;
    }

    // Puts back every SubModule.xml the prefabs fix changed. Returns how many were put back.
    private int RestorePrefabsFix()
    {
        var problems = new List<string>();
        var restored = BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.RestoreAll(ModRoots(), problems);
        foreach (var folder in restored)
            Log("Put back the original SubModule.xml of " + Path.GetFileName(folder) + ".");
        foreach (var problem in problems)
            Log(problem);
        return restored.Count;
    }

    // Right before the game starts: a UIExtenderEx copy missing a DLL its Compiled Prefabs need gets
    // them turned off (see UIExtenderExPrefabsFix). It is put back when the game closes.
    private void ApplyPrefabsFix(TaomSetup setup)
    {
        RestorePrefabsFix();
        foreach (var line in BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.ApplyForLaunch(RotLoadOrder.LaunchModules(setup, joinPassword: false), ModRoots()))
            Log(line);
        if (!setup.HasEnabledExtras)
            return;
        UnblockModDlls(setup);
    }

    // Starts the game with the prefabs fix in place. When the game does not start (no process or an
    // error), the fix is put back right away, since no Exited event ever will.
    private System.Diagnostics.Process StartWithPrefabsFix(TaomSetup setup, System.Diagnostics.ProcessStartInfo start)
    {
        ApplyPrefabsFix(setup);
        System.Diagnostics.Process? started = null;
        try
        {
            started = System.Diagnostics.Process.Start(start);
            // The close counts a game started a moment ago as running until it exits (MainWindow.CoopBuild).
            _gameStartedAt = DateTime.UtcNow;
            _startedGame = started;
        }
        finally
        {
            if (started == null)
                RestorePrefabsFix();
        }

        return started ?? throw new InvalidOperationException("Bannerlord did not start.");
    }

    // Windows marks DLLs from a downloaded zip, and the game cannot load a marked DLL. Done before
    // every launch and after Install mods, since an update brings the mark back.
    private void UnblockModDlls(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact>? facts = null)
    {
        facts ??= GatherModFacts();
        foreach (var mod in setup.EnabledExtras)
        {
            if (!facts.TryGetValue(mod.ModuleId, out var fact))
                continue;
            foreach (var folder in fact.Copies)
            {
                try
                {
                    var count = BannerlordCoopLauncher.Core.Support.ModUnblock.UnblockDlls(folder);
                    if (count > 0)
                        Log(BannerlordCoopLauncher.Core.Support.ModUnblock.Line(mod.Label, count));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log("Could not unblock the DLL files of " + mod.Label + ". " + ex.Message);
                }
            }
        }
    }

    private void RestoreWhenClosed(System.Diagnostics.Process game)
    {
        try
        {
            game.Exited += (_, _) => Dispatcher.Invoke(() => RestorePrefabsFix());
            game.EnableRaisingEvents = true;
            // A game that closed before the handler was set may not raise Exited; putting the fix back twice is harmless.
            if (game.HasExited)
                RestorePrefabsFix();
        }
        catch (InvalidOperationException)
        {
            // The process can no longer be watched, so no Exited event comes to put the fix back.
            RestorePrefabsFix();
        }
    }

    // Every module folder the game can load: Steam Workshop items and the game's Modules folder. TaomModFacts knows
    // the same library mods (Harmony, ButterLib, UIExtenderEx, MCM) Realm of Thrones loads.
    private IReadOnlyDictionary<string, TaomModFact> GatherModFacts() => TaomModFacts.Gather(ModRoots());

    private static IReadOnlyDictionary<string, ModuleManifest> Manifests(IReadOnlyDictionary<string, TaomModFact> facts) =>
        facts.Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);

    private void AutoSort_Click(object sender, RoutedEventArgs e)
    {
        if (_editing.Locked)
        {
            Log(_editing.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
            return;
        }

        var result = RotAutoSort.Sort(_editing, Manifests(GatherModFacts()));
        ChangedMods();
        if (result.Lines.Count == 0)
        {
            ShowModOrderNotice("Auto sort found nothing to change. The extra mods already load in a good order.");
            return;
        }

        var lines = result.Changed ? result.Lines.Append("Click Save mods to keep the new order.") : result.Lines;
        ShowModOrderNotice(string.Join(Environment.NewLine, lines));
    }

    // ----- The Realm of Thrones compatibility list -----

    private RotCompatList _compat = RotCompatList.BuiltIn();

    private static string CompatCachePath => BannerlordCoopLauncher.Core.IO.ToolData.File(RotCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = RotCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    // Takes the online list when it can be read; otherwise the saved or built in list stays.
    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await RotCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModuleList);
    }

    private static System.Windows.Media.Color BadgeColor(CompatTag tag) => RotCompatList.BadgeColor(tag) switch
    {
        "green" => System.Windows.Media.Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => System.Windows.Media.Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    // The list badge of a mod in the Add mods window: Unknown, not tested when the list does not know it.
    private BannerlordCoopLauncher.ModBadge? PickerBadge(BannerlordCoopLauncher.Core.Profile.FoundMod mod)
    {
        if (RotModChips.ShowsUnknown(_compat, mod.ModuleId, mod.WorkshopId, mod.Name))
            return BannerlordCoopLauncher.ModPills.Unknown(RotModChips.UnknownTip, null);
        var entry = _compat.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        var text = entry == null ? null : RotCompatList.BadgeText(entry.Kind);
        return entry == null || text == null ? null
            : new BannerlordCoopLauncher.ModBadge(text, BadgeColor(entry.Kind), RotCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T), null);
    }

    // A known broken mod is only added when the player says so.
    private bool ConfirmBrokenMod(string name, string moduleId, string? workshopId)
    {
        var entry = _compat.Find(moduleId, workshopId, name);
        if (entry is not { Kind: CompatTag.Broken })
            return true;
        return Coodsz.Ui.UiLanguage.Message(RotCompatList.BrokenWarning(name, entry) + "\n\nAdd it anyway?", "Known broken mod",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes;
    }

    // The chips of an extra mod row, in the order Coodsz Coop and Friends shows them: the Realm of Thrones list
    // status (Unknown, not tested in grey when the list does not know the mod), the green and red counts of the
    // reports sent with tool rot and the Report link. The Ready pill sits just before this column (StatusPill).
    // A fixed column: the chips wrap to a second line when they do not fit, and a chip wider than the column is
    // trimmed (the hover has the text).
    private FrameworkElement? CompatBadge(string moduleId, string? workshopId, string label)
    {
        // With sending set up, a click opens the Works, Broken and Works only with buttons.
        var report = RotModChips.OffersReport(moduleId);
        var status = RotModChips.Status(_compat, moduleId, workshopId, label, report, BannerlordCoopLauncher.Core.Localization.Translator.T);
        if (status == null)
            return null;
        var panel = BadgeColumn();
        var (ink, back) = ChipColors(status.Kind);
        var badge = new System.Windows.Controls.Border
        {
            Background = back,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(0, 1, 6, 1),
            MaxWidth = BadgeColumnWidth,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = status.Text + "\n" + status.Tip,
            Child = new System.Windows.Controls.TextBlock
            {
                Text = status.Text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ink
            }
        };
        void Report() => Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(moduleId, label, InstalledVersion(moduleId)));
        if (report)
        {
            badge.Cursor = System.Windows.Input.Cursors.Hand;
            badge.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Report();
            };
        }

        panel.Children.Add(badge);
        if (CommunityCounts(moduleId, workshopId, label) is { } chips)
            AddChips(panel, chips);
        // The small Report link, the same as a click on the status chip, so reporting is plain to see.
        if (report)
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, Report));
        return panel;
    }

    // A core row (the library mods, the Realm of Thrones modules and the compat module): what players reported and
    // the Report link. The list status stays for the extra mods. Null for Coop and the official modules.
    private System.Windows.Controls.WrapPanel? CoreReportChips(string moduleId, string name, string version)
    {
        if (!BannerlordCoopLauncher.Core.Modules.ModReports.Rates(moduleId))
            return null;
        var panel = BadgeColumn();
        if (CommunityCounts(moduleId, null, null) is { } chips)
            AddChips(panel, chips);
        if (RotModChips.OffersReport(moduleId))
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(moduleId, name, version))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // The green and red count chips of a mod, from the reports sent with tool rot that the tally counts into the
    // Realm of Thrones list (not the Coop and Friends list).
    private FrameworkElement? CommunityCounts(string moduleId, string? workshopId, string? name) =>
        _compat.CountsFor(moduleId, workshopId, name) is { } counts ? Coodsz.Ui.ModReportUi.CountChips(this, counts.Works, counts.Broken) : null;

    // CountChips gives one row of chips; each chip goes into the column on its own, so a chip that does not
    // fit wraps to the next line like in Coop and Friends instead of being cut off. A chip keeps its row's tip.
    private static void AddChips(System.Windows.Controls.Panel column, FrameworkElement chips)
    {
        if (chips is not System.Windows.Controls.StackPanel row)
        {
            column.Children.Add(chips);
            return;
        }

        foreach (var chip in row.Children.OfType<FrameworkElement>().ToList())
        {
            row.Children.Remove(chip);
            chip.ToolTip ??= row.ToolTip;
            AddChips(column, chip);
        }
    }

    // The colors of a status chip, the same as Coodsz Coop and Friends' list badge: green, gold, blue, red or grey.
    private (System.Windows.Media.Brush Ink, System.Windows.Media.Brush Back) ChipColors(string kind) => kind switch
    {
        RotModChips.Works => (Coodsz.Ui.ModReportUi.Green(this), Coodsz.Ui.ModReportUi.GreenBack(this)),
        RotModChips.Broken => (Coodsz.Ui.ModReportUi.Red(this), Coodsz.Ui.ModReportUi.RedBack(this)),
        RotModChips.Risky => (Coodsz.Ui.ModReportUi.Gold(this), Coodsz.Ui.ModReportUi.GoldBack(this)),
        RotModChips.Reported => (ChipBrush(0x8E, 0xB4, 0xD4), ChipBrush(0x1C, 0x2A, 0x3A)),
        _ => (ChipBrush(0x8E, 0xA0, 0xAE), ChipBrush(0x24, 0x30, 0x3A))
    };

    private static System.Windows.Media.SolidColorBrush ChipBrush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));

    // The report window for one mod, with the setup's launch order and each module's version on this PC.
    private Coodsz.Ui.ModReportRequest ReportRequest(string moduleId, string name, string version) => new()
    {
        Tool = RotRelease.ToolId,
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = RotStack.CoopVersion,
        Game = RotStack.GameVersion,
        Rate = new[] { new Coodsz.Ui.ModReportChoice(moduleId, name, version) },
        ModList = BannerlordCoopLauncher.Core.Modules.ModReports.ModList(LaunchOrder().Select(id =>
            (id, (string?)(InstalledVersion(id) is { Length: > 0 } extra ? extra : CoreVersion(id)))))
    };

    // The width of the chip column after the status pill, the same on every row, so the buttons line up.
    internal const double BadgeColumnWidth = Coodsz.Ui.ModRowLayout.ClientChipColumn;

    internal const double StatusColumnWidth = 104;

    /// <summary>The Ready, Off, Missing or Wrong version chip of a row, the shared Coodsz.Ui.Chip like the other tools' rows, in
    /// its colour from ModChips.Status (the English word; UiLanguage shows it in the picked language), in a fixed column
    /// so the chips after it line up on every row.</summary>
    private System.Windows.Controls.Border StatusPill(string text, string kind)
    {
        var chip = BannerlordCoopLauncher.Core.Modules.ModChips.Status(kind);
        var pill = new Coodsz.Ui.Chip
        {
            Text = text,
            Kind = Enum.Parse<Coodsz.Ui.ChipKind>(chip.KindName),
            Tip = chip.Tip,
            MaxWidth = StatusColumnWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(pill, "RowStatusPill");
        return new System.Windows.Controls.Border { Width = StatusColumnWidth, VerticalAlignment = VerticalAlignment.Center, Child = pill };
    }

    /// <summary>The chip column of a mod row: one fixed width for every row, so the move and remove
    /// buttons of each row line up.</summary>
    internal static System.Windows.Controls.WrapPanel BadgeColumn() => new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Width = BadgeColumnWidth
    };

    /// <summary>Logs the checks for a setup. False when a problem blocks the launch.</summary>
    private bool ModChecksPass(TaomSetup setup)
    {
        if (RotLoadOrder.ClientOrder(setup).Count == RotStack.ClientModules.Length)
            return true;
        IReadOnlyList<TaomModIssue> issues;
        try
        {
            issues = RotModChecks.Run(setup, GatherModFacts());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not check the extra mods. " + ex.Message);
            return true;
        }

        foreach (var issue in issues)
            Log(issue.Text);
        var blocking = issues.Where(issue => issue.Blocks).ToList();
        if (blocking.Count == 0)
            return true;
        ShowJoin("Fix the mod problems first. " + blocking[0].Text);
        return false;
    }
}
