using System.Text.Json.Serialization;

namespace TaomCoop.Core;

/// <summary>Where an extra mod loads. The TAOM core never splits: extras go either
/// between the game modules and TAOM, or after TAOM.CoopCompat (the default). BeforeCoop
/// (after LOTRLOME_Armory, before Coop and TAOM.CoopCompat) is only for the mods that
/// TAOM.CoopCompat itself wants loaded before it, such as Alliance.Wargs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExtraSlot
{
    AfterTaom = 0,
    BeforeTaom = 1,
    BeforeCoop = 2,
    BeforeRealmOfThrones = 3
}

/// <summary>One extra mod a player put on a TAOM setup.</summary>
public sealed class TaomExtraMod
{
    public string Name { get; set; } = "";
    public string ModuleId { get; set; } = "";
    /// <summary>The Steam Workshop id, or "0" for a mod that is not on the Workshop.</summary>
    public string WorkshopId { get; set; } = "0";
    public bool Enabled { get; set; } = true;
    public ExtraSlot Slot { get; set; } = ExtraSlot.AfterTaom;

    public TaomExtraMod Copy() => new()
    {
        Name = Name,
        ModuleId = ModuleId,
        WorkshopId = WorkshopId,
        Enabled = Enabled,
        Slot = Slot
    };

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? ModuleId : Name;
}

public sealed record Taom157ModuleRequirement(string Id, string Version);

/// <summary>A named TAOM mod setup: the locked TAOM core plus the player's extra mods.
/// The extras list is kept in load order, the BeforeTaom ones first.</summary>
public sealed class TaomSetup
{
    public string Name { get; set; } = "";
    public bool Locked { get; set; }
    public List<TaomExtraMod> Extras { get; set; } = new();

    // Null means no host list has been imported for Bannerlord 1.5.7.
    public List<Taom157ModuleRequirement>? Line157Requirements { get; set; }
    public bool Line157Exact { get; set; }

    public TaomSetup Copy(string name) => new()
    {
        Name = name,
        Locked = false,
        Extras = Extras.Select(mod => mod.Copy()).ToList(),
        Line157Requirements = Line157Requirements?.ToList(),
        Line157Exact = Line157Exact
    };

    /// <summary>A copy that keeps the name and the lock, for editing before Save mods.</summary>
    public TaomSetup Clone() => new()
    {
        Name = Name,
        Locked = Locked,
        Extras = Extras.Select(mod => mod.Copy()).ToList(),
        Line157Requirements = Line157Requirements?.ToList(),
        Line157Exact = Line157Exact
    };

    public bool SameMods(TaomSetup other)
    {
        if (Line157Exact != other.Line157Exact
            || (Line157Requirements == null) != (other.Line157Requirements == null)
            || !(Line157Requirements ?? []).SequenceEqual(other.Line157Requirements ?? [])
            || Extras.Count != other.Extras.Count)
            return false;
        for (var i = 0; i < Extras.Count; i++)
        {
            var a = Extras[i];
            var b = other.Extras[i];
            if (!a.ModuleId.Equals(b.ModuleId, StringComparison.OrdinalIgnoreCase) || a.Enabled != b.Enabled || a.Slot != b.Slot)
                return false;
        }

        return true;
    }

    [JsonIgnore]
    public IEnumerable<TaomExtraMod> EnabledExtras => Extras.Where(mod => mod.Enabled);

    [JsonIgnore]
    public bool HasEnabledExtras => Extras.Any(mod => mod.Enabled);
}

/// <summary>The setups saved in taom-launcher.json (client) or taom-server.json (host).
/// "Plain TAOM" is always there, always first and always locked: it is exactly the sealed
/// TAOM stack, so a player who never adds a mod sees no change.</summary>
public sealed class TaomSetupBook
{
    public const string PlainName = "Plain TAOM";
    public const string PlusName = TaomPlus.Name;

    public string Active { get; set; } = PlainName;
    public List<TaomSetup> Setups { get; set; } = new();

    public static bool IsPlain(string? name) =>
        name != null && name.Trim().Equals(PlainName, StringComparison.OrdinalIgnoreCase);

