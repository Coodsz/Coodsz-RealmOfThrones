using BannerlordCoopLauncher.Core.Launch;

namespace TaomCoop.Core;

/// <summary>
/// The locked TAOM core and the rules for extra mods around it.
///
/// The core is TaomStack.ClientModules (TaomStack.ServerModules on the host) in its sealed
/// order. It is split in blocks that always stay whole:
///   game block: TAOM.Dependencies, Native, SandBoxCore, Sandbox, StoryMode, CustomBattle
///   content block: TAOM, TAOM_Map, LOTRLOME_Armory
///   Coop block: Coop, TAOM.CoopCompat
/// Extras load in one of three places: between the game block and TAOM (before TAOM), after
/// TAOM.CoopCompat (after TAOM, the default), or between the content block and Coop, which is
/// only for mods that the TAOM.CoopCompat SubModule.xml wants loaded before it. Nothing goes
/// before TAOM.Dependencies (so every extra loads after it, as its ModulesToLoadAfterThis
/// list asks), nothing goes inside a block, so Coop always stays right before TAOM.CoopCompat.
/// Client add-ons (DedicatedServer.*) still load last, after every extra.
/// </summary>
/// <summary>One row of the Mods page: a core module (ExtraIndex -1) or setup.Extras[ExtraIndex].</summary>
public sealed record TaomOrderRow(string ModuleId, int ExtraIndex)
{
    public bool IsCore => ExtraIndex < 0;
}

public static class TaomLoadOrder
{
    public const string FirstTaomModule = "TAOM";

    public static IReadOnlyList<string> GameBlock { get; } =
        TaomStack.ClientModules.TakeWhile(id => id != FirstTaomModule).ToArray();

    public static IReadOnlyList<string> TaomBlock { get; } =
        TaomStack.ClientModules.SkipWhile(id => id != FirstTaomModule).ToArray();

    public static IReadOnlyList<string> ContentBlock { get; } =
        TaomBlock.TakeWhile(id => id != TaomStack.CoopModuleId).ToArray();

    public static IReadOnlyList<string> CoopBlock { get; } =
        TaomBlock.SkipWhile(id => id != TaomStack.CoopModuleId).ToArray();

