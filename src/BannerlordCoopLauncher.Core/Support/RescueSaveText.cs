namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// The Rescue save help and status lines, the same in Coodsz Host, the AD1259 server tool and the
/// TAOM server tool. Kept short and one action per step: players kept pressing Start before Rescue
/// save, or did not know it saves by itself. New text needs tools\ExtractUiText and translations.
/// Start server runs the safe fixes on every start (AutoRescue); the button, on the Logs page, is
/// the full run for support tickets.
/// </summary>
public static class RescueSaveText
{
    public const string Title = "Rescue save";
    public const string HelpTitle = "How Rescue save works";

    public const string Steps =
        "Fixes a save that crashes on load, a frozen map, or lords stuck in a battle that never ends.\n"
        + "Start server already runs the safe fixes every time. Use this when support asks for it, or when Start did not fix the save.\n\n"
        + "1. Press Stop server.\n"
        + "2. Make sure your save is picked.\n"
        + "3. On the Logs page, press Run Rescue save, then OK.\n"
        + "Don't press Start server. Rescue save starts the server for you.\n"
        + "4. Wait about a minute until the log says \"Report\". The game saves only when something was fixed.\n"
        + "5. Press Stop server, then Start server. Done, play normally.\n\n"
        + "Still broken? Press Send my logs and post the code on Discord.";

    public const string Confirm = Steps + "\n\nPress OK to start the rescue now.";

    public const string StopFirst = "Press Stop server first, then Run Rescue save.";
    public const string Running = "Rescue save is running. Wait for the Report line, about a minute.";
    public const string NothingFound = "Rescue save found nothing to fix in this save.";
    public const string Done = "Done! Press Stop server, then Start server to play normally.";
}