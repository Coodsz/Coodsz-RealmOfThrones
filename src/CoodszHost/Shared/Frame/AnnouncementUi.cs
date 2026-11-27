using System.Windows;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Support;

namespace Coodsz.Ui;

/// <summary>
/// Coodsz's notice from compat-list/announcement.json (AnnouncementFile) in a window's AnnouncementBanner, in the Banners stack above
/// the pages so every page shows it, after the crash, report and reply bars (it asks for reports, so it never hides them). The kept copy shows at once, the download updates it a moment later,
/// and a failed download changes nothing. Dismiss hides it until the id or the words change.
/// </summary>
public static class AnnouncementUi
{
    /// <summary>Call once when the window is set up. family: coop, taom, ad1259 or be.</summary>
    public static void Attach(Window owner, Banner banner, string family)
    {
        var path = AnnouncementDismissals.PathForThisTool();
        var file = AnnouncementFile.LoadCached();
        AnnouncementText? shown = null;

        void Show()
        {
            shown = file?.For(family, Translator.Current);
            if (shown == null || AnnouncementDismissals.IsDismissed(path, shown))
            {
                banner.Visibility = Visibility.Collapsed;
                return;
            }

            banner.Title = shown.Title;
            banner.Text = shown.Text;
            banner.Visibility = Visibility.Visible;
        }

        banner.Dismissed += () =>
        {
            if (shown != null)
                AnnouncementDismissals.Dismiss(path, shown);
        };
        // The words follow the language button. Translator.Changed is static, so the window lets go of it when it closes.
        void LanguageChanged() => owner.Dispatcher.BeginInvoke(Show);
        Translator.Changed += LanguageChanged;
        owner.Closed += (_, _) => Translator.Changed -= LanguageChanged;
        Show();
        _ = Task.Run(async () =>
        {
            var fresh = await AnnouncementFile.FetchForWindowAsync("CoodszAnnouncement/1").ConfigureAwait(false);
            owner.Dispatcher.BeginInvoke(() =>
            {
                file = fresh;
                Show();
            });
        });
    }
}
