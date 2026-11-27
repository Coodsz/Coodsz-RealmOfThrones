using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>
/// The locked Realm of Thrones core and the rules for extra mods in it.
///
/// The required modules keep their relative order. Extras can go on either side of the four
/// Realm of Thrones modules, with Better Exception Window placed after Harmony when enabled.

/// </summary>
public static class RotLoadOrder
{
    public const ExtraSlot SlotAfterRot = ExtraSlot.AfterTaom;
    public const ExtraSlot SlotBeforeRot = ExtraSlot.BeforeRealmOfThrones;

    public static bool LoadsAfterHarmony(string id) =>
        id.Equals("BetterExceptionWindow", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> NotForCampaign = new(StringComparer.OrdinalIgnoreCase)
    {
        "Multiplayer",
        "DedicatedCustomServer"
    };

    /// <summary>The official modules of Bannerlord 1.4.8 (ModuleType Official or OfficialOptional in the
    /// game's Modules folder). The ones outside the core are never extras, so none lands in the server's
    /// "modules" list.</summary>
    public static IReadOnlyCollection<string> OfficialModules { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "NavalDLC", "BirthAndDeath", "FastMode", "Multiplayer"
    };

    /// <summary>The TAOM core modules. TAOM is another overhaul and never loads with Realm of Thrones.</summary>
    public static IReadOnlyCollection<string> TaomCoreIds { get; } =
        TaomStack.ContentVersions.Keys.Append(TaomStack.CompatModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The Burning Empires core modules (BeStack in BurningEmpires.Core, which this library does not load).
    public static IReadOnlyCollection<string> BeCoreIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BurningEmpires", "BurningEmpires_Elephants", "BurningEmpires.CoopCompat" };

    public static bool IsTaomCore(string? moduleId) =>
        moduleId != null && TaomCoreIds.Contains(moduleId.Trim());

    public static bool IsBeCore(string? moduleId) =>
        moduleId != null && BeCoreIds.Contains(moduleId.Trim());

    /// <summary>The overhaul a core module of TAOM or Burning Empires belongs to, or null.</summary>
    public static string? OtherOverhaul(string? moduleId) =>
        IsTaomCore(moduleId) ? "TAOM" : IsBeCore(moduleId) ? "Burning Empires" : null;

    // The Chaos in Calradia mod and its compat module, which have tools of their own. No other tool's list has them.
    public static IReadOnlyCollection<string> ChaosIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ChaosInCalradia", "ChaosInCalradia.CoopCompat" };

    /// <summary>The mod with tools of its own (Chaos in Calradia) a module belongs to, or null.</summary>
    public static string? OtherTools(string? moduleId) =>
        moduleId != null && ChaosIds.Contains(moduleId.Trim()) ? "Chaos in Calradia" : null;

    /// <summary>The other mod a module belongs to, an overhaul or a mod with tools of its own, or null.</summary>
    public static string? OtherGame(string? moduleId) => OtherOverhaul(moduleId) ?? OtherTools(moduleId);

    /// <summary>True for the modules of the Realm of Thrones core, client or server side.</summary>
    public static bool IsCore(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId)
        && (RotStack.ClientModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase)
            || RotStack.ServerModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>The Realm of Thrones module this id is a second copy of (ROT_Core, ROT-Map, rot content),
    /// or null. Ids are compared on their letters and digits only, without case.</summary>
    public static string? SecondCopyOf(string? moduleId)
    {
        var key = Key(moduleId);
        return key.Length == 0 ? null : RotStack.RotModules.FirstOrDefault(id => Key(id) == key);
    }

    private static string Key(string? id) =>
        new string((id ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Why this module cannot be an extra mod on a Realm of Thrones setup, or null when it can.</summary>
    public static string? RefusalReason(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        if (!id.Any(char.IsLetterOrDigit) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('*') || id.Contains('"'))
            return "That module id cannot be loaded.";
        if (IsCore(id))
            return id + " is part of the Realm of Thrones core. It is always loaded.";
        if (NotForCampaign.Contains(id))
            return id + " is not a campaign module.";
        if (OfficialModules.Contains(id))
            return id + " comes with Bannerlord. It cannot be added as an extra mod.";
        if (RotStack.OtherCoopModuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            return id + " is an old Coop copy. Two Coop copies crash the game.";
        var overhaul = OtherOverhaul(id);
        if (overhaul != null)
            return id + " is part of " + overhaul + ", another overhaul. It does not load together with Realm of Thrones.";
        if (OtherTools(id) is { } tools)
            return id + " is part of " + tools + ", which has tools of its own. It does not load together with Realm of Thrones.";
        if (SecondCopyOf(id) is { } rot)
            return id + " is a second copy of " + rot + ". The Realm of Thrones core already loads it.";
        if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            return id + " is a server or launcher module. It is not added by hand.";
        return null;
    }

    /// <summary>Trims ids, drops duplicates and maps legacy slots to their original placement.</summary>

    public static void Normalize(TaomSetup setup)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<TaomExtraMod>();
        foreach (var mod in setup.Extras)
        {
            if (mod == null)
                continue;
            mod.ModuleId = (mod.ModuleId ?? "").Trim();
            if (!seen.Add(mod.ModuleId))
                continue;
            if (mod.Slot != SlotBeforeRot || LoadsAfterHarmony(mod.ModuleId))
                mod.Slot = SlotAfterRot;
            kept.Add(mod);
        }

        setup.Extras = kept.OrderBy(mod => mod.Slot == SlotBeforeRot ? 0 : 1).ToList();
    }

    public static IReadOnlyList<string> ClientOrder(TaomSetup? setup) =>
        Build(setup, RotStack.ClientModules);

    public static IReadOnlyList<string> ServerOrder(TaomSetup? setup) =>
        Build(setup, RotStack.ServerModules);

    public static IReadOnlyList<TaomOrderRow> ClientRows(TaomSetup setup) =>
        Rows(setup, RotStack.ClientModules);

    public static IReadOnlyList<TaomOrderRow> ServerRows(TaomSetup setup) =>
        Rows(setup, RotStack.ServerModules);

    private static IReadOnlyList<TaomOrderRow> Rows(TaomSetup? setup, IReadOnlyList<string> core)
    {
        var extras = new List<TaomOrderRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (setup != null)
        {
            for (var index = 0; index < setup.Extras.Count; index++)
            {
                var id = setup.Extras[index].ModuleId.Trim();
                if (RefusalReason(id) == null && seen.Add(id))
                    extras.Add(new TaomOrderRow(id, index));
            }
        }

        var rows = new List<TaomOrderRow>();
        foreach (var id in core)
        {
            if (id == RotStack.CoreModuleId)
                rows.AddRange(extras.Where(row => !LoadsAfterHarmony(row.ModuleId) && setup!.Extras[row.ExtraIndex].Slot == SlotBeforeRot));
            if (id == RotStack.CompatModuleId)
                rows.AddRange(extras.Where(row => !LoadsAfterHarmony(row.ModuleId) && setup!.Extras[row.ExtraIndex].Slot != SlotBeforeRot));

            rows.Add(new TaomOrderRow(id, -1));
            if (id == "Bannerlord.Harmony")
                rows.AddRange(extras.Where(row => LoadsAfterHarmony(row.ModuleId)));
        }
        return rows;
    }

    /// <summary>The client order plus the client add-ons (the join password module) last.</summary>
    public static IReadOnlyList<string> LaunchModules(TaomSetup? setup, bool joinPassword)
    {
        var order = ClientOrder(setup).ToList();
        if (joinPassword)
            order.Add(ClientAddOns.JoinPasswordId);
        return order;
    }

    private static IReadOnlyList<string> Build(TaomSetup? setup, IReadOnlyList<string> core)
    {
        var order = Rows(setup, core)
            .Where(row => row.IsCore || setup!.Extras[row.ExtraIndex].Enabled)
            .Select(row => row.ModuleId)
            .ToList();
        if (CoreProblem(order, core) is { } problem)
            throw new InvalidOperationException(problem);
        return order;
    }

    public static string? CoreProblem(IReadOnlyList<string> order, IReadOnlyList<string>? core = null)
    {
        core ??= RotStack.ClientModules;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
        {
            if (!seen.Add(id))
                return id + " is in the module list twice.";
        }

        if (!order.Where(id => core.Contains(id, StringComparer.OrdinalIgnoreCase)).SequenceEqual(core, StringComparer.OrdinalIgnoreCase))
            return "The Realm of Thrones core has to load first, in its order.";

        var rotStart = order.ToList().FindIndex(RotStack.IsRotModule);
        if (!order.Skip(rotStart).Take(RotStack.RotModules.Count).SequenceEqual(RotStack.RotModules, StringComparer.OrdinalIgnoreCase))
            return "The Realm of Thrones core has to load first, in its order.";

        if (order.Count == 0 || !order[^1].Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return "Realm of Thrones Coop Compat has to load last.";
        return null;
    }

    // ----- Editing. A locked setup refuses every change. -----

    /// <summary>Adds an extra mod at the end of the extras. Returns null when it was added, or why not.</summary>
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
            WorkshopId = SharedModList.IsWorkshopId(workshopId) ? workshopId!.Trim() : "0",
            Enabled = true,
            Slot = SlotAfterRot
        });
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
        Find(setup, moduleId).Enabled = enabled;
    }

    public static bool Move(TaomSetup setup, string moduleId, int delta)
    {
        Unlocked(setup);
        if (delta == 0 || LoadsAfterHarmony(moduleId))
            return false;

        var rows = ClientRows(setup)
            .Where(row => !LoadsAfterHarmony(row.ModuleId) && (!row.IsCore || row.ModuleId == RotStack.CoreModuleId))
            .Select(row => row.ModuleId).ToList();
        var index = rows.FindIndex(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        var target = index + Math.Sign(delta);
        if (index < 0 || target < 0 || target >= rows.Count)
            return false;
        return moduleId == RotStack.CoreModuleId
            ? MoveNextTo(setup, rows[target], moduleId, after: delta < 0)
            : MoveNextTo(setup, moduleId, rows[target], after: delta > 0);
    }

    public static bool MoveNextTo(TaomSetup setup, string moduleId, string targetId, bool after)
    {
        Unlocked(setup);
        if (LoadsAfterHarmony(moduleId) || LoadsAfterHarmony(targetId)
            || moduleId.Equals(targetId, StringComparison.OrdinalIgnoreCase))
            return false;

        var mod = Find(setup, moduleId);
        var target = setup.Extras.FirstOrDefault(extra => extra.ModuleId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (target == null && !RotStack.IsRotModule(targetId) && targetId != RotStack.CompatModuleId)
            return false;

        setup.Extras.Remove(mod);
        if (target != null)
        {
            mod.Slot = target.Slot;
            setup.Extras.Insert(setup.Extras.IndexOf(target) + (after ? 1 : 0), mod);
        }
        else if (RotStack.IsRotModule(targetId) && !after)
        {
            mod.Slot = SlotBeforeRot;
            setup.Extras.Add(mod);
        }
        else
        {
            mod.Slot = SlotAfterRot;
            setup.Extras.Insert(targetId == RotStack.CompatModuleId ? setup.Extras.Count : 0, mod);
        }

        Normalize(setup);
        return true;
    }



    public static string? MoveProblem(TaomSetup original, TaomSetup edited, IReadOnlyDictionary<string, TaomModFact> facts)
    {
        var existing = RotModChecks.ReversedOrder(original, facts).ToHashSet();
        return RotModChecks.ReversedOrder(edited, facts).Except(existing)
            .Select(problem => RotModChecks.OrderMessage(edited, problem))
            .FirstOrDefault();
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
