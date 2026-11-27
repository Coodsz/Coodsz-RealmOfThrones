using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Modules;
using RealmOfThrones.Core;

namespace RealmOfThronesHost;

// The mod reports on the mods page, the same as each mod row of the Realm of Thrones client: what players reported
// (the green and red count chips from the Realm of Thrones list) and a small Report link that opens the report
// window with the server's mods in load order. Reports carry tool rot.
public partial class MainWindow
{
    // Each mod's version for this redraw of the mods page.
    private readonly Dictionary<string, string> _rowVersions = new(StringComparer.OrdinalIgnoreCase);

    private void SetUpModReports()
    {
        // The list status and the count chips come from the Realm of Thrones list.
        LoadCompatList();
    }

    // The chips and the Report link of a mod row, or null for the official modules and Coop. A host report says the
    // server ran with the mod, so serverRunsCode is true only for the core and for an extra the plan stages with code
    // the server runs (RotServerModule.Code DataOnly or ServerBuild). For ClientCodeOnly, ClientCodeSkipped or an extra
    // the server does not load, a report would only mean it loaded, so the row gets the chips but no Report link.
    private FrameworkElement? ReportChips(string moduleId, string? workshopId, string name, string version, IReadOnlyList<(string Id, string? Version)> loadOrder, bool serverRunsCode)
    {
        if (!ModReports.Rates(moduleId))
            return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_compat.CountsFor(moduleId, workshopId, name) is { } counts && Coodsz.Ui.ModReportUi.CountChips(this, counts.Works, counts.Broken) is { } chips)
            panel.Children.Add(chips);
        if (RotModChips.OffersReport(moduleId) && serverRunsCode)
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(moduleId, name, version, loadOrder))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // The report window for one mod, with the mods the server loads in order.
    private Coodsz.Ui.ModReportRequest ReportRequest(string moduleId, string name, string version, IReadOnlyList<(string Id, string? Version)> loadOrder) => new()
    {
        Tool = RotRelease.ToolId,
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = RotStack.CoopVersion,
        Game = RotStack.GameVersion,
        Rate = new[] { new Coodsz.Ui.ModReportChoice(moduleId, name, version) },
        ModList = ModReports.ModList(loadOrder)
    };

    // The version of a module for this redraw: the folder Start server copies a core module from, else the mod's
    // copy on this PC. Read once.
    private string RowVersion(string moduleId)
    {
        if (_rowVersions.TryGetValue(moduleId, out var known))
            return known;
        var folder = RotStack.IsLibrary(moduleId) || RotStack.IsRotModule(moduleId)
            ? RotServerPath.FindModule(CoreSources(), moduleId)
            : moduleId.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase)
                ? CompatModuleSource()
                : HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Directory;
        var version = ManifestVersion(folder);
        _rowVersions[moduleId] = version;
        return version;
    }
}
