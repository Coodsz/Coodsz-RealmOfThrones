using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>The setups saved in rot-launcher.json (client) or rot-server.json (host), in the TAOM
/// setup model. "Plain Realm of Thrones" is always there, always first and always locked: it is
/// exactly the sealed Realm of Thrones stack and nothing else. Custom setups are made with New setup
/// or Copy and carry the extras.</summary>
public sealed class RotSetupBook
{
    public const string CoreName = "Plain Realm of Thrones";

    public string Active { get; set; } = CoreName;
    public List<TaomSetup> Setups { get; set; } = new();

    /// <summary>True for the locked setup the tools bring.</summary>
    public static bool IsBuiltIn(string? name) =>
        name != null && name.Trim().Equals(CoreName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The line under the server tool's setup dropdown when the picked setup is locked.</summary>
    public static string LockedNote(string setupName) =>
        setupName + " is locked. Copy it to change mods or their order.";

    /// <summary>The tooltip of a Realm of Thrones core row on the server tool's Mods page.</summary>
    public const string CoreRowTip = "Part of the Realm of Thrones core. It always loads in this order.";

    /// <summary>Adds the locked setup when it is missing, keeps it without extras, puts it first,
    /// drops setups without a name or with a repeated name, and fixes the active name.</summary>
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

        var core = kept.FirstOrDefault(setup => IsBuiltIn(setup.Name)) ?? new TaomSetup();
        kept.Remove(core);
        core.Name = CoreName;
        // A locked setup is the same on every PC, so players and hosts match.
        core.Locked = true;
        core.Extras.Clear();
        kept.Insert(0, core);
        // Normalize trims each extra id and drops a repeated one, so a hand edited file cannot list a mod twice.
        foreach (var setup in kept.Skip(1))
        {
            setup.Locked = false;
            RotLoadOrder.Normalize(setup);
        }

        Setups = kept;
        var active = string.IsNullOrWhiteSpace(Active) ? null : Find(Active);
        Active = active?.Name ?? CoreName;
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

    /// <summary>A new setup with only the Realm of Thrones core, made active.</summary>
    public TaomSetup Create(string? name = null)
    {
        Ensure();
        var setup = new TaomSetup { Name = CheckNewName(name ?? NextName()) };
        Setups.Add(setup);
        Active = setup.Name;
        return setup;
    }

    /// <summary>A copy of a setup with its extra mods, made active. The locked setup can be
    /// copied, which is the quick way to start a setup that adds mods.</summary>
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

    /// <summary>Deletes an unlocked setup. The active setup falls back to the locked setup.</summary>
    public void Delete(string name)
    {
        Ensure();
        var setup = Find(name) ?? throw new InvalidOperationException("That setup was not found.");
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. It cannot be deleted.");
        Setups.Remove(setup);
        if (Active.Equals(setup.Name, StringComparison.OrdinalIgnoreCase))
            Active = CoreName;
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
