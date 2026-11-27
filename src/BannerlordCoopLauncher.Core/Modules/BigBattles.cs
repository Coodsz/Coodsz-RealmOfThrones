using System.Globalization;
using BannerlordCoopLauncher.Core.Configuration;

namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>What the server's mod list allows for the battle size.</summary>
public enum BigBattleMode
{
    /// <summary>Coop's own range, 200 to 1000.</summary>
    Normal,

    /// <summary>Limitless and Limitless.CoopCompat are both on: 200 to 10000.</summary>
    Unlocked,

    /// <summary>Limitless is on but its Coop patch is not: still 200 to 1000, with a note to add the patch.</summary>
    NeedsPatch,
}

/// <summary>How heavy a battle size is, for the label next to the slider.</summary>
public enum BigBattleTier
{
    Smooth,
    Big,
    Epic,
}

/// <summary>
/// Big battles with Limitless. Coop clamps the battle size to 200..1000 on the server;
/// Limitless.CoopCompat lifts that when the module Limitless is in the mod list, so the
/// server must list both. The RAM numbers come from a live test with two game copies on a
/// Ryzen 7 9800X3D: about 4.3 GB on the campaign map, 5.9 GB at 4000 agents and 7.2 GB at
/// 8400 agents, with horses adding roughly 10 to 15 percent agents on top of soldiers.
/// </summary>
public static class BigBattles
{
    public const string LimitlessId = "Limitless";
    public const string PatchId = "Limitless.CoopCompat";

    /// <summary>The top of the slider with Limitless. The patch itself accepts up to 12000.</summary>
    public const int Max = 10000;

    public const int SmoothUpTo = 2000;
    public const int BigUpTo = 5000;

    /// <summary>Below this much RAM a joining player is warned about a big battles server.</summary>
    public const int ClientWarnBelowGb = 16;

    // RAM estimate: the game on the campaign map, plus a share per 1000 agents in the battle.
    private const double BaseRamGb = 4.5;
    private const double RamGbPer1000Agents = 0.4;
    private const double AgentsPerSoldier = 1.15;

    public static BigBattleMode Detect(IEnumerable<string> enabledModuleIds)
    {
        var ids = enabledModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!ids.Contains(LimitlessId))
            return BigBattleMode.Normal;
        return ids.Contains(PatchId) ? BigBattleMode.Unlocked : BigBattleMode.NeedsPatch;
    }

    public static int MaxFor(BigBattleMode mode) =>
        mode == BigBattleMode.Unlocked ? Max : ModSettingsCatalog.BattleSizeMax;

    public static int Clamp(double size, BigBattleMode mode) =>
        (int)Math.Clamp(Math.Round(size), ModSettingsCatalog.BattleSizeMin, MaxFor(mode));

    /// <summary>A saved battle size above what the mod list allows comes back to the top of the range.</summary>
    public static int ClampBack(int saved, BigBattleMode mode, out bool clamped)
    {
        var size = Clamp(saved, mode);
        clamped = size < saved;
        return size;
    }

    /// <summary>The note's English template; the {0} key is what the language files translate.</summary>
    public const string ClampedNoteTemplate = "Battle size was {0}, but bigger battles need Limitless and Limitless Coop Compat on the server. It is back to 1000.";

    /// <summary>The note in the tool's language, with the saved size filled in after the lookup.</summary>
    public static string ClampedNote(int saved) =>
        string.Format(CultureInfo.InvariantCulture, BannerlordCoopLauncher.Core.Localization.Translator.T(ClampedNoteTemplate), saved);

    public const string NeedsPatchNote = "Add Limitless Coop Compat to unlock bigger battles.";

    public static BigBattleTier Tier(int size) =>
        size <= SmoothUpTo ? BigBattleTier.Smooth : size <= BigUpTo ? BigBattleTier.Big : BigBattleTier.Epic;

    public static string TierText(int size) => Tier(size) switch
    {
        BigBattleTier.Smooth => "Smooth",
        BigBattleTier.Big => "Big battle, strong PC recommended",
        _ => "Epic, expect low FPS",
    };

    /// <summary>About how much RAM each player's game uses at this battle size, in whole GB.</summary>
    public static int EstimatedRamGb(int soldiers)
    {
        var agents = Math.Max(0, soldiers) * AgentsPerSoldier;
        return (int)Math.Round(BaseRamGb + RamGbPer1000Agents * agents / 1000, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A battle size this PC handles well: the game may use about a third of the RAM (the
    /// server, Windows and other apps need the rest), never above the Big battle tier and never
    /// below Coop's own 1000. Rounded down to a step of 100.
    /// </summary>
    public static int SuggestedSize(double ramGb)
    {
        var budget = ramGb / 3 - BaseRamGb;
        if (budget <= 0)
            return ModSettingsCatalog.BattleSizeMax;
        var soldiers = budget / RamGbPer1000Agents * 1000 / AgentsPerSoldier;
        var stepped = (int)(Math.Floor(soldiers / 100) * 100);
        return Math.Clamp(stepped, ModSettingsCatalog.BattleSizeMax, BigUpTo);
    }

    /// <summary>This PC's RAM in GB, or 0 when it cannot be read.</summary>
    public static double ThisPcRamGb()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0 ? bytes / 1073741824.0 : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The warning a joining player sees when the server uses Limitless and this PC may be too
    /// small, or null. With the server's battle size known, the RAM estimate for it is used;
    /// without it, 16 GB. Never a reason to stop the join.
    /// </summary>
    public static string? ClientWarning(IEnumerable<string> serverModuleIds, double ramGb, int? battleSize = null)
    {
        if (ramGb <= 0 || Detect(serverModuleIds) == BigBattleMode.Normal)
            return null;
        var wholeGb = (int)Math.Round(ramGb, MidpointRounding.AwayFromZero);
        var needed = battleSize is { } size ? EstimatedRamGb(size) : ClientWarnBelowGb;
        if (wholeGb >= needed)
            return null;
        return "This server uses big battles. Your PC has " + wholeGb.ToString(CultureInfo.InvariantCulture) + " GB of RAM, so big fights may lag or run out of memory.";
    }

    /// <summary>The line under the unlocked slider.</summary>
    public static string RamText(int size) =>
        "About " + EstimatedRamGb(size).ToString(CultureInfo.InvariantCulture) + " GB of RAM for the game on each player's PC.";

    public static string SuggestedText(int size) =>
        "Suggested for this PC: " + size.ToString(CultureInfo.InvariantCulture);

    public const string AddPatchTip =
        "Turns on Limitless Coop Compat in the server's mod list and saves it. Restart the server after.";

    public const string Tooltip =
        "Total soldiers on the field, both sides. Armies bigger than this still fight, the extra troops come in as reinforcements. Every player's PC shows the whole battle, so pick a size your friends' PCs can handle.";
}
