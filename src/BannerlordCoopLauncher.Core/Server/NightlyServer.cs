using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Configuration;

namespace BannerlordCoopLauncher.Core.Server;

/// <summary>How a tool starts the Coop nightly dedicated server: the exe, its arguments, its working
/// folder, the environment it sets, the variables it removes from what the process would inherit, and the
/// encoding its console input has to be written in.</summary>
public sealed record NightlyServerStart(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    Encoding InputEncoding,
    IReadOnlyList<string> RemoveVariables)
{
    /// <summary>The arguments as one command line, each quoted when it has a space.</summary>
    public string ArgumentLine => string.Join(" ", Arguments.Select(argument => argument.Contains(' ') ? "\"" + argument + "\"" : argument));
}

public enum SeedResult
{
    /// <summary>The save is already there and was not touched.</summary>
    Existing,
    /// <summary>A new save name got a copy of the server's blank new game.</summary>
    Seeded,
    /// <summary>No blank for this War Sails mode was found.</summary>
    NoBlank
}

/// <summary>
/// The files and the start of the Coop nightly dedicated server (BannerlordCoopServer.exe), for any tool that
/// hosts one. The server reads its module list and save name from server-config.json in its data folder
/// (--data-dir), and Coop's gameplay settings, War Sails among them, from mod-config.json one level up, in the
/// COOP_DATA_DIR folder. It loads Native, SandBoxCore, Sandbox, Coop and, with War Sails on, NavalDLC before the
/// "modules" list and DedicatedServer.Windows after it. Each tool gives the server a data folder of its own, so
/// nothing of one tool's server reaches another's.
/// </summary>
public static class NightlyServer
{
    public const string ExeName = "BannerlordCoopServer.exe";
    public const string DataFolderName = "DedicatedServer";
    public const string ConfigFileName = "server-config.json";
    public const string ModConfigFileName = "mod-config.json";
    public const string GameSavesFolder = "Game Saves";
    public const string Blank = "default_new_game";
    public const string BlankWarSails = "default_new_game_warsails";
    public const string ModulesKey = "modules";
    public const string SaveNameKey = "saveName";
    public const string OptionsSection = "modOptions";
    public const string WarSailsKey = "warSails";
    public const string DataDirVariable = "COOP_DATA_DIR";
    public const string UserDirVariable = "BANNERLORD_USER_DIR";

    /// <summary>The server reads this once the person hosting accepted its terms in the tool, and saves the
    /// answer in the data folder's eula.txt. The tools never pass --accept-eula and never write eula.txt.</summary>
    public const string AcceptTermsVariable = "DS_ACCEPT_EULA";

    public const string EulaFileName = "eula.txt";

    /// <summary>Set to 0 for the server: .NET must keep its call counting stubs while Harmony patches go in.</summary>
    public const string CallCountingVariable = "DOTNET_TC_DeleteCallCountingStubsAfter";

    /// <summary>Console input is UTF-8 without a byte order mark: with one, the server loses the first command.</summary>
    public static readonly Encoding InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public const string NavalModuleId = "NavalDLC";

    /// <summary>The modules the server always loads before the "modules" list (the Coop module under its own
    /// id, and War Sails' NavalDLC only when War Sails is on), and the one it loads after it. None of them goes
    /// into the list.</summary>
    public static IReadOnlyList<string> LoadsFirst(string coopModuleId, bool warSails) =>
        warSails ? ["Native", "SandBoxCore", "Sandbox", coopModuleId, NavalModuleId] : ["Native", "SandBoxCore", "Sandbox", coopModuleId];

    public const string LoadsLast = "DedicatedServer.Windows";

    /// <summary>Everything the server loads, in order, for this "modules" list and War Sails mode.</summary>
    public static IReadOnlyList<string> FullOrder(string coopModuleId, IEnumerable<string> modules, bool warSails) =>
        LoadsFirst(coopModuleId, warSails).Concat(modules).Append(LoadsLast).ToList();

    // ----- Folders -----

