using BannerlordCoopLauncher.Core.Configuration;

namespace TaomCoop.Core;

/// <summary>The saved campaign speed settings the host tools show in one place.
/// ClientTimeControls is server-config.json's allowClientTimeControls; the Coodsz Host start
/// script holds fast forward at 1x while it is off. Tools without that setting leave it true.</summary>
public sealed record GameSpeedState(bool FastForward, bool BattleSpeedLimit, bool ClientTimeControls = true)
{
    /// <summary>Players can press the fast forward (2x) button on the campaign map.</summary>
    public bool TwoTimesOn => FastForward && ClientTimeControls;
}

/// <summary>The words and choices of the Game speed area on the server page. The tools write
/// the values through their own Save settings code, so there is one way into the files.</summary>
public static class GameSpeed
{
    public const string FastForwardId = ModSettingsCatalog.OptionsSection + ".fastForwardEnabled";
    public const string BattleLimitId = ModSettingsCatalog.OptionsSection + ".speedLimitWhilePlayersInBattle";

    /// <summary>The state from catalogue values (a save's kept settings or mod-config.json).
    /// A missing value is Coop's own default.</summary>
    public static GameSpeedState From(IReadOnlyDictionary<string, object> values, bool clientTimeControls = true) =>
        new(Flag(values, FastForwardId), Flag(values, BattleLimitId), clientTimeControls);

    private static bool Flag(IReadOnlyDictionary<string, object> values, string id) =>
        values.TryGetValue(id, out var value) && value is bool flag
            ? flag
            : ModSettingsCatalog.Find(id)?.Default is true;

    /// <summary>What "Turn on 2x speed" or "Turn off 2x speed" saves. Turning it on also lets
    /// players control campaign time, because fast forward stays at 1x without that.</summary>
    public static GameSpeedState WithTwoTimes(GameSpeedState state, bool on) =>
        on ? state with { FastForward = true, ClientTimeControls = true } : state with { FastForward = false };

    public static GameSpeedState WithBattleLimit(GameSpeedState state, bool on) => state with { BattleSpeedLimit = on };

    public static string SpeedLine(GameSpeedState state) =>
        state.TwoTimesOn ? "2x speed is on. Players can fast forward on the campaign map."
        : !state.FastForward ? "2x speed is off. Fast forward is turned off."
        : "2x speed is off. Allow clients to control campaign time is turned off, so everyone stays at normal speed.";

    public static string BattleLine(GameSpeedState state) =>
        state.BattleSpeedLimit
            ? "Battle speed limit is on. Time goes back to normal speed while any player is in a battle."
            : "Battle speed limit is off. Time can stay fast while players are in a battle.";

    public static string SpeedButton(GameSpeedState state) => state.TwoTimesOn ? "Turn off 2x speed" : "Turn on 2x speed";

    public static string BattleButton(GameSpeedState state) =>
        state.BattleSpeedLimit ? "Turn off battle speed limit" : "Turn on battle speed limit";

    /// <summary>A running server keeps the speed it started with. atStart is what it started
    /// with, or null when this tool does not know.</summary>
    public static bool NeedsRestart(bool running, GameSpeedState? atStart, GameSpeedState saved) =>
        running && atStart is { } started && started != saved;

    public static string RestartLine(bool running, GameSpeedState? atStart, GameSpeedState saved) =>
        NeedsRestart(running, atStart, saved)
            ? "Saved. The running server still uses the old speed. Restart the server so the new speed applies."
            : running
                ? "These buttons save right away. A running server only uses the new speed after a restart."
                : "These buttons save right away. The new speed applies the next time the server starts.";
}
