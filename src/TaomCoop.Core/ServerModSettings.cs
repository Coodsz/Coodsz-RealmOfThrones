using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using CoodszSettingsSync;

namespace TaomCoop.Core;

/// <summary>One value of a mod's MCM settings file, as the file has it.</summary>
public sealed class ServerModValue
{
    public string Name { get; init; } = "";
    public JsonValueKind Kind { get; init; }

    /// <summary>A number written without a point or exponent: a whole number or a dropdown's place in its list.</summary>
    public bool Whole { get; init; }

    public string Text { get; init; } = "";
}

/// <summary>One Global\&lt;folder&gt;\&lt;id&gt;.json file in the server's own ModSettings folder.</summary>
public sealed class ServerModFile
{
    public string Folder { get; init; } = "";
    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    public List<ServerModValue> Values { get; init; } = new();

    /// <summary>The values the sync sends from this file (ServerModSettings.SyncedNames), at most 512.</summary>
    public IReadOnlySet<string> Synced { get; init; } = new HashSet<string>();

    /// <summary>More values than the 512 the sync sends.</summary>
    public bool More { get; init; }

    /// <summary>A looks mod (interface, graphics, sound): players keep their own values for it.</summary>
    public bool Looks { get; init; }

    public string Label => ServerModSettings.Shown(Folder.Equals(Id, StringComparison.OrdinalIgnoreCase) ? Folder : Folder + " (" + Id + ")");
}

/// <summary>What an import took, one plain line per item it left out, and the mods it changed.</summary>
public sealed record ModSettingsImport(int Added, IReadOnlyList<string> Skipped, IReadOnlyList<string> Mods);

/// <summary>
/// The mod settings players get from the Realm of Thrones and TAOM servers. MCM's provider does not run there (its DLLs
/// load as libraries, its SubModule does not, as the server logs show), so the settings sync sends the flat
/// Global\&lt;folder&gt;\&lt;id&gt;.json files of one folder (SettingsFiles.FindModSettingsRoot): the server's own
/// &lt;data folder&gt;\Configs\ModSettings once it holds any file, otherwise the host's
/// Documents\Mount and Blade II Bannerlord\Configs\ModSettings, never both. Seed copies every Documents file the
/// server's folder is missing, and a newer Documents copy of a file the host did not change or import here, on every
/// page open, Prepare and Start. The files the host changed or imported are listed next to the folder
/// (<see cref="EditedListPath"/>) and stay as set until Back to this PC's values. The sync reads the folder again
/// every 5 seconds and sends each settings id that changed, so a save here reaches every player within a few seconds.
/// Looks mods (the community list's UI, Graphics and Audio mods) go into recipes.json's PlayersOwnSettings, so players
/// keep their own values for them.
/// </summary>
public static class ServerModSettings
{
    public const long MaxFileBytes = 256 * 1024;
    public const int MaxEntries = 500;
    public const long MaxTotalBytes = 16 * 1024 * 1024;

    /// <summary>The most values the sync sends for one settings id (SettingsWire.MaxValuesPerObject).</summary>
    public const int MaxValues = 512;

    /// <summary>The most settings ids the sync sends (SettingsWire.MaxObjectsPerSession); it leaves out the rest.</summary>
    public const int MaxIds = 256;

    public static string ServerRoot(string dataRoot) => System.IO.Path.Combine(dataRoot, "Configs", "ModSettings");

    public static string DocumentsRoot =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Configs", "ModSettings");

    /// <summary>
    /// The files the host changed or imported here, as paths under the server's folder. It sits next to the folder,
    /// never in it: any file inside makes the sync read only the server's folder.
    /// </summary>
    public static string EditedListPath(string serverRoot) => serverRoot.TrimEnd('\\', '/') + ".edited.json";

    // Seed, Save, Back to this PC's values and Import each read the list of changed files, write files and write the list
    // again: one at a time, so a seed never writes this PC's copy over a file a save is writing, or the list goes stale.
    private static readonly object FilesLock = new();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Copies each Documents Global file the server's folder does not have yet, and the Documents copy of a file the
    /// host did not change or import here whenever its content differs. Files match by exact settings id, whatever the
    /// folder, so an id never gets two files; an id that differs only in capital letters from one the server has (or
    /// one taken this run) is skipped with a line in <paramref name="skipped"/>, so the folder never holds such twins.
    /// When the list of changed files can't be read, only missing files are copied. A file that can't be read or
    /// written now is left for the next Seed and named in <paramref name="failed"/>. Returns how many were copied.
    /// </summary>
    public static int Seed(string serverRoot, string documentsRoot, List<string>? failed = null, List<string>? skipped = null)
    {
        lock (FilesLock)
            return SeedLocked(serverRoot, documentsRoot, failed, skipped);
    }

