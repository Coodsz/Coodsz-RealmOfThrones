using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace RealmOfThronesHost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // A copy of this exe started to read a crash dump writes its text, then exits (DumpStackSummary).
        if (DumpStackSummary.OnStartup(e.Args))
        {
            Environment.Exit(Environment.ExitCode);
            return;
        }

        // Settings in %LOCALAPPDATA%\Coodsz (copied once from beside the exe), and the modules this exe carries.
        ToolData.Start(ToolData.RotServer);
        // A net like Coodsz Host's: an exception a window handler did not catch is logged and the tool keeps running, instead
        // of closing with the server still up.
        // Only once the window is up: an error while it is still being made would otherwise leave a process with no window, so
        // that one still ends the tool the usual way (and its crash file is written).
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                if (MainWindow is not MainWindow { IsLoaded: true } window)
                    return;
                args.Handled = true;
                window.NoteUnexpected(args.Exception);
            }
            catch (Exception)
            {
                // The net itself never throws.
            }
        };
        Coodsz.Ui.UiLanguage.Start();
        Coodsz.Ui.UiLanguage.LicenseOnce("rot-server");
        Coodsz.Ui.UiLanguage.WhatsNewOnce("rot-server", ReleaseNotice.RotVersion, WhatsNew.Rot);
        // The tool's own colour on the sidebar brand mark (build-plan.md 1.2); the red page edge comes from KeepColors.Rot.xaml.
        Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.RotServer);
        // The picture behind the pages (Keep.BackdropArt): dark stone with a faint green and a low red glow, under a near
        // black gradient, 30% at the top to 82% at the bottom.
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/rot-backdrop-ember-green.png"));
        Resources["Keep.BackdropOverlayTop"] = System.Windows.Media.Color.FromArgb(0x4D, 0x05, 0x06, 0x06);
        Resources["Keep.BackdropOverlayBottom"] = System.Windows.Media.Color.FromArgb(0xD1, 0x05, 0x06, 0x06);
        base.OnStartup(e);
    }
}
