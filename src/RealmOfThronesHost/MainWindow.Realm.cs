using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace RealmOfThronesHost;

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
            RealmStatus.Text = "Couldn't open the browser. Use Copy invite link and paste it in Discord.";
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

    private void RealmReward_Click(object sender, RoutedEventArgs e)
    {
        RealmRewardPanel.Visibility = RealmRewardPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (RealmRewardPanel.Visibility == Visibility.Visible)
            ShowRealmRewardCode();
    }

    // One code per Steam account; see RealmRewardCode.
    private void ShowRealmRewardCode()
    {
        var code = BannerlordCoopLauncher.Core.Realm.RealmRewardCode.ForCurrentSteamUser();
        RealmRewardCodeBox.Text = code ?? "";
        RealmRewardHint.Text = code == null
            ? BannerlordCoopLauncher.Core.Realm.RealmRewardCode.NoSteam
            : "Paste this code in your “Coodsz Tools Reward” ticket.";
    }

    private void RealmRewardCopy_Click(object sender, RoutedEventArgs e)
    {
        if (RealmRewardCodeBox.Text.Length == 0)
            ShowRealmRewardCode();
        if (RealmRewardCodeBox.Text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(RealmRewardCodeBox.Text);
            RealmRewardHint.Text = "Code copied. Paste it in your “Coodsz Tools Reward” ticket.";
        }
        catch
        {
            RealmRewardHint.Text = "Couldn't copy. Select the code and press Ctrl+C.";
        }
    }
}