    /// <summary>The server's data folder (--data-dir) inside the tool's own COOP_DATA_DIR folder.</summary>
    public static string DataDir(string coopDataRoot) => Path.Combine(coopDataRoot, DataFolderName);

    public static string ConfigPath(string dataDir) => Path.Combine(dataDir, ConfigFileName);

    /// <summary>mod-config.json, one level above the data folder.</summary>
    public static string ModConfigPath(string dataDir) => Path.Combine(Parent(dataDir), ModConfigFileName);

    public static string GameSaves(string dataDir) => Path.Combine(dataDir, GameSavesFolder);

    /// <summary>The Coop module's own template for mod-config.json in a server folder. The folder is named
    /// Coop for the nightly too.</summary>
    public static string ModConfigTemplate(string serverRoot) =>
        Path.Combine(serverRoot, "engine", "Modules", "Coop", "mod-config.default.json");

    // ----- server-config.json -----

    /// <summary>The "modules" list of server-config.json text, in order. Empty when there is none.</summary>
    public static IReadOnlyList<string> ReadModules(string configText)
    {
        using var document = JsoncEditor.Parse(configText).ToJsonDocument();
        if (Property(document.RootElement, ModulesKey) is not { ValueKind: JsonValueKind.Array } modules)
            return Array.Empty<string>();
        return modules.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim())
            .Where(id => id.Length > 0)
            .ToList();
    }

    public static string? ReadSaveName(string configText)
    {
        using var document = JsoncEditor.Parse(configText).ToJsonDocument();
        return Property(document.RootElement, SaveNameKey) is { ValueKind: JsonValueKind.String } name ? name.GetString() : null;
    }

    /// <summary>server-config.json text with these "modules" and "saveName" (the save's stem, SaveStem). Every
    /// other key, value and comment stays as it is; a key that already holds the wanted value keeps its own
    /// layout; a missing key is added. Text that does not read as JSON with comments throws (JsonException).</summary>
    public static string WriteConfig(string configText, IReadOnlyList<string> modules, string saveName)
    {
        var text = string.IsNullOrWhiteSpace(configText) ? "{\n}\n" : configText;
        var wanted = modules.Select(id => id.Trim()).ToArray();
        var stem = SaveStem(saveName);
        var editor = JsoncEditor.Parse(text);
        if (!ReadModules(text).SequenceEqual(wanted, StringComparer.Ordinal) || !editor.ContainsKey(ModulesKey))
            editor.SetRawValue(ModulesKey, JsonSerializer.Serialize(wanted));
        if (ReadSaveName(text) != stem)
            editor.SetString(SaveNameKey, stem);
        return editor.Text;
    }

    /// <summary>Writes the "modules" list and the save name into the data folder's server-config.json (the file
    /// is made when it is missing). A file that already says this is not written. True when it was written. A
    /// file that does not read as JSON with comments is never changed: that throws with a plain line.</summary>
    public static bool ApplyConfig(string dataDir, IReadOnlyList<string> modules, string saveName)
    {
        var path = ConfigPath(dataDir);
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        string updated;
        try
        {
            updated = WriteConfig(text, modules, saveName);
        }
        catch (JsonException)
        {
            throw Damaged(path);
        }

        if (updated == text)
            return false;
        Directory.CreateDirectory(dataDir);
        SaveChecked(path, updated);
        return true;
    }

    // ----- mod-config.json -----

    /// <summary>modOptions.warSails, or null when the file does not say.</summary>
    public static bool? ReadWarSails(string modConfigText) =>
        JsoncEditor.Parse(modConfigText).GetRawValue(OptionsSection, WarSailsKey)?.Trim() switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };

    /// <summary>mod-config.json text with modOptions.warSails set. Every other key and comment stays.</summary>
    public static string WriteWarSails(string modConfigText, bool warSails)
    {
        var editor = JsoncEditor.Parse(string.IsNullOrWhiteSpace(modConfigText) ? "{\n}\n" : modConfigText);
        editor.SetRawValue(OptionsSection, WarSailsKey, warSails ? "true" : "false");
        return editor.Text;
    }

    /// <summary>Sets War Sails in mod-config.json one level above the data folder. A missing file is made from
    /// the server's Coop template first (ModConfigTemplate) when there is one. True when the file was made or
    /// changed, so a file made from a template that already had this mode counts too. A file that does not read
    /// as JSON with comments is never changed: that throws with a plain line.</summary>
    public static bool ApplyWarSails(string dataDir, bool warSails, string? templatePath = null)
    {
        var store = new ModConfigStore(ModConfigPath(dataDir));
        Directory.CreateDirectory(Parent(dataDir));
        var created = !store.Exists;
        store.EnsureExists(templatePath);
        var text = store.Exists ? File.ReadAllText(store.Path) : "";
        if (!ReadsAsJson(text))
            throw Damaged(store.Path);
        if (ReadWarSails(text) == warSails)
            return created;
        SaveChecked(store.Path, WriteWarSails(text, warSails));
        return true;
    }

    private static bool ReadsAsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        try
        {
            using var document = JsoncEditor.Parse(text).ToJsonDocument();
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static InvalidOperationException Damaged(string path) =>
        new(path + " does not read as a settings file, so the tool did not change it. Fix the file or delete it, then try again.");

    // Writes a settings file only when the new text reads as JSON with comments. The first .bak copy is kept,
    // so a later start cannot replace the host's own file with an already changed one. The text goes to a
    // temporary file first, without a byte order mark, then over the old one.
    private static void SaveChecked(string path, string text)
    {
        if (!ReadsAsJson(text) || string.IsNullOrWhiteSpace(text))
            throw Damaged(path);
        var backup = path + ".bak";
        if (File.Exists(path) && !File.Exists(backup))
            File.Copy(path, backup);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    // ----- Saves -----

    public static string BlankName(bool warSails) => warSails ? BlankWarSails : Blank;

    /// <summary>True for the server's blank new games, which are templates and never a campaign to host.</summary>
    public static bool IsBlank(string? saveName)
    {
        var stem = SaveStem(saveName);
        return stem.Equals(Blank, StringComparison.OrdinalIgnoreCase) || stem.Equals(BlankWarSails, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where the blank new game for this War Sails mode can be, best first: the data folder's own
    /// copy (the server puts one there on its first start), the server folder's server-data, then the
    /// engine's bin folder.</summary>
    public static IReadOnlyList<string> BlankCandidates(string dataDir, string serverRoot, bool warSails)
    {
        var file = BlankName(warSails) + ".sav";
        return
        [
            Path.Combine(GameSaves(dataDir), file),
            Path.Combine(serverRoot, "server-data", GameSavesFolder, file),
            Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server", file)
        ];
    }

    // The safe rule for a new campaign name (Coop's start script's old A to Z rule).
    private static readonly Regex NewName = new("^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>Why this cannot be the name of a new campaign, or null when it can.</summary>
    public static string? NewSaveProblem(string? name)
    {
        var stem = SaveStem(name);
        if (stem.Length == 0)
            return "Type a name for the new campaign.";
        if (IsBlank(stem))
            return stem + " is the server's blank new game, not a campaign. Type another name for the new campaign.";
        if (!NewName.IsMatch(stem))
            return "A new campaign name can only use the English letters A to Z, numbers, spaces, hyphens and underscores, up to 64 in all, and starts with a letter or number. Coop's server refuses other letters such as Ü in a new campaign name.";
        if (Reserved.Contains(stem.Trim(), StringComparer.OrdinalIgnoreCase))
            return stem + " is a name Windows keeps for itself. Pick another name.";
        return null;
    }

    /// <summary>
    /// Gets a save ready for the server. A save that is already in the data folder's Game Saves is never
    /// touched. A new name gets a copy of the blank new game for this War Sails mode as &lt;name&gt;.sav, and a
    /// leftover &lt;name&gt;.json of an older world with that name is removed. A name that cannot be a new
    /// campaign is refused with NewSaveProblem's line.
    /// </summary>
    public static SeedResult SeedSave(string dataDir, string serverRoot, string saveName, bool warSails)
    {
        var stem = SaveStem(saveName);
        var saves = GameSaves(dataDir);
        if (stem.Length > 0 && !IsBlank(stem) && File.Exists(Path.Combine(saves, stem + ".sav")))
            return SeedResult.Existing;
        if (NewSaveProblem(saveName) is { } problem)
            throw new InvalidOperationException(problem);
        var blank = BlankCandidates(dataDir, serverRoot, warSails).FirstOrDefault(File.Exists);
        if (blank == null)
            return SeedResult.NoBlank;
        Directory.CreateDirectory(saves);
        File.Copy(blank, Path.Combine(saves, stem + ".sav"), overwrite: false);
        var leftover = Path.Combine(saves, stem + ".json");
        if (File.Exists(leftover))
            File.Delete(leftover);
        return SeedResult.Seeded;
    }

    /// <summary>
    /// Puts the blank new game for this War Sails mode in the data folder's Game Saves when it is not there yet, from the
    /// server folder (BlankCandidates). The Coop server makes a new campaign itself: with no save of that name it copies
    /// the blank for its "warSails" mode from its Game Saves, else from its own bin folder. False when there is no blank.
    /// </summary>
    public static bool EnsureBlank(string dataDir, string serverRoot, bool warSails)
    {
        var candidates = BlankCandidates(dataDir, serverRoot, warSails);
        if (File.Exists(candidates[0]))
            return true;
        var blank = candidates.Skip(1).FirstOrDefault(File.Exists);
        if (blank == null)
            return false;
        Directory.CreateDirectory(GameSaves(dataDir));
        var temp = candidates[0] + ".tmp";
        File.Copy(blank, temp, overwrite: true);
        File.Move(temp, candidates[0], overwrite: true);
        return true;
    }

    /// <summary>
    /// Undoes a new campaign whose server never built the world: &lt;name&gt;.sav, .json and .launcher.json go only while
    /// the .sav is still byte for byte a blank new game, so a world the server saved is never removed. The Server Tool
    /// keeps a &lt;name&gt;.last-good copy of the save, so one that is still a blank goes too. The next start makes the
    /// name again. True when the seed was removed.
    /// </summary>
    public static bool RemoveUnbuiltSeed(string dataDir, string serverRoot, string saveName)
    {
        var stem = SaveStem(saveName);
        if (NewSaveProblem(stem) != null)
            return false;
        var saves = GameSaves(dataDir);
        var blanks = BlankCandidates(dataDir, serverRoot, true).Concat(BlankCandidates(dataDir, serverRoot, false)).ToList();
        RemoveBlankCopy(saves, stem + ".last-good", blanks);
        return RemoveBlankCopy(saves, stem, blanks);
    }

    // <stem>.sav, <stem>.json and <stem>.launcher.json, only while the .sav is byte for byte one of the blanks. The start
    // script writes .launcher.json after a run that ends well and refuses -NewSaveName while it is there, so it goes too.
    // The .sav goes last: a locked side file leaves the blank in place and the next try finds it again.
    private static bool RemoveBlankCopy(string saves, string stem, IReadOnlyList<string> blanks)
    {
        var save = Path.Combine(saves, stem + ".sav");
        if (!File.Exists(save) || !blanks.Any(blank => SameBytes(save, blank)))
            return false;
        foreach (var side in new[] { ".launcher.json", ".json" })
        {
            var path = Path.Combine(saves, stem + side);
            if (File.Exists(path))
                File.Delete(path);
        }
        File.Delete(save);
        return true;
    }

    private static bool SameBytes(string path, string other)
    {
        if (!File.Exists(other) || new FileInfo(path).Length != new FileInfo(other).Length)
            return false;
        using var a = File.OpenRead(path);
        using var b = File.OpenRead(other);
        var left = new byte[81920];
        var right = new byte[81920];
        while (true)
        {
            var read = a.ReadAtLeast(left, left.Length, throwOnEndOfStream: false);
            if (read == 0)
                return true;
            if (b.ReadAtLeast(right, read, throwOnEndOfStream: false) != read || !left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read)))
                return false;
        }
    }

    /// <summary>The save name without a .sav ending or spaces around it.</summary>
    public static string SaveStem(string? saveName)
    {
        var name = (saveName ?? "").Trim();
        return name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name[..^4].Trim() : name;
    }

    // ----- Start -----

    /// <summary>
    /// The start of the server in serverRoot with the tool's own COOP_DATA_DIR folder: --data-dir points at its
    /// DedicatedServer folder, console input comes through a pipe and the server's own watchdog is off (the tool
    /// watches it). COOP_DATA_DIR is that folder and BANNERLORD_USER_DIR the data folder, so the server's
    /// settings, saves and logs stay inside the tool's folder. DS_ACCEPT_EULA is set only when the person
    /// hosting accepted the terms; otherwise it is in RemoveVariables, so a value inherited from the host's own
    /// shell never accepts the terms without the tool's button. DOTNET_TC_DeleteCallCountingStubsAfter is 0.
    /// extra adds environment variables (a tool's own switches); it can never set these four, in any casing.
    /// dataDir is for a tool whose data folder is a folder of its own, not COOP_DATA_DIR's DedicatedServer: --data-dir
    /// and BANNERLORD_USER_DIR then point at it, and COOP_DATA_DIR stays coopDataRoot, where the server reads mod-config.json.
    /// </summary>
    public static NightlyServerStart StartPlan(string serverRoot, string coopDataRoot, bool acceptTerms, IReadOnlyDictionary<string, string>? extra = null, string? dataDir = null)
    {
        var root = Path.GetFullPath(serverRoot);
        var coopData = Path.GetFullPath(coopDataRoot);
        dataDir = dataDir == null ? DataDir(coopData) : Path.GetFullPath(dataDir);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DataDirVariable] = coopData,
            [UserDirVariable] = dataDir,
            [CallCountingVariable] = "0"
        };
        if (acceptTerms)
            environment[AcceptTermsVariable] = "true";
        string[] reserved = [DataDirVariable, UserDirVariable, AcceptTermsVariable, CallCountingVariable];
        foreach (var (name, value) in extra ?? new Dictionary<string, string>())
        {
            if (!reserved.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                environment[name.Trim()] = value;
        }

        IReadOnlyList<string> remove = acceptTerms ? [] : [AcceptTermsVariable];
        return new NightlyServerStart(Path.Combine(root, ExeName), root, ["--data-dir", dataDir, "--pipe-input", "--no-watchdog"], environment, InputEncoding, remove);
    }

    /// <summary>The line for an exit code of the nightly server, or null for a code without one (0 among them).</summary>
    public static string? ExitHint(int exitCode) => ExitHints.TryGetValue(exitCode, out var hint) ? hint : null;

    public static readonly IReadOnlyDictionary<int, string> ExitHints = new Dictionary<int, string>
    {
        [2] = "The server could not load the campaign save (exit code 2). The save may be damaged, or it needs a mod that is not on.",
        [3] = "The server stopped on a fatal error while it was running (exit code 3). Its log names the cause.",
        [4] = "The server refused to start because its Coop module was changed or replaced (exit code 4). The server only runs the Coop build it came with.",
        [6] = "The server did not start because its terms were not accepted (exit code 6). Start it again and accept the terms to host.",
        [8] = "The server did not start because a module in its list has no folder in the server's Modules folder (exit code 8).",
        [9] = "The save does not fit the War Sails setting (exit code 9). A save made with War Sails needs War Sails on, and one made without it needs War Sails off.",
        [10] = "This server build cannot run War Sails (exit code 10). Turn War Sails off or use a server build that has it.",
        [11] = "The server made or loaded no world after it started hosting (exit code 11)."
    };

    private static JsonElement? Property(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string Parent(string dataDir) =>
        Path.GetDirectoryName(Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        ?? throw new InvalidOperationException("The server's data folder needs a folder above it.");
}