    // TAOM.CoopCompat v0.3.25 SubModule.xml: DependedModuleMetadata Alliance.Wargs order="LoadBeforeThis".
    // Only such mods may sit between LOTRLOME_Armory and Coop.
    private static readonly HashSet<string> LoadBeforeCompat = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alliance.Wargs"
    };

    public static bool MayLoadBeforeCoop(string? moduleId) =>
        moduleId != null && LoadBeforeCompat.Contains(moduleId.Trim());

    // Mods TAOM players found work only at the very bottom of the load order. Auto sort puts them last.
    public static readonly IReadOnlySet<string> LoadsLast = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RTSCamera", "RTSCamera.CommandSystem" };

    /// <summary>0 for most mods; 1 for RTS Camera and its Command System, which TAOM players found work
    /// only at the very bottom; 2 for the TAOM Xorberax patch, which goes last, after Xorberax's Legacy.</summary>
    public static int LastRank(string id, string? name = null)
    {
        if (IsXorberaxTaomPatch(id, name))
            return 2;
        return LoadsLast.Contains(id) ? 1 : 0;
    }

    public static bool IsXorberaxTaomPatch(string id, string? name)
    {
        var text = id + " " + name;
        return text.Contains("Xorberax", StringComparison.OrdinalIgnoreCase)
            && !id.Equals("XorberaxLegacy", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("TAOM", StringComparison.OrdinalIgnoreCase) || text.Contains("patch", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The TAOM Dependency Patch (Nexus 13196), which tells the game TAOM has Harmony, ButterLib,
    /// UIExtenderEx and MCM. Its module id is not known here, so it is found by its id or name.</summary>
    public static bool IsTaomDependencyPatch(string id, string? name)
    {
        var text = (id + " " + name).Replace(".", " ").Replace("_", " ");
        return text.Contains("TAOM", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("Dependency Patch", StringComparison.OrdinalIgnoreCase) || text.Contains("DependencyPatch", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly ExtraSlot[] SlotOrder = [ExtraSlot.BeforeTaom, ExtraSlot.BeforeCoop, ExtraSlot.AfterTaom];

    // TAOM.Dependencies ships the whole BUTR stack itself (0Harmony, UIExtenderEx, ButterLib, MCMv5 and
    // the MBOptionScreen loader, see its SubModule.xml). A second copy of any of them breaks the game.
    private static readonly HashSet<string> Inside = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bannerlord.Harmony",
        "Bannerlord.ButterLib",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.MBOptionScreen"
    };

    public static IReadOnlyCollection<string> InsideTaomDependencies => Inside;

    private static readonly HashSet<string> NotForCampaign = new(StringComparer.OrdinalIgnoreCase)
    {
        "Multiplayer",
        "DedicatedCustomServer"
    };

    /// <summary>True for the modules TAOM and the TAOM Coop kit install (TAOM.Dependencies, TAOM, TAOM_Map,
    /// LOTRLOME_Armory, TAOM.CoopCompat, see TaomStack.ContentVersions and TaomArchive), the game modules
    /// and Coop. Other mods whose id starts with TAOM, such as the TAOM developer's Nexus mods
    /// (TAOM_PlayerVoices, TAOM_CheatPanel, the TAOM_Standalone_Compat_* patches), are ordinary extras.</summary>
    public static bool IsCore(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId)
        && (TaomStack.ClientModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase)
            || TaomStack.ServerModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Why this module cannot be an extra mod on a TAOM setup, or null when it can.</summary>
    public static string? RefusalReason(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0 || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('*') || id.Contains('"'))
            return "That module id cannot be loaded.";
        if (IsCore(id))
            return id + " is part of the TAOM core. It is always loaded.";
        if (id.Equals(TaomStack.LegacyCoopModuleId, StringComparison.OrdinalIgnoreCase))
            return id + " is an old Coop copy. Two Coop copies crash TAOM.CoopCompat.";
        if (Inside.Contains(id))
            return id + " is already inside TAOM Dependencies. A second copy crashes the game.";
        if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            return id + " is a server or launcher module. It is not added by hand.";
        if (NotForCampaign.Contains(id))
            return id + " is not a campaign module.";
        return null;
    }

    /// <summary>Puts the extras in slot order (before TAOM, before Coop, after TAOM), keeping the
    /// order inside each slot. A mod that may not load before Coop goes after TAOM.</summary>
    public static void Normalize(TaomSetup setup)
    {
        foreach (var mod in setup.Extras)
            mod.Slot = SlotOf(mod);

        setup.Extras = SlotOrder.SelectMany(slot => setup.Extras.Where(mod => mod.Slot == slot)).ToList();
    }

    /// <summary>The module order the client launches with: the core in its sealed order with the
    /// enabled extras in their slots. Plain TAOM gives exactly TaomStack.ClientModules.</summary>
    public static IReadOnlyList<string> ClientOrder(TaomSetup? setup) =>
        Build(setup, TaomStack.ClientModules, serverTail: null);

    /// <summary>The order the dedicated server loads for this setup: the server core with the
    /// same extras, and DedicatedServer.Windows last. Plain TAOM gives TaomStack.ServerModules.</summary>
    public static IReadOnlyList<string> ServerOrder(TaomSetup? setup)
    {
        var core = TaomStack.ServerModules.Where(id => !id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase)).ToArray();
        var tail = TaomStack.ServerModules.Where(id => id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase)).ToArray();
        return Build(setup, core, tail);
    }

    /// <summary>The rows the server tool's Mods page shows, in the order ServerOrder loads them:
    /// the server core with every extra in its slot, the ones turned off too, so the host can turn
    /// them back on. ExtraIndex points into setup.Extras, or is -1 for a core module.</summary>
    public static IReadOnlyList<TaomOrderRow> ServerRows(TaomSetup setup)
    {
        var core = TaomStack.ServerModules.Where(id => !id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase)).ToList();
        var tail = TaomStack.ServerModules.Where(id => id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase));
        var rows = new List<TaomOrderRow>();
        IEnumerable<TaomOrderRow> Core(IEnumerable<string> ids) => ids.Select(id => new TaomOrderRow(id, -1));
        IEnumerable<TaomOrderRow> In(ExtraSlot slot) => setup.Extras
            .Select((mod, index) => (mod, index))
            .Where(pair => SlotOf(pair.mod) == slot)
            .Select(pair => new TaomOrderRow(pair.mod.ModuleId.Trim(), pair.index));
        rows.AddRange(Core(core.TakeWhile(id => id != FirstTaomModule)));
        rows.AddRange(In(ExtraSlot.BeforeTaom));
        rows.AddRange(Core(core.SkipWhile(id => id != FirstTaomModule).TakeWhile(id => id != TaomStack.CoopModuleId)));
        rows.AddRange(In(ExtraSlot.BeforeCoop));
        rows.AddRange(Core(core.SkipWhile(id => id != TaomStack.CoopModuleId)));
        rows.AddRange(In(ExtraSlot.AfterTaom));
        rows.AddRange(Core(tail));
        return rows;
    }

    private static ExtraSlot SlotOf(TaomExtraMod mod) =>
        !SlotOrder.Contains(mod.Slot) || (mod.Slot == ExtraSlot.BeforeCoop && !MayLoadBeforeCoop(mod.ModuleId)) ? ExtraSlot.AfterTaom : mod.Slot;

    /// <summary>The client order plus the client add-ons (the join password module) last.</summary>
    public static IReadOnlyList<string> LaunchModules(TaomSetup? setup, bool joinPassword, bool includeCompat = true)
    {
        var order = ClientOrder(setup).ToList();
        if (!includeCompat)
            order.Remove(TaomStack.CompatModuleId);
        if (joinPassword)
            order.Add(ClientAddOns.JoinPasswordId);
        return order;
    }

    private static IReadOnlyList<string> Build(TaomSetup? setup, IReadOnlyList<string> core, IReadOnlyList<string>? serverTail)
    {
        var game = core.TakeWhile(id => id != FirstTaomModule).ToList();
        var content = core.SkipWhile(id => id != FirstTaomModule).TakeWhile(id => id != TaomStack.CoopModuleId).ToList();
        var coop = core.SkipWhile(id => id != TaomStack.CoopModuleId).ToList();
        var order = new List<string>(game);
        var extras = Usable(setup);
        IEnumerable<string> In(ExtraSlot slot) => extras.Where(mod => mod.Slot == slot).Select(mod => mod.ModuleId.Trim());
        order.AddRange(In(ExtraSlot.BeforeTaom));
        order.AddRange(content);
        order.AddRange(In(ExtraSlot.BeforeCoop));
        order.AddRange(coop);
        order.AddRange(In(ExtraSlot.AfterTaom));
        if (serverTail != null)
            order.AddRange(serverTail);
        var problem = CoreProblem(order, core);
        if (problem != null)
            throw new InvalidOperationException(problem);
        return order;
    }

    private static List<TaomExtraMod> Usable(TaomSetup? setup)
    {
        var list = new List<TaomExtraMod>();
        if (setup == null || (setup.Locked && !TaomSetupBook.IsPlus(setup.Name)))
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in setup.Extras)
        {
            if (!mod.Enabled || RefusalReason(mod.ModuleId) != null || !seen.Add(mod.ModuleId.Trim()))
                continue;
            var copy = mod.Copy();
            if (copy.Slot == ExtraSlot.BeforeCoop && !MayLoadBeforeCoop(copy.ModuleId))
                copy.Slot = ExtraSlot.AfterTaom;
            list.Add(copy);
        }

        return list;
    }

    /// <summary>Null when the order keeps the core whole: the game block first and unbroken, the
    /// content block unbroken after it, Coop right before TAOM.CoopCompat after the content block
    /// with only the mods CoopCompat wants before it in between, no module twice. Otherwise a
    /// plain sentence that says what is wrong.</summary>
    public static string? CoreProblem(IReadOnlyList<string> order, IReadOnlyList<string>? core = null)
    {
        core ??= TaomStack.ClientModules;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
        {
            if (!seen.Add(id))
                return id + " is in the module list twice.";
        }

        var game = core.TakeWhile(id => id != FirstTaomModule).ToList();
        var content = core.SkipWhile(id => id != FirstTaomModule).TakeWhile(id => id != TaomStack.CoopModuleId).ToList();
        for (var i = 0; i < game.Count; i++)
        {
            if (i >= order.Count || !order[i].Equals(game[i], StringComparison.OrdinalIgnoreCase))
                return "The game modules have to load first, in the TAOM order.";
        }

        var list = order.ToList();
        var start = list.FindIndex(id => id.Equals(FirstTaomModule, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return "TAOM is missing from the module list.";
        for (var i = 0; i < content.Count; i++)
        {
            if (start + i >= order.Count || !order[start + i].Equals(content[i], StringComparison.OrdinalIgnoreCase))
                return "The TAOM modules have to stay together, in the TAOM order.";
        }

        var coop = list.FindIndex(id => id.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase));
        if (coop < start + content.Count)
            return "The TAOM modules have to stay together, in the TAOM order.";
        for (var i = start + content.Count; i < coop; i++)
        {
            if (!MayLoadBeforeCoop(order[i]))
                return "The TAOM modules have to stay together, in the TAOM order.";
        }

        return TaomStack.CoopIsAdjacent(order) ? null : "Coop has to load right before TAOM.CoopCompat.";
    }

    // ----- Editing. A locked setup refuses every change. -----

    /// <summary>Adds an extra mod after TAOM. Returns null when it was added, or why not.</summary>
    public static string? Add(TaomSetup setup, string name, string moduleId, string? workshopId)
    {
        Unlocked(setup);
        var id = (moduleId ?? "").Trim();
        var refusal = RefusalReason(id);
        if (refusal != null)
            return refusal;
        if (setup.Extras.Any(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            return (string.IsNullOrWhiteSpace(name) ? id : name.Trim()) + " is already on this setup.";
        setup.Extras.Add(new TaomExtraMod
        {
            Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
            ModuleId = id,
            WorkshopId = BannerlordCoopLauncher.Core.Modules.SharedModList.IsWorkshopId(workshopId) ? workshopId!.Trim() : "0",
            Enabled = true,
            Slot = ExtraSlot.AfterTaom
        });
        Normalize(setup);
        return null;
    }

    public static bool Remove(TaomSetup setup, string moduleId)
    {
        Unlocked(setup);
        return setup.Extras.RemoveAll(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public static void SetEnabled(TaomSetup setup, string moduleId, bool enabled)
    {
        Unlocked(setup);
        var mod = Find(setup, moduleId);
        mod.Enabled = enabled;
    }

    /// <summary>Moves an extra one place up (-1) or down (+1) in the load order. Moving up past
    /// the TAOM block puts it before TAOM; moving down past it puts it after TAOM. A mod that
    /// TAOM.CoopCompat wants before it also stops between LOTRLOME_Armory and Coop. Returns false
    /// when it is already at the top or the bottom.</summary>
    public static bool Move(TaomSetup setup, string moduleId, int delta)
    {
        Unlocked(setup);
        Normalize(setup);
        if (delta == 0)
            return false;
        var mod = Find(setup, moduleId);
        var index = setup.Extras.IndexOf(mod);
        var neighbour = index + Math.Sign(delta);
        if (neighbour >= 0 && neighbour < setup.Extras.Count && setup.Extras[neighbour].Slot == mod.Slot)
        {
            setup.Extras.RemoveAt(index);
            setup.Extras.Insert(neighbour, mod);
            return true;
        }

        var slots = SlotOrder.Where(slot => slot != ExtraSlot.BeforeCoop || MayLoadBeforeCoop(mod.ModuleId)).ToList();
        var next = slots.IndexOf(mod.Slot) + Math.Sign(delta);
        if (next < 0 || next >= slots.Count)
            return false;
        mod.Slot = slots[next];
        Normalize(setup);
        return true;
    }

    /// <summary>Puts an extra at a place in a slot (used by drag and drop and Auto sort). The
    /// index counts the other extras in that slot; a big index means the end of the slot.</summary>
    public static void MoveTo(TaomSetup setup, string moduleId, ExtraSlot slot, int index)
    {
        Unlocked(setup);
        var mod = Find(setup, moduleId);
        setup.Extras.Remove(mod);
        if (slot == ExtraSlot.BeforeCoop && !MayLoadBeforeCoop(mod.ModuleId))
            slot = ExtraSlot.AfterTaom;
        mod.Slot = slot;
        Normalize(setup);
        var slotStart = SlotOrder.TakeWhile(item => item != slot).Sum(item => setup.Extras.Count(extra => extra.Slot == item));
        var slotCount = setup.Extras.Count(item => item.Slot == slot);
        var at = slotStart + Math.Clamp(index, 0, slotCount);
        setup.Extras.Insert(at, mod);
    }

    private static TaomExtraMod Find(TaomSetup setup, string moduleId) =>
        setup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(moduleId + " is not on this setup.");

    private static void Unlocked(TaomSetup setup)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
    }
}
