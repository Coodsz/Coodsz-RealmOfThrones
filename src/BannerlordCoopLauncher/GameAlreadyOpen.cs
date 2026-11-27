using System.Windows;
using BannerlordCoopLauncher.Core.Launch;

namespace Coodsz.Ui;

/// <summary>
/// Connect with a Bannerlord still running: asks whether to close it, so a game that
/// froze in the background no longer makes Connect look like it does nothing.
/// </summary>
public static class GameAlreadyOpen
{
    /// <summary>
    /// True when no Bannerlord is running any more and Connect can go on. The report gets
    /// each line to show and whether it is a problem.
    /// </summary>
    public static async Task<bool> ClearAsync(Window owner, Action<string, bool> report)
    {
        if (!ClientLaunchGuard.IsGameRunning())
            return true;
        // No is the default, so a stray Enter does not throw away an open game.
        if (UiLanguage.Message(ClientLaunchGuard.AlreadyOpenAsk, ClientLaunchGuard.AlreadyOpenTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, owner, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            report(ClientLaunchGuard.AlreadyOpenKept, true);
            return false;
        }

        if (await ClientLaunchGuard.CloseRunningGamesAsync())
        {
            report(ClientLaunchGuard.ClosedOpenGame, false);
            return true;
        }

        report(ClientLaunchGuard.CouldNotCloseGame, true);
        return false;
    }
}
