using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace RealmOfThronesLauncher;

public partial class App : Application
{
    // One Client Launcher at a time, since a second one would put Coop in or take it out under the first
    // (MainWindow.CoopBuild). Kept for the whole run; Windows lets go of it when the process ends.
    private static Mutex? _oneLauncher;

    public const string AlreadyOpen =
        "The Realm of Thrones Client Launcher is already open. Look for it in the taskbar. If you closed it while Bannerlord runs, it waits there for the game to close and then puts Coop back.";

    protected override void OnStartup(StartupEventArgs e)
    {
        // A copy of this exe started to read a crash dump writes its text, then exits (DumpStackSummary).
        if (DumpStackSummary.OnStartup(e.Args))
        {
            Environment.Exit(Environment.ExitCode);
            return;
        }

        // Settings in %LOCALAPPDATA%\Coodsz (copied once from beside the exe), and the modules this exe carries.
        ToolData.Start(ToolData.RotClient);
        Coodsz.Ui.UiLanguage.Start();
        _oneLauncher = new Mutex(true, @"Local\CoodszRotClientLauncher", out var first);
        if (!first)
        {
            Coodsz.Ui.UiLanguage.Message(AlreadyOpen, "Coodsz Realm of Thrones Client Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }
        Coodsz.Ui.UiLanguage.LicenseOnce("rot-launcher");
        Coodsz.Ui.UiLanguage.WhatsNewOnce("rot-launcher", ReleaseNotice.RotVersion, WhatsNew.Rot);
        // The tool's own colour on the sidebar brand mark (build-plan.md 1.2), and the dark stone picture behind the pages
        // under a near black gradient, 30% at the top to 82% at the bottom. After InitializeComponent, so the keys land in the
        // App.xaml resources.
        Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.RotClient);
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/rot-backdrop-ember-green.png"));
        Resources["Keep.BackdropOverlayTop"] = System.Windows.Media.Color.FromArgb(0x4D, 0x05, 0x06, 0x06);
        Resources["Keep.BackdropOverlayBottom"] = System.Windows.Media.Color.FromArgb(0xD1, 0x05, 0x06, 0x06);
        base.OnStartup(e);
    }
}