    private static int SeedLocked(string serverRoot, string documentsRoot, List<string>? failed, List<string>? skipped)
    {
        var from = System.IO.Path.Combine(documentsRoot, "Global");
        if (!Directory.Exists(from) || SameFolder(serverRoot, documentsRoot))
            return 0;
        HashSet<string>? edited;
        try
        {
            edited = ReadEdited(serverRoot);
        }
        catch (IOException)
        {
            edited = null;
        }

        // Matched by settings id, whatever the folder on either side (FilesById), so an id never gets two files.
        var server = FilesById(serverRoot);
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in server.Keys)
            taken.TryAdd(id, id);
        var copied = 0;
        foreach (var (id, source) in FilesById(documentsRoot))
        {
            var relative = System.IO.Path.GetRelativePath(documentsRoot, source);
            try
            {
                byte[] bytes;
                if (server.TryGetValue(id, out var target))
                {
                    if (edited == null || edited.Contains(System.IO.Path.GetRelativePath(serverRoot, target)))
                        continue;
                    bytes = File.ReadAllBytes(source);
                    if (bytes.AsSpan().SequenceEqual(File.ReadAllBytes(target)))
                        continue;
                }
                else if (taken.TryGetValue(id, out var twin))
                {
                    skipped?.Add(TwinLine(id, twin));
                    continue;
                }
                else
                {
                    target = System.IO.Path.Combine(serverRoot, relative);
                    bytes = File.ReadAllBytes(source);
                    taken[id] = id;
                }

                // A file with a name or text that can't be read back (a lone surrogate escape, bad UTF-8) never reaches the
                // server's folder: there it would stop the page and every Start.
                if (HasUnreadableText(bytes))
                {
                    skipped?.Add(Shown(relative) + ": has text that is not valid, so it was skipped.");
                    continue;
                }

                WriteAtomic(target, bytes);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed?.Add(relative);
            }
        }

