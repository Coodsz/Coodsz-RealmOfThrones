namespace TaomCoop.Core;

/// <summary>Notes for extra mods that change what the TAOM tools' settings do.</summary>
public static class TaomModNotes
{
    public const string BattleSizeResizedId = "BattleSizeResized";

    // BattleSizeResized replaces the game's battle size on each player's game: TAOM.CoopCompat applies the
    // server's size through the top battle size slot, which BattleSizeResized turns into 2040 (or its own
    // MCM size when "Override Native Battlesize" is on). So the server tool's battle size never shows up
    // in battles, and big battles hit the engine's agent limit.
    public const string BattleSizeResized =
        "BattleSizeResized is in this setup. It overrides the server's battle size on each player's game, so the \"Field battle size\" set in the server tool does not apply. Remove it, or turn on \"Override Native Battlesize\" in its MCM settings and set the size there on every player's game.";

    public const string AioCompatId = "TAOM_AIO_Compat";
    public const string LimitlessId = "Limitless";

    // The TAOM AIO Compatibility Patch (github.com/MinimalCash63/TAOM-AIO-Compat) has one MCM switch per mod it fixes.
    // The tools carry its version in a shared setup, but they can't read or compare a mod's MCM settings.
    public const string AioCompat =
        "TAOM AIO Compatibility Patch is in this setup. The host and every player need the same version of it and the same switches in its MCM settings. The tools can't compare those switches.";

    // Its start-up warning (Core\StartupWarnings.cs, v1.5.13) says its Limitless switch does nothing while Coop is loaded.
    public const string AioLimitless =
        "Limitless is in this setup with the TAOM AIO Compatibility Patch. The patch's Limitless switch only works in singleplayer and does nothing in Coop.";

    public static IReadOnlyList<string> For(TaomSetup? setup)
    {
        if (setup == null)
            return [];
        var notes = new List<string>();
        if (setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(BattleSizeResizedId, StringComparison.OrdinalIgnoreCase)))
            notes.Add(BattleSizeResized);
        if (Has(setup, AioCompatId))
        {
            notes.Add(AioCompat);
            if (Has(setup, LimitlessId))
                notes.Add(AioLimitless);
        }

        return notes;
    }

    private static bool Has(TaomSetup setup, string moduleId) =>
        setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
}
