namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>The colour of a mod row chip. The names match Coodsz.Ui.ChipKind, so XAML can bind KindName to it.</summary>
public enum ModChipKind { Ok, Warn, Bad, Info, Mute, Gold }

/// <summary>One chip on a mod row: the English word, its colour and a hover line when the word needs more.</summary>
public sealed record ModChip(string Text, ModChipKind Kind, string Tip = "")
{
    public string KindName => Kind.ToString();
}

/// <summary>
/// The words on the mod row chips of every tool, in one place (build-plan.md 1.3 and wave 3): at most three per row,
/// the status first, then Always on, Coodsz module or Bundled, then the community word.
/// </summary>
public static class ModChips
{
    public const int Max = 3;

    public const string Off = "Off";
    public const string Ready = "Ready";
    public const string ReadyExtraCopy = "Ready, extra copy";
    public const string Missing = "Missing";
    public const string MissingOnWorkshop = "Missing on Workshop";
    public const string MissingOnWorkshopTip = "This mod is not on this PC. Subscribe to it on Steam Workshop. Install mods opens Steam.";
    public const string NeedsUpdate = "Needs update";
    public const string WrongVersion = "Wrong version";
    public const string AlwaysOn = "Always on";
    public const string CoodszModule = "Coodsz module";
    public const string Bundled = "Bundled";
    public const string New = "New";
    // A TaleWorlds module (CoopJoinRules.OfficialModules). The community list doesn't rate those and Coop leaves them
    // out of its join check, so the row says what it is instead of showing no chip at all.
    public const string Official = "Official module";
    public const string OfficialTip = "A TaleWorlds module that comes with Bannerlord. Coop leaves it out of the mod check when players join.";

    /// <summary>The status word of a client launcher row. A missing mod with a Workshop id says Missing on Workshop,
    /// since Install mods opens its Steam page.</summary>
    public static string ClientStatus(ModuleState state, bool enabled, bool extraCopy, string? workshopId)
    {
        if (!enabled)
            return Off;
        if (state == ModuleState.Ready && extraCopy)
            return ReadyExtraCopy;
        return state switch
        {
            ModuleState.Ready => Ready,
            ModuleState.Missing => SharedModList.IsWorkshopId(workshopId) ? MissingOnWorkshop : Missing,
            ModuleState.UpdatePending => NeedsUpdate,
            ModuleState.IdMismatch => "Id mismatch",
            ModuleState.Ambiguous => "Extra copies",
            ModuleState.Unavailable => "Unavailable",
            ModuleState.WrongGame => "Wrong game",
            ModuleState.VersionMismatch => WrongVersion,
            ModuleState.InvalidDefinition => "Invalid",
            _ => state.ToString()
        };
    }

    /// <summary>The colour of a status word: Ready green, Needs update gold, Off grey, a problem red.</summary>
    public static ModChipKind StatusKind(string word) => word switch
    {
        Ready or ReadyExtraCopy => ModChipKind.Ok,
        NeedsUpdate or New => ModChipKind.Gold,
        Off => ModChipKind.Mute,
        _ => ModChipKind.Bad,
    };

    /// <summary>The hover line a status word needs, or "" when the word says it all.</summary>
    public static string StatusTip(string word) => word == MissingOnWorkshop ? MissingOnWorkshopTip : "";

    public static ModChip Status(string word) => new(word, StatusKind(word), StatusTip(word));

    /// <summary>What a module is: Bundled (another author's, shipped with permission), Coodsz module, or Always on for
    /// a module the tool keeps on. Null for a plain mod.</summary>
    public static ModChip? Role(string? moduleId, bool alwaysOn) =>
        CoodszModuleIds.IsBundled(moduleId) ? new ModChip(Bundled, ModChipKind.Info)
        : CoodszModuleIds.IsCoodsz(moduleId) ? new ModChip(CoodszModule, ModChipKind.Gold)
        : alwaysOn ? new ModChip(AlwaysOn, ModChipKind.Mute)
        : moduleId != null && CoopJoinRules.OfficialModules.Contains(moduleId.Trim()) ? new ModChip(Official, ModChipKind.Mute, OfficialTip)
        : null;

    /// <summary>The chips of one row in order (status, what it is, community), at most <see cref="Max"/>.</summary>
    public static IReadOnlyList<ModChip> For(ModChip? status, ModChip? role, ModChip? community = null) =>
        new[] { status, role, community }.OfType<ModChip>().Take(Max).ToList();
}