        return copied;
    }

    /// <summary>Every Global settings file in the server's folder that reads as a JSON object, by mod.</summary>
    public static List<ServerModFile> Load(string serverRoot, ModCompatDatabase list)
    {
        var global = System.IO.Path.Combine(serverRoot, "Global");
        if (!Directory.Exists(global))
            return new List<ServerModFile>();
        var read = new List<(string Path, string Folder, string Id, List<ServerModValue> Values, bool Looks, bool FolderLooks, List<string> Synced)>();
        foreach (var path in Directory.EnumerateFiles(global, "*.json", SearchOption.AllDirectories))
        {
            var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? "";
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                using var document = JsonDocument.Parse(WithoutBom(File.ReadAllBytes(path)));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                var values = document.RootElement.EnumerateObject().Select(property => new ServerModValue
                {
                    Name = property.Name,
                    Kind = property.Value.ValueKind,
                    Whole = property.Value.ValueKind == JsonValueKind.Number && property.Value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0,
                    Text = property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? property.Value.GetRawText() : property.Value.ToString()
                }).ToList();
                read.Add((path, folder, id, values, IsLooks(list, id, folder), IsLooks(list, folder), SyncedNames(id, document.RootElement, MaxValues + 1)));
            }
            // InvalidOperationException: JsonDocument takes a lone surrogate escape ("D800") or bad UTF-8, and reading that name or
            // text throws later; that one file is left out instead of the whole page and every Start failing.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // A file being written or broken is left out of the list; the sync leaves it out the same way.
            }
        }

        // An id that is gameplay anywhere stays gameplay everywhere, so no second file can unlock it for players.
        // Nor can a file whose id or folder is the name of a gameplay mod's id or folder.
        var gameplay = new HashSet<string>(read.Where(file => !file.Looks).SelectMany(file => file.FolderLooks ? new[] { file.Id } : new[] { file.Id, file.Folder }), StringComparer.OrdinalIgnoreCase);
        return read.Select(file => new ServerModFile { Folder = file.Folder, Id = file.Id, Path = file.Path, Values = file.Values,
                Synced = file.Synced.Take(MaxValues).ToHashSet(StringComparer.Ordinal), More = file.Synced.Count > MaxValues, Looks = file.Looks && !gameplay.Contains(file.Id) && !gameplay.Contains(file.Folder) })
            .OrderBy(file => file.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The names of the values the sync sends from one settings file, in its order, decided the way
    /// SettingsFiles.ReadValues does it with the same SettingsWire rules: no file whose id is a hotkey, interface,
    /// sound or camera one, none of those values, only plain values that encode (no list, no private or overlong
    /// text, no number out of range), at most <paramref name="max"/>. Tests check it against SettingsFiles.CaptureFromFiles.
    /// </summary>
    public static List<string> SyncedNames(string id, JsonElement root, int max = MaxValues)
    {
        var names = new List<string>();
        if (!SettingsWire.IsValidId(id) || SettingsWire.IsAlwaysSkipped(id) || root.ValueKind != JsonValueKind.Object)
            return names;
        foreach (var property in root.EnumerateObject())
        {
            if (names.Count >= max)
                break;
            if (!SettingsWire.IsAlwaysSkipped(property.Name) && ToWire(property.Name, property.Value) is { } value && SettingsWire.Encode(value) != null)
                names.Add(property.Name);
        }

        return names;
    }

    // SettingsFiles.ToWire, step for step.
    private static WireValue? ToWire(string id, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True: return new WireValue(WireKind.Bool, id, SettingsWire.FormatBool(true));
            case JsonValueKind.False: return new WireValue(WireKind.Bool, id, SettingsWire.FormatBool(false));
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                return text.Length > SettingsWire.MaxStringLength || SettingsWire.IsPrivateText(text, id) ? null : new WireValue(WireKind.String, id, text);
            case JsonValueKind.Number:
                var raw = element.GetRawText();
                if (raw.IndexOfAny(['.', 'e', 'E']) < 0 && element.TryGetInt32(out var whole))
                    return new WireValue(WireKind.Int, id, SettingsWire.FormatInt(whole));
                return element.TryGetDouble(out var number) && !double.IsInfinity(number) ? new WireValue(WireKind.Float, id, SettingsWire.FormatFloat(number)) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// True for a looks mod: the community list's category is UI, Graphics or Audio. Matched on the settings id, the
    /// MCM folder and the list's name with spaces removed. Everything else is gameplay, synced and locked: unknown and
    /// unmatched mods, and client only ones too (XorberaxLegacy runs gameplay code on every player's PC).
    /// </summary>
    public static bool IsLooks(ModCompatDatabase list, params string[] names)
    {
        var keys = names.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var entry = keys.Select(list.Find).FirstOrDefault(found => found != null)
            ?? keys.Select(ModCompatDatabase.Normalize).Where(key => key.Length > 0)
                .Select(key => list.All.FirstOrDefault(item => ModCompatDatabase.Normalize(item.DisplayName) == key))
                .FirstOrDefault(found => found != null);
        return entry?.Category is { } category && (category.Equals("UI", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Graphics", StringComparison.OrdinalIgnoreCase) || category.Equals("Audio", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Writes the changed values (name to text) into the file, each in the kind it has, then replaces the file in one
    /// step and lists it as changed here. Every other value keeps its text as written (2.0 stays 2.0). Returns the
    /// first value that does not fit, as a line for the host, with nothing written; null when saved.
    /// </summary>
    public static string? Save(string serverRoot, string path, IReadOnlyDictionary<string, string> changes)
    {
        lock (FilesLock)
            return SaveLocked(serverRoot, path, changes);
    }

    private static string? SaveLocked(string serverRoot, string path, IReadOnlyDictionary<string, string> changes)
    {
        // First, so a list that can't be read stops the save before anything is written.
        var edited = ReadEdited(serverRoot);
        var bytes = File.ReadAllBytes(path);
        var root = JsonNode.Parse(WithoutBom(bytes)) as JsonObject ?? throw new InvalidDataException(path + " is not a settings file.");
        foreach (var (name, text) in changes)
        {
            if (root[name] is not JsonValue current)
                continue;
            switch (current.GetValueKind())
            {
                case JsonValueKind.True or JsonValueKind.False:
                    if (!bool.TryParse(text.Trim(), out var on))
                        return name + " needs on or off.";
                    root[name] = on;
                    break;
                case JsonValueKind.Number:
                    var typed = text.Trim().Replace(',', '.');
                    if (current.ToJsonString().IndexOfAny(['.', 'e', 'E']) < 0)
                    {
                        if (!long.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                            return name + " needs a whole number.";
                        root[name] = JsonNode.Parse(whole.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                            return name + " needs a number.";
                        var raw = number.ToString("R", CultureInfo.InvariantCulture);
                        root[name] = JsonNode.Parse(raw.IndexOfAny(['.', 'E', 'e']) < 0 ? raw + ".0" : raw);
                    }
                    break;
                case JsonValueKind.String:
                    root[name] = text;
                    break;
            }
        }

        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var json = Encoding.UTF8.GetBytes(root.ToJsonString(Indented));
        // Listed as changed first, so a crash before the write never leaves a saved file the next Seed writes over. A write
        // that fails takes the entry out again, so the next Seed still keeps the file in step with this PC.
        var relative = System.IO.Path.GetRelativePath(serverRoot, path);
        var listedNow = edited.Add(relative);
        if (listedNow)
            WriteEdited(serverRoot, edited);
        try
        {
            WriteAtomic(path, bom ? [0xEF, 0xBB, 0xBF, .. json] : json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (listedNow && edited.Remove(relative))
            {
                try { WriteEdited(serverRoot, edited); }
                catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                {
                    // The host hears of it with the save's own reason, never a quiet stale mark.
                    throw new IOException(ex.Message + " This file also stays marked as changed here, so seeding leaves it alone until you click Back to this PC's values.", ex);
                }
            }
            throw;
        }
        return null;
    }

    // Reads every property name and every text value under the element, so a name or text the parser took but can't give
    // back as a string (a lone surrogate, bad UTF-8) throws InvalidOperationException here, before anything is written.
    private static void ReadAllText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = property.Name;
                    ReadAllText(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ReadAllText(item);
                break;
            case JsonValueKind.String:
                _ = element.GetString();
                break;
        }
    }

    /// <summary>True when this PC's Documents folder has a file for this server file.</summary>
    public static bool HasPcCopy(string serverRoot, string documentsRoot, string path) =>
        FindPcCopy(FilesById(documentsRoot), System.IO.Path.GetFileNameWithoutExtension(path)) != null;

    /// <summary>
    /// Back to this PC's values for one file, or for every Global file when <paramref name="path"/> is null. A file
    /// this PC has a copy of is overwritten with it in one step, so the sync sends the change; a file it has none of
    /// is deleted, but never while the server runs (<paramref name="serverRunning"/>): a deleted file sends players
    /// nothing, so players on the server would keep its values while later ones use their own. Those stay, named in
    /// <paramref name="kept"/>. Returns how many files got this PC's values.
    /// </summary>
    public static int Reset(string serverRoot, string documentsRoot, string? path = null, bool serverRunning = false, List<string>? kept = null)
    {
        lock (FilesLock)
            return ResetLocked(serverRoot, documentsRoot, path, serverRunning, kept);
    }

    private static int ResetLocked(string serverRoot, string documentsRoot, string? path, bool serverRunning, List<string>? kept)
    {
        var global = System.IO.Path.GetFullPath(System.IO.Path.Combine(serverRoot, "Global"));
        var targets = new List<string>();
        if (path != null)
        {
            if (System.IO.Path.GetFullPath(path).StartsWith(global + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                targets.Add(path);
        }
        else if (Directory.Exists(global))
        {
            targets.AddRange(Directory.EnumerateFiles(global, "*.json", SearchOption.AllDirectories));
        }
        // This PC's copy of each file by settings id, whatever folder either side keeps it in.
        var pc = FilesById(documentsRoot);
        var edited = ReadEdited(serverRoot);
        var restored = 0;
        // Only the files handled here leave the list; one a failure stopped at stays listed.
        void SaveList(bool failing)
        {
            if (path == null && !serverRunning)
                edited.RemoveWhere(relative => !File.Exists(System.IO.Path.Combine(serverRoot, relative)));
            if (!failing)
            {
                WriteEdited(serverRoot, edited);
                return;
            }
            try { WriteEdited(serverRoot, edited); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try
        {
            foreach (var file in targets)
            {
                var relative = System.IO.Path.GetRelativePath(serverRoot, file);
                if (FindPcCopy(pc, System.IO.Path.GetFileNameWithoutExtension(file)) is { } copy)
                {
                    WriteAtomic(file, File.ReadAllBytes(copy));
                    restored++;
                }
                else if (serverRunning)
                {
                    // Kept, and still listed as changed here, until the server is stopped.
                    var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file)) ?? "";
                    var id = System.IO.Path.GetFileNameWithoutExtension(file);
                    kept?.Add(Shown(folder.Equals(id, StringComparison.OrdinalIgnoreCase) ? folder : folder + " (" + id + ")"));
                    continue;
                }
                else
                {
                    File.Delete(file);
                }
                edited.Remove(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list goes out on a best try, so its own failure never hides this one.
            SaveList(failing: true);
            throw;
        }

        SaveList(failing: false);
        return path == null ? restored + Seed(serverRoot, documentsRoot) : restored;
    }

    /// <summary>
    /// Puts the looks mods' settings ids into recipes.json (PlayersOwnSettings) in <paramref name="moduleFolder"/>, the folder
    /// Coodsz Server Tools runs from on the server (its own module folder on Realm of Thrones, DedicatedServer.Windows on TAOM);
    /// ids the host listed by hand stay; the ones this tool listed before are kept in CoodszLooksSettings, so a
    /// mod that is no longer looks leaves the list again. Every other field stays, and a recipes.json that does not
    /// read is kept as recipes.json.bak first. The sync reads it again within a few seconds. False when the module is
    /// not in the server yet.
    /// </summary>
    public static bool WritePlayersOwn(string moduleFolder, IEnumerable<ServerModFile> files, Action<string>? note = null)
    {
        const string Field = "PlayersOwnSettings", Ours = "CoodszLooksSettings";
        var folder = moduleFolder;
        if (!Directory.Exists(folder))
            return false;
        var path = System.IO.Path.Combine(folder, "recipes.json");
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        JsonObject root;
        try
        {
            root = existing == null ? new JsonObject() : JsonNode.Parse(existing) as JsonObject ?? throw new JsonException();
            // A key named twice only throws once the object is read, so it is read here.
            _ = root.Count;
            if (root[Field] is { } field && field is not JsonArray)
                throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // Kept as the first free recipes.json.bak, .bak2 up to .bak9, so an earlier copy is never written over; a copy
            // with the same bytes is not made twice, and past .bak9 the last one is reused.
            var bytes = File.ReadAllBytes(path);
            var backups = new[] { path + ".bak" }.Concat(Enumerable.Range(2, 8).Select(i => path + ".bak" + i)).ToList();
            var backup = backups.FirstOrDefault(name => File.Exists(name) && File.ReadAllBytes(name).AsSpan().SequenceEqual(bytes))
                ?? backups.FirstOrDefault(name => !File.Exists(name)) ?? backups[^1];
            if (!File.Exists(backup) || !File.ReadAllBytes(backup).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(backup, bytes);
            note?.Invoke("recipes.json could not be read as a list of mods players keep for themselves, so it was kept as " + System.IO.Path.GetFileName(backup) + " and written again.");
            root = new JsonObject();
        }

        static List<string> Strings(JsonNode? node) => node is JsonArray array
            ? array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : null).Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text!).ToList()
            : new List<string>();
        var before = new HashSet<string>(Strings(root[Ours]), StringComparer.OrdinalIgnoreCase);
        // The host's own entries, and the looks ids only this tool adds: an id the host listed stays theirs.
        var hand = Strings(root[Field]).Where(id => !before.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var looks = files.Where(file => file.Looks).Select(file => file.Id).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !hand.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var list = hand.Concat(looks).ToList();
        root["SchemaVersion"] ??= 1;
        root["GeneratedBy"] ??= ServerToolsModule.Name;
        root["Mods"] ??= new JsonArray();
        static JsonArray Array(List<string> ids) => new(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        if (list.Count == 0)
            root.Remove(Field);
        else
            root[Field] = Array(list);
        if (looks.Count == 0)
            root.Remove(Ours);
        else
            root[Ours] = Array(looks);
        var text = root.ToJsonString(Indented);
        if (text != existing)
            WriteAtomic(path, Encoding.UTF8.GetBytes(text));
        return true;
    }

    /// <summary>
    /// At every Prepare and Start, after Coodsz Server Tools is in the server: seeds the server's folder and lists the
    /// looks mods in recipes.json, that also when the seed fails. Returns how many files the seed copied.
    /// </summary>
    public static int Prepare(string dataRoot, string moduleFolder, string documentsRoot, ModCompatDatabase list, Action<string>? note = null)
    {
        var root = ServerRoot(dataRoot);
        int copied;
        try
        {
            var skipped = new List<string>();
            var failed = new List<string>();
            copied = Seed(root, documentsRoot, failed, skipped);
            foreach (var line in skipped)
                note?.Invoke(line);
            if (failed.Count > 0)
                note?.Invoke(failed.Count == 1 ? "1 file on this PC could not be copied now. It is tried again next time."
                    : failed.Count + " files on this PC could not be copied now. They are tried again next time.");
        }
        catch (Exception seedFailed)
        {
            // recipes.json still follows the files; its own failure never hides the seed's.
            try { WritePlayersOwn(moduleFolder, Load(root, list), note); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(seedFailed).Throw();
            throw;
        }

        WritePlayersOwn(moduleFolder, Load(root, list), note);
        return copied;
    }

    /// <summary>
    /// Imports the mod settings a player sent: a folder or a .zip. Untrusted. The file name is the settings id players
    /// get, so the target is picked by id: an id the server has is written over that exact file, name and folder; a
    /// new id only into the folder of a mod the server has (<paramref name="serverMods"/>: its module folders and the
    /// setup's mods), when the id starts with that mod's name or the host's Documents has the same folder and id.
    /// Each file is at most 256 KB, a JSON object with at most 512 values, and written in one step inside the server's
    /// Global folder; 500 entries and 16 MB in all at most. Anything else is left out with one line. Nothing is run
    /// and nothing is unpacked anywhere else. Every written file is listed as changed here.
    /// </summary>
    public static ModSettingsImport Import(string source, string serverRoot, string documentsRoot, IEnumerable<string> serverMods)
    {
        lock (FilesLock)
            return ImportLocked(source, serverRoot, documentsRoot, serverMods);
    }

    private static ModSettingsImport ImportLocked(string source, string serverRoot, string documentsRoot, IEnumerable<string> serverMods)
    {
        // The game's own modules have no MCM settings, so they never let a new id in.
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mod in serverMods.Where(mod => !string.IsNullOrWhiteSpace(mod) && !IsOfficial(mod)))
            if (ModKey(mod) is { Length: > 0 } key)
                known.TryAdd(key, mod.Trim());
        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(serverRoot, "Global"));
        // The sync uses the first file per id in this order (SettingsFiles.CaptureFromFiles).
        var byId = FilesById(serverRoot);
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(target))
        {
            foreach (var folder in Directory.GetDirectories(target))
                folders.TryAdd(ModKey(System.IO.Path.GetFileName(folder)), System.IO.Path.GetFileName(folder));
        }

        // The ids the cap counts: the server's and this PC's, so an import never crowds out a file the host's seed brings in later.
        var counted = new HashSet<string>(byId.Keys, StringComparer.Ordinal);
        if (Directory.Exists(System.IO.Path.Combine(documentsRoot, "Global")))
            foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(documentsRoot, "Global"), "*.json", SearchOption.AllDirectories))
                counted.Add(System.IO.Path.GetFileNameWithoutExtension(file));
        var context = new ImportTarget(target, System.IO.Path.Combine(documentsRoot, "Global"), known, byId, folders, counted);
        var skipped = new List<string>();
        var mods = new List<string>();
        var written = new Dictionary<string, (string Shown, string Id)>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        long total = 0;
        var name = Shown(System.IO.Path.GetFileName(source.TrimEnd('\\', '/')));
        var edited = ReadEdited(serverRoot);
        var full = false;
        var newIds = 0;

        // One file: its path inside what was sent, and how to read at most MaxFileBytes + 1 of it.
        void Take(string inside, Func<byte[]> read)
        {
            var shown = Shown(inside);
            var problem = TargetOf(inside, shown, context, out var file);
            if (problem != null)
            {
                skipped.Add(problem);
                return;
            }
            var takenId = System.IO.Path.GetFileNameWithoutExtension(file!);
            if (written.TryGetValue(takenId, out var first))
            {
                skipped.Add(first.Id.Equals(takenId, StringComparison.Ordinal)
                    ? shown + ": the same settings file as " + first.Shown + ", so only the first one was taken."
                    : shown + ": " + Shown(takenId) + " differs only in capital letters from " + Shown(first.Id) + ", so it was skipped.");
                return;
            }
            // The sync sends the first 256 ids only, so a new id past that would push one the server has out.
            var isNew = !counted.Contains(System.IO.Path.GetFileNameWithoutExtension(file!));
            if (isNew && counted.Count + newIds >= MaxIds)
            {
                skipped.Add(shown + ": the server already has " + MaxIds + " mod settings files, the most players can get, so it was skipped.");
                return;
            }

            byte[] bytes;
            try
            {
                bytes = read();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                skipped.Add(shown + ": could not be read, so it was skipped.");
                return;
            }
            if (bytes.Length > MaxFileBytes)
            {
                skipped.Add(shown + ": bigger than 256 KB, so it was skipped.");
                return;
            }
            try
            {
                using var document = JsonDocument.Parse(WithoutBom(bytes));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    skipped.Add(shown + ": not a mod settings file (its JSON is not an object), so it was skipped.");
                    return;
                }
                // Every name and text read once here: JsonDocument takes a lone surrogate escape or bad UTF-8 that only throws when
                // read, and such a file written to the server would break the page and every Start.
                ReadAllText(document.RootElement);
                // Counted with the sync's own rules: hotkeys and the like do not count.
                if (SyncedNames(System.IO.Path.GetFileNameWithoutExtension(file!), document.RootElement, MaxValues + 1).Count > MaxValues)
                {
                    skipped.Add(shown + ": more than 512 values, more than players can get, so it was skipped.");
                    return;
                }
            }
            catch (JsonException)
            {
                skipped.Add(shown + ": not valid JSON, so it was skipped.");
                return;
            }
            catch (InvalidOperationException)
            {
                skipped.Add(shown + ": has text that is not valid, so it was skipped.");
                return;
            }

            // Only files that pass count toward the 16 MB.
            if (total + bytes.Length > MaxTotalBytes)
            {
                skipped.Add(name + ": more than 16 MB in all, so the rest was skipped.");
                full = true;
                return;
            }
            total += bytes.Length;

            // Listed as changed here before the file is written, so a crash in between never leaves an imported file
            // that the next Seed would take for this PC's copy and write over.
            var relative = System.IO.Path.GetRelativePath(serverRoot, file!);
            var listedNow = edited.Add(relative);
            if (listedNow)
                WriteEdited(serverRoot, edited);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    WriteAtomic(file!, bytes);
                    break;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 3)
                {
                    Thread.Sleep(150);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(shown + ": could not be written (the file may be in use), so it was skipped.");
                    if (listedNow && edited.Remove(relative))
                    {
                        try { WriteEdited(serverRoot, edited); }
                        catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                        {
                            skipped.Add(shown + ": it also stays marked as changed here, so seeding leaves it alone until you click Back to this PC's values.");
                        }
                    }
                    return;
                }
            }

            written[takenId] = (shown, takenId);
            if (isNew)
                newIds++;
            var id = System.IO.Path.GetFileNameWithoutExtension(file!);
            var folderName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file!)) ?? "";
            mods.Add(Shown(folderName.Equals(id, StringComparison.OrdinalIgnoreCase) ? folderName : folderName + " (" + id + ")"));
            added++;
        }

        try
        {
            if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(source);
                    if (zip.Entries.Count > MaxEntries)
                        return new ModSettingsImport(0, [name + ": more than " + MaxEntries + " files, so nothing was taken from it."], []);
                    foreach (var entry in zip.Entries)
                    {
                        if (full)
                            break;
                        if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                            continue;
                        // A link (Unix mode S_IFLNK, or the Windows reparse point attribute) is never followed.
                        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                        {
                            skipped.Add(Shown(entry.FullName) + ": a link, not a file, so it was skipped.");
                            continue;
                        }
                        Take(entry.FullName, () =>
                        {
                            using var stream = entry.Open();
                            return ReadCapped(stream);
                        });
                    }
                }
                catch (InvalidDataException)
                {
                    skipped.Add(name + ": could not be read as a .zip file.");
                }
            }
            else if (Directory.Exists(source))
            {
                // The folder itself may be Global, ModSettings, Configs or one above; links inside are never followed.
                var inGlobal = System.IO.Path.GetFileName(source.TrimEnd('\\', '/')).Equals("Global", StringComparison.OrdinalIgnoreCase);
                var seen = 0;
                var stack = new Stack<string>([source]);
                while (stack.Count > 0 && !full)
                {
                    foreach (var item in new DirectoryInfo(stack.Pop()).EnumerateFileSystemInfos())
                    {
                        if (full)
                            break;
                        var relative = System.IO.Path.GetRelativePath(source, item.FullName);
                        var inside = inGlobal ? System.IO.Path.Combine("Global", relative) : relative;
                        // Folders count too, so a tree of empty folders can't keep the walk going.
                        if (++seen > MaxEntries)
                        {
                            skipped.Add(name + ": more than " + MaxEntries + " files, so the rest was skipped.");
                            full = true;
                        }
                        else if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                            skipped.Add(Shown(inside) + ": a link, not a file, so it was skipped.");
                        else if (item is DirectoryInfo)
                            stack.Push(item.FullName);
                        else
                            Take(inside, () =>
                            {
                                using var stream = File.OpenRead(item.FullName);
                                return ReadCapped(stream);
                            });
                    }
                }
            }
            else
            {
                skipped.Add(name + ": not a folder or a .zip file.");
            }
        }
        catch (Exception)
        {
            // What was written stays listed on a best try; that never hides the failure.
            if (added > 0)
            {
                try { WriteEdited(serverRoot, edited); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }

        if (added > 0)
            WriteEdited(serverRoot, edited);

        return new ModSettingsImport(added, skipped, mods.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private sealed record ImportTarget(string Global, string DocumentsGlobal, Dictionary<string, string> Known,
        Dictionary<string, string> ById, Dictionary<string, string> Folders, HashSet<string> Ids);

    // Why a path inside what was sent is not taken, as the line for the host, or null with the file it goes to.
    private static string? TargetOf(string inside, string shown, ImportTarget at, out string? file)
    {
        file = null;
        if (inside.Contains(':') || inside.StartsWith('/') || inside.StartsWith('\\') || System.IO.Path.IsPathRooted(inside))
            return shown + ": a full path, not a file inside what was sent, so it was skipped.";
        var parts = inside.Split('/', '\\').Where(part => part.Length > 0).ToArray();
        if (parts.Any(part => part is "." or ".."))
            return shown + ": points outside what was sent, so it was skipped.";
        // Global\mod\id.json, with at most three folders before Global (Documents\...\Configs\ModSettings\Global).
        if (parts.Length is < 3 or > 6 || !parts[^3].Equals("Global", StringComparison.OrdinalIgnoreCase)
            || !parts[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return shown + ": not a Global mod settings file (Global\\mod\\id.json), so it was skipped.";
        var mod = parts[^2];
        var id = System.IO.Path.GetFileNameWithoutExtension(parts[^1]);
        if (!PlainName(mod) || !PlainName(id))
            return shown + ": a file or folder name Windows can't use, so it was skipped.";

        // First of all: never next to a server id that differs only in capital letters, whatever this PC has. On Windows
        // the second file would replace the first.
        if (!at.ById.ContainsKey(id) && at.ById.Keys.FirstOrDefault(other => other.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } serverTwin)
            return shown + ": " + Shown(id) + " differs only in capital letters from " + Shown(serverTwin) + ", so it was skipped.";

        string? chosen;
        if (at.ById.TryGetValue(id, out var existing))
        {
            // The id players get stays the file the server already sends, in its own folder and with its own name.
            chosen = existing;
        }
        // Against the server's ids and this PC's (Ids), so the host's own file can still come in later.
        else if (!at.Ids.Contains(id) && at.Ids.FirstOrDefault(other => other.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } twin)
        {
            // Never a second id that differs only in capital letters: players' MCM may know only one of them.
            return shown + ": " + Shown(id) + " differs only in capital letters from " + Shown(twin) + ", so it was skipped.";
        }
        else
        {
            var key = ModKey(mod);
            if (!at.Known.TryGetValue(key, out var serverMod))
                return shown + ": the server does not have the mod " + Shown(mod) + ", so it was skipped.";
            var documents = Directory.Exists(System.IO.Path.Combine(at.DocumentsGlobal, mod))
                // Windows matches the name pattern in any case; only the exact id counts.
                ? Directory.GetFiles(System.IO.Path.Combine(at.DocumentsGlobal, mod), id + ".json")
                    .FirstOrDefault(file => System.IO.Path.GetFileNameWithoutExtension(file).Equals(id, StringComparison.Ordinal))
                : null;
            // The mod whose name the id starts with, the longest one: FancyCombat_v1 is FancyCombat's, never Fancy's.
            var owner = at.Known.Keys.Where(known => ModCompatDatabase.Normalize(id).StartsWith(known, StringComparison.Ordinal) || ModKey(id).StartsWith(known, StringComparison.Ordinal))
                .OrderByDescending(known => known.Length).FirstOrDefault();
            if (documents != null)
                // The host's own file: its name, in the server's folder for the mod when there is one.
                chosen = System.IO.Path.Combine(at.Global, at.Folders.TryGetValue(key, out var own) ? own : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(documents))!, System.IO.Path.GetFileName(documents));
            else if (owner != null && owner != key)
                return shown + ": " + Shown(id) + " belongs to the mod " + Shown(at.Known[owner]) + ", not " + Shown(mod) + ", so it was skipped.";
            else if (owner == key)
                // The server's own folder for the mod, else the mod's name as the server has it, never the spelling sent.
                chosen = System.IO.Path.Combine(at.Global, at.Folders.TryGetValue(key, out var folder) ? folder : PlainName(serverMod) ? serverMod : key, id + ".json");
            else
                return shown + ": " + Shown(id) + " is not a settings file of the mod " + Shown(mod) + ", so it was skipped.";
        }

        var full = System.IO.Path.GetFullPath(chosen);
        if (!full.StartsWith(at.Global + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return shown + ": points outside what was sent, so it was skipped.";
        // The last check before the write: a file already there under another case would be replaced, so it is kept.
        if (File.Exists(full) && Directory.GetFiles(System.IO.Path.GetDirectoryName(full)!, System.IO.Path.GetFileName(full)).Select(System.IO.Path.GetFileName)
                .FirstOrDefault() is { } onDisk && !onDisk.Equals(System.IO.Path.GetFileName(full), StringComparison.Ordinal))
            return shown + ": " + Shown(id) + " differs only in capital letters from " + Shown(System.IO.Path.GetFileNameWithoutExtension(onDisk)) + ", so it was skipped.";
        file = full;
        return null;
    }

    // Each settings id's file in a ModSettings folder's Global, the first in the order the sync reads them, with ids told
    // apart exactly as the sync does (SettingsFiles.CaptureFromFiles, Ordinal): Combat_v1 and combat_v1 are two ids.
    // Seed, HasPcCopy, Reset and Import all match files this way: by exact id to write, FindPcCopy to find this PC's copy.
    private static Dictionary<string, string> FilesById(string root)
    {
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        var global = System.IO.Path.Combine(root, "Global");
        if (Directory.Exists(global))
            foreach (var file in Directory.GetFiles(global, "*.json", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                byId.TryAdd(System.IO.Path.GetFileNameWithoutExtension(file), file);
        return byId;
    }

    // This PC's file for a server file: the exact id, else the one id that differs only in case, never a guess between two.
    private static string? FindPcCopy(Dictionary<string, string> byId, string id)
    {
        if (byId.TryGetValue(id, out var file))
            return file;
        var twins = byId.Where(pair => pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).Take(2).ToList();
        return twins.Count == 1 ? twins[0] : null;
    }

    private static string TwinLine(string id, string other) =>
        Shown(id) + " differs only in capital letters from " + Shown(other) + ", so it was skipped.";

    /// <summary>A name from untrusted input as it may go into a line: no control or format characters, at most 120 characters.</summary>
    public static string Shown(string? text)
    {
        var clean = new string((text ?? "").Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse) && (c == ' ' || !char.IsSeparator(c))).ToArray());
        return clean.Length > 120 ? clean[..120] + "..." : clean;
    }

    // Windows device names, also with an extension (CON.json) and the superscript digits Windows treats the same.
    private static readonly Regex DeviceName = new(@"^(CON|PRN|AUX|NUL|COM[0-9\u00B9\u00B2\u00B3]|LPT[0-9\u00B9\u00B2\u00B3]|CONIN\$|CONOUT\$)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // MCM folders and ids are plain ASCII: letters, digits, _ - . and spaces, so no format character or look-alike letter gets in.
    private static bool PlainName(string name) =>
        name.Length is > 0 and <= 128 && name.Trim().Length == name.Length && !name.EndsWith('.')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ' ') && !DeviceName.IsMatch(name.Split('.')[0].TrimEnd());

    // The game's official modules (no MCM settings of their own) and the DedicatedServer.* host modules.
    private static readonly HashSet<string> Official = new[] { "Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle", "BirthAndDeath", "Multiplayer", "NavalDLC" }
        .Select(ModKey).ToHashSet(StringComparer.Ordinal);

    // By the matched name, so "Sand Box" or "sandbox" is official too.
    private static bool IsOfficial(string mod) =>
        Official.Contains(ModKey(mod)) || ModKey(mod).StartsWith("dedicatedserver", StringComparison.Ordinal);

    // A mod's name as matched: letters and digits only, lower case, without a leading "bannerlord".
    private static string ModKey(string name)
    {
        var key = ModCompatDatabase.Normalize(name ?? "");
        return key.StartsWith("bannerlord", StringComparison.Ordinal) && key.Length > "bannerlord".Length ? key["bannerlord".Length..] : key;
    }

    private static HashSet<string> ReadEdited(string serverRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = EditedListPath(serverRoot);
        if (!File.Exists(path))
            return set;
        // A list that can't be read is never written over from nothing: the caller stops (Seed only copies missing files).
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray list)
                throw new JsonException();
            foreach (var item in list)
                if (item is JsonValue value && value.TryGetValue<string>(out var relative) && relative.Length > 0)
                    set.Add(relative);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new IOException("The list of mod settings you changed here could not be read: " + path + ". Fix or delete that file, then try again.", ex);
        }

        return set;
    }

    private static void WriteEdited(string serverRoot, HashSet<string> set)
    {
        var path = EditedListPath(serverRoot);
        if (set.Count == 0)
        {
            File.Delete(path);
            return;
        }

        var list = new JsonArray(set.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
        WriteAtomic(path, Encoding.UTF8.GetBytes(list.ToJsonString(Indented)));
    }

    private static byte[] ReadCapped(Stream stream)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (copy.Length <= MaxFileBytes && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
            copy.Write(buffer, 0, read);
        return copy.ToArray();
    }

    // True when the JSON reads but a name or text in it can't be given back as a string. Broken JSON is not this: the list
    // and the sync leave such a file out by themselves.
    private static bool HasUnreadableText(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(WithoutBom(bytes));
            ReadAllText(document.RootElement);
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static byte[] WithoutBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private static bool SameFolder(string a, string b) =>
        System.IO.Path.GetFullPath(a).TrimEnd('\\', '/').Equals(System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // A temp file next to the target, then one replace: the server reads these files every few seconds and must never
    // see half of one. The temp name ends in .tmp, which the sync's *.json search leaves out, and goes when a step fails.
    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
