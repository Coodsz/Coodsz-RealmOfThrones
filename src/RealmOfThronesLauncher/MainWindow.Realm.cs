using System.Diagnostics;
using System.Windows;

namespace RealmOfThronesLauncher;

public partial class MainWindow
{
    // The Realm Bannerlord Discord invite. Use an invite that never expires.
    private const string RealmInviteUrl = "https://discord.gg/z8SncS7UX";

    private void RealmJoin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RealmInviteUrl) { UseShellExecute = true });
            RealmStatus.Text = "Opening Discord in your browser…";
        }
        catch
        {
            RealmStatus.Text = "Couldn't open the browser. Use Copy invite and paste it in Discord.";
        }
    }

    private void RealmCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RealmInviteUrl);
            RealmStatus.Text = "Invite link copied. Paste it in Discord or your browser.";
        }
        catch
        {
            RealmStatus.Text = RealmInviteUrl;
        }
    }

    // One code per Steam account; see RealmRewardCode. Clicking copies it.
    private void RealmReward_Click(object sender, RoutedEventArgs e)
    {
        var code = BannerlordCoopLauncher.Core.Realm.RealmRewardCode.ForCurrentSteamUser();
        if (code == null)
        {
            RealmStatus.Text = BannerlordCoopLauncher.Core.Realm.RealmRewardCode.NoSteam;
            return;
        }

        var copied = false;
        try
        {
            Clipboard.SetText(code);
            copied = true;
        }
        catch
        {
        }

        RealmStatus.Text = "Your reward code: " + code + (copied ? " (copied)" : "")
            + ". Join our Discord, open a \u201cCoodsz Tools Reward\u201d ticket and paste it.";
    }
}
