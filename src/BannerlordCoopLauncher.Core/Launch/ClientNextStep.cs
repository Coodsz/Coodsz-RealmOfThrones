namespace BannerlordCoopLauncher.Core.Launch;

/// <summary>The one gold button of the client's Play page (ux-plan.md 2.4).</summary>
public enum NextStep { FindBannerlord, CheckMods, InstallMods, Connect, FillJoin, FixMods, Searching }

/// <summary>
/// What the Coop client's Play page asks for now, in the order of truth: the game first, then the mods, then the join.
/// The words are English; the window translates them.
/// </summary>
public static class ClientNextStep
{
    /// <summary>The step: Looking for Bannerlord while the game search runs, Find Bannerlord with no game, Check mods before
    /// the first check, Connect when it is ready (also when Connect gets the missing mods first), Install mods while a mod
    /// that is on needs it, Fix mods while the mods still block Connect for another reason (no Coop in the list, a mod that
    /// is off and not Ready: ModuleResolver.ConnectBlockReason), else fill in the Join box.</summary>
    public static NextStep For(bool gameMissing, bool modsChecked, bool connectReady, int attention, string? blockReason = null, bool searching = false)
    {
        if (searching)
            return NextStep.Searching;
        if (gameMissing)
            return NextStep.FindBannerlord;
        if (!modsChecked)
            return NextStep.CheckMods;
        if (connectReady)
            return NextStep.Connect;
        if (attention > 0)
            return NextStep.InstallMods;
        return string.IsNullOrEmpty(blockReason) ? NextStep.FillJoin : NextStep.FixMods;
    }

    /// <summary>The line over the buttons. Fix mods shows the reason Connect is off, word for word.</summary>
    public static string Title(NextStep step, int attention, string? blockReason = null) => step switch
    {
        NextStep.Searching => Searching,
        NextStep.FixMods => blockReason ?? "",
        NextStep.FindBannerlord => "Find Bannerlord first.",
        NextStep.CheckMods => "Check your mods.",
        NextStep.InstallMods => attention == 1 ? "1 mod needs attention." : attention + " mods need attention.",
        NextStep.Connect => "Ready to join.",
        _ => "Fill in the Join box.",
    };

    /// <summary>The words of the header pill: Joining, In game, Refused or Game open while a join runs, else Ready or Not ready.</summary>
    public static string PillText(bool connectReady, string? joinState) => joinState ?? (connectReady ? Ready : NotReady);

    public const string Searching = "Looking for Bannerlord...";
    public const string NotReady = "Not ready";
    public const string Ready = "Ready";
    public const string Joining = "Joining";
    public const string InGame = "In game";
    public const string Refused = "Refused";
    /// <summary>The join watch ended with the game open and Coop's client log empty, so no result is known (ClientJoinLog).</summary>
    public const string GameOpen = "Game open";
}