    public static bool IsPlus(string? name) =>
        name != null && name.Trim().Equals(PlusName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The line under the server tool's setup dropdown when the picked setup is locked.</summary>
    public static string LockedNote(string setupName) =>
        setupName + " is locked. Copy it to change mods or their order.";

    /// <summary>The tooltip of a TAOM core row on the server tool's Mods page.</summary>
    public const string CoreRowTip = "Part of the TAOM core. It always loads in this order.";

    /// <summary>Adds Plain TAOM when it is missing, keeps it locked with no extras, puts it
    /// first, drops setups without a name or with a repeated name, and fixes the active name.</summary>
    public void Ensure()
    {
        Setups ??= new List<TaomSetup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<TaomSetup>();
        foreach (var setup in Setups)
        {
            if (setup == null || string.IsNullOrWhiteSpace(setup.Name))
                continue;
            setup.Name = setup.Name.Trim();
            setup.Extras ??= new List<TaomExtraMod>();
            if (!seen.Add(setup.Name))
                continue;
            kept.Add(setup);
        }

        var plain = kept.FirstOrDefault(setup => IsPlain(setup.Name)) ?? new TaomSetup();
        kept.Remove(plain);
        plain.Name = PlainName;
        plain.Locked = true;
        plain.Extras.Clear();
        var plus = kept.FirstOrDefault(setup => IsPlus(setup.Name)) ?? new TaomSetup();
        kept.Remove(plus);
        plus.Name = PlusName;
        plus.Locked = true;
        // The built in list wins, but a player's on and off switches on it are not kept either:
        // a locked setup is the same on every PC, so players and hosts match.
        plus.Extras = TaomPlus.Extras();
        kept.Insert(0, plus);
        kept.Insert(0, plain);
        foreach (var setup in kept.Skip(2))
        {
            setup.Locked = false;
            TaomLoadOrder.Normalize(setup);
        }

        Setups = kept;
        if (string.IsNullOrWhiteSpace(Active) || Find(Active) == null)
            Active = PlainName;
        else
            Active = Find(Active)!.Name;
    }

    public TaomSetup? Find(string? name) =>
        name == null ? null : Setups.FirstOrDefault(setup => setup.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public TaomSetup Current()
    {
        Ensure();
        return Find(Active)!;
    }

    public string NextName()
    {
        var number = 2;
        while (Find("Setup " + number) != null)
            number++;
        return "Setup " + number;
    }

    /// <summary>A new setup with only the TAOM core, made active.</summary>
    public TaomSetup Create(string? name = null)
    {
        Ensure();
        var setup = new TaomSetup { Name = CheckNewName(name ?? NextName()) };
        Setups.Add(setup);
        Active = setup.Name;
        return setup;
    }

    /// <summary>A copy of a setup with its extra mods, made active. Plain TAOM can be copied,
    /// which is the quick way to start a setup that adds mods.</summary>
    public TaomSetup CopyOf(string source, string? name = null)
    {
        Ensure();
        var from = Find(source) ?? throw new InvalidOperationException("That setup was not found.");
        var copy = from.Copy(CheckNewName(name ?? NextName()));
        Setups.Add(copy);
        Active = copy.Name;
        return copy;
    }

    public void Rename(string from, string to)
    {
        Ensure();
        var setup = Find(from) ?? throw new InvalidOperationException("That setup was not found.");
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Its name cannot be changed.");
        var name = (to ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Type a setup name first.");
        if (name.Equals(setup.Name, StringComparison.Ordinal))
            return;
        var other = Find(name);
        if (other != null && !ReferenceEquals(other, setup))
            throw new InvalidOperationException("A setup named " + name + " already exists.");
        var wasActive = Active.Equals(setup.Name, StringComparison.OrdinalIgnoreCase);
        setup.Name = name;
        if (wasActive)
            Active = name;
    }

    /// <summary>Deletes an unlocked setup. The active setup falls back to Plain TAOM.</summary>
    public void Delete(string name)
    {
        Ensure();
        var setup = Find(name) ?? throw new InvalidOperationException("That setup was not found.");
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. It cannot be deleted.");
        Setups.Remove(setup);
        if (Active.Equals(setup.Name, StringComparison.OrdinalIgnoreCase))
            Active = PlainName;
    }

    public void Switch(string name)
    {
        Ensure();
        Active = (Find(name) ?? throw new InvalidOperationException("That setup was not found.")).Name;
    }

    private string CheckNewName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Type a setup name first.");
        if (Find(trimmed) != null)
            throw new InvalidOperationException("A setup named " + trimmed + " already exists.");
        return trimmed;
    }
}
