using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Core.IO;

/// <summary>
/// One tool's own files: which product folder they go in, the Server or Client subfolder when the two
/// tools of a product write a file of the same name, and the names that used to sit beside the exe.
/// </summary>
public sealed record ToolDataPlan(string Product, string? Role, string Key, IReadOnlyList<string> OldFiles);

/// <summary>
/// %LOCALAPPDATA%\Coodsz\&lt;Product&gt;, the folder one product's tools keep their settings in, with
/// payload\&lt;version&gt; under it for the modules the exe writes out (ToolPayload).
/// </summary>
public sealed class ToolDataFolder
{
    public const string MarkerPrefix = "copied-from-exe-folder-";

    public ToolDataFolder(string root, string product, string? role = null)
    {
        Root = root;
        Product = product;
        Role = role;
    }

    public string Root { get; }
    public string Product { get; }
    public string? Role { get; }

    public string ProductFolder => Path.Combine(Root, Product);

    /// <summary>Where this tool's settings go.</summary>
    public string Folder => Role == null ? ProductFolder : Path.Combine(ProductFolder, Role);

    public string PayloadFolder(string version) => Path.Combine(ProductFolder, "payload", version);

    public string MarkerPath(string key) => Path.Combine(Folder, MarkerPrefix + key + ".txt");

    /// <summary>The exe folders CopyOldFiles already took this key's files from, read from its marker.</summary>
    public IReadOnlyList<string> CopiedFrom(string key)
    {
        const string from = " copied from ";
        const string newer = " newer copies from ";
        var marker = MarkerPath(key);
        if (!File.Exists(marker))
            return [];
        var folders = new List<string>();
        foreach (var line in File.ReadLines(marker))
        {
            var at = line.IndexOf(from, StringComparison.Ordinal);
            var length = from.Length;
            if (at < 0)
            {
                at = line.IndexOf(newer, StringComparison.Ordinal);
                length = newer.Length;
            }

            if (at >= 0)
                folders.Add(line[(at + length)..]);
        }

        return folders;
    }

    public const string LogName = "settings-move.log";
    private const string LeftPrefix = "left ";

    /// <summary>
    /// Generic folder names that are copied only when they hold one of these files of ours, so a stranger's
    /// Data folder beside the exe never comes along.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> OwnFileSigns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Data"] = ["modtest-results.json", "modtest-compat-mods.json"],
        ["ModSettings"] = ["*.overrides.json", "*.last-settings.json"]
    };

    // A folder bigger than this is left where it is, with a line in the log. Tests lower it.
    internal static long MaxFolderBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>
    /// The first start after the move copies each old file or folder from beside the exe that has no copy
    /// here yet, then writes the marker. A copy, not a move, so an older version started from the exe folder
    /// still finds its settings. A file already here wins. On later starts, and only while an old name exe
    /// of this tool still sits beside it (Coodsz and Friends.exe too, an old client name), a file beside the
    /// exe that changed after the marker and is newer than the copy here (that older version was used in
    /// between) replaces it, and the replaced copy is kept as &lt;name&gt;.bak. Once the old exe is gone the
    /// per user copy here always wins, so a shared exe folder never overrides it. Every copy goes in
    /// settings-move.log. Returns the paths copied. A failure leaves the first marker unwritten, so the next
    /// start retries. currentExeName defaults to the running exe.
    /// </summary>
    public IReadOnlyList<string> CopyOldFiles(string exeFolder, string key, IEnumerable<string> names, string? currentExeName = null)
    {
        var copied = new List<string>();
        Directory.CreateDirectory(Folder);
        if (SameFolder(exeFolder, Folder))
            return copied;
        var marker = MarkerPath(key);
        var log = new List<string>();
        // Folders already found too big: a "left <name>" line in the marker, so they are not summed or logged again.
        var left = File.Exists(marker)
            ? File.ReadLines(marker).Where(line => line.StartsWith(LeftPrefix, StringComparison.Ordinal)).Select(line => line[LeftPrefix.Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newlyLeft = new List<string>();
        if (!File.Exists(marker))
        {
            var failed = false;
            foreach (var name in names)
            {
                try
                {
                    if (CopyFirst(Path.Combine(exeFolder, name), Path.Combine(Folder, name), name, log, left, newlyLeft))
                        copied.Add(name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed = true;
                    log.Add("Could not copy " + name + ". " + ex.Message);
                }
            }

            if (!failed)
                File.WriteAllLines(marker, [Stamp() + " copied from " + exeFolder, .. copied, .. newlyLeft.Select(name => LeftPrefix + name)]);
        }
        else if (OldExeBeside(exeFolder, currentExeName ?? Path.GetFileName(Environment.ProcessPath ?? "")))
        {
            var since = File.GetLastWriteTimeUtc(marker);
            foreach (var name in names)
            {
                try
                {
                    copied.AddRange(TakeNewer(Path.Combine(exeFolder, name), Path.Combine(Folder, name), name, since, log, left, newlyLeft));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.Add("Could not copy " + name + ". " + ex.Message);
                }
            }

            // The marker's time moves on, so the same file is not taken twice.
            if (copied.Count > 0 || newlyLeft.Count > 0)
                File.AppendAllLines(marker, [Stamp() + " newer copies from " + exeFolder, .. copied, .. newlyLeft.Select(name => LeftPrefix + name)]);
        }

        WriteLog(log);
        return copied;
    }

    // An old name exe of the running tool beside it (ToolNames: CoodszHost.exe, Coodsz Coop and Friends.exe,
    // Coodsz and Friends.exe and the like). Only that older version can have changed the files there.
    private static bool OldExeBeside(string exeFolder, string currentExeName)
    {
        try
        {
            return ToolRename.OldExePaths(exeFolder, currentExeName).Any(File.Exists);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private bool CopyFirst(string source, string target, string name, List<string> log, HashSet<string> left, List<string> newlyLeft)
    {
        if (File.Exists(source) && !File.Exists(target) && !Directory.Exists(target))
        {
            File.Copy(source, target, overwrite: false);
            log.Add("Copied " + name + " from beside the exe.");
            return true;
        }

        if (!Directory.Exists(source) || Directory.Exists(target) || File.Exists(target) || !IsOurs(source, name, log, left, newlyLeft))
            return false;
        // Into a side folder first, so a copy cut short never looks like a finished one.
        var partial = target + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);
        CopyFolder(source, partial);
        Directory.Move(partial, target);
        log.Add("Copied the " + name + " folder from beside the exe.");
        return true;
    }

    private static List<string> TakeNewer(string source, string target, string name, DateTime since, List<string> log, HashSet<string> left, List<string> newlyLeft)
    {
        var pairs = new List<(string From, string To, string Label)>();
        if (File.Exists(source))
            pairs.Add((source, target, name));
        else if (Directory.Exists(source) && IsOurs(source, name, log, left, newlyLeft))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                pairs.Add((file, Path.Combine(target, relative), Path.Combine(name, relative)));
            }
        }

        var taken = new List<string>();
        foreach (var (from, to, label) in pairs)
        {
            var changed = File.GetLastWriteTimeUtc(from);
            if (changed <= since || (File.Exists(to) && changed <= File.GetLastWriteTimeUtc(to)))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to))
            {
                File.Copy(to, to + ".bak", overwrite: true);
                log.Add("Kept the copy of " + label + " here as " + Path.GetFileName(to) + ".bak.");
            }

            File.Copy(from, to, overwrite: true);
            log.Add("Took the newer " + label + " from beside the exe, changed by an older version.");
            taken.Add(label);
        }

        return taken;
    }

    // A generic folder name needs one of our own files in it, and no folder is copied past the size cap.
    // A folder found too big is noted once and never summed again.
    private static bool IsOurs(string source, string name, List<string> log, HashSet<string> left, List<string> newlyLeft)
    {
        if (left.Contains(name))
            return false;
        if (OwnFileSigns.TryGetValue(name, out var signs)
            && !signs.Any(sign => Directory.EnumerateFiles(source, sign, SearchOption.TopDirectoryOnly).Any()))
            return false;
        var bytes = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        if (bytes <= MaxFolderBytes)
            return true;
        log.Add("Left the " + name + " folder beside the exe. It holds " + bytes / (1024 * 1024) + " MB, more than a settings folder should.");
        newlyLeft.Add(name);
        return false;
    }

    private void WriteLog(List<string> lines)
    {
        if (lines.Count == 0)
            return;
        try
        {
            File.AppendAllLines(Path.Combine(Folder, LogName), lines.Select(line => Stamp() + " " + line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is best effort.
        }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static bool SameFolder(string left, string right) =>
        Path.GetFullPath(left).TrimEnd('\\', '/').Equals(Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The folder the running tool keeps its own files in. Each App calls <see cref="Start"/> first. Until
/// then, and when %LOCALAPPDATA% cannot be written, everything stays beside the exe like before.
/// The files shared by every tool in %APPDATA%\Coodsz (the license answer, the language) do not move.
/// </summary>
public static class ToolData
{
    public const string Coop = "Coop";
    public const string Taom = "TAOM";
    public const string AD1259 = "AD1259";
    public const string BurningEmpires = "BurningEmpires";
    public const string RealmOfThrones = "RealmOfThrones-Contributor";
    // The folder test builds of Coodsz and Friends (the More Nations build) used. That build is gone: the
    // Coop client launcher reads the files here once (MoreNationsFoldIn) and never writes to it.
    public const string MoreNations = "MoreNations";

    // Coodsz Host and Coodsz AD1259 Server: one source, so one list.
    private static readonly string[] HostFiles =
        ["host.json", "cheats.json", "hero-names.json", "save-settings", "ModSettings", "McmProfiles", "Data"];

    public static readonly ToolDataPlan CoopServer = new(Coop, null, "coop-server", HostFiles);
    public static readonly ToolDataPlan CoopClient = new(Coop, null, "coop-client", ["launcher.json", "profile-cache.json"]);
    public static readonly ToolDataPlan MoreNationsClient = new(MoreNations, null, "more-nations-client", ["launcher.json", "profile-cache.json"]);
    public static readonly ToolDataPlan AD1259Server = new(AD1259, null, "ad1259-server", HostFiles);
    public static readonly ToolDataPlan AD1259Client = new(AD1259, null, "ad1259-client", ["ad1259-launcher.json"]);
    // Both TAOM tools keep a taom-mod-compat.json, so each has a subfolder.
    public static readonly ToolDataPlan TaomServer = new(Taom, "Server", "taom-server", ["taom-server.json", "taom-cheats.json", "taom-mod-compat.json"]);
    public static readonly ToolDataPlan TaomClient = new(Taom, "Client", "taom-client", ["taom-launcher.json", "taom-mod-compat.json"]);
    // Both Burning Empires tools keep a be-mod-compat.json too, so each has a subfolder.
    public static readonly ToolDataPlan BeServer = new(BurningEmpires, "Server", "be-server", ["be-server.json", "be-cheats.json", "be-bans.json", "be-mod-compat.json"]);
    public static readonly ToolDataPlan BeClient = new(BurningEmpires, "Client", "be-client", ["be-launcher.json", "be-mod-compat.json", "be-host-mod-list.json"]);
    // The Realm of Thrones tools, in subfolders like the Burning Empires ones. rot-host-mod-list.json is the host list the launcher compares at Connect.
    public static readonly ToolDataPlan RotServer = new(RealmOfThrones, "Server", "rot-server", ["rot-server.json"]);
    public static readonly ToolDataPlan RotClient = new(RealmOfThrones, "Client", "rot-client", ["rot-launcher.json", "rot-host-mod-list.json"]);

    // Debug builds only: COODSZ_TEST_DATA_ROOT points live tests at a scratch folder, so a Debug run never
    // writes the settings Coodsz's installed tools use.
#if DEBUG
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("COODSZ_TEST_DATA_ROOT") is { Length: > 0 } scratch
            ? scratch
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coodsz");
#else
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coodsz");
#endif

    // The roaming Coodsz folder every tool shares: the language pick, what's new markers, caches and logs. A Debug
    // run with COODSZ_TEST_DATA_ROOT uses a Roaming folder under it, so a live test leaves this alone too.
#if DEBUG
    public static string RoamingRoot =>
        Environment.GetEnvironmentVariable("COODSZ_TEST_DATA_ROOT") is { Length: > 0 } scratch
            ? Path.Combine(scratch, "Roaming")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coodsz");
#else
    public static string RoamingRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coodsz");
#endif

    public static ToolDataFolder? Current { get; private set; }

    /// <summary>The running tool's plan (its key names its own files, like report-replies-taom-client.json), or null before Start.</summary>
    public static ToolDataPlan? CurrentPlan { get; private set; }

    /// <summary>The running tool's folder, or the exe folder before Start or when AppData failed.</summary>
    public static string Folder => Current?.Folder ?? AppContext.BaseDirectory;

    public static string File(string name) => Path.Combine(Folder, name);

    /// <summary>One line in this tool's settings-move.log. Nothing before Start. Best effort.</summary>
    public static void Log(string line)
    {
        if (Current == null)
            return;
        try
        {
            System.IO.File.AppendAllText(Path.Combine(Current.Folder, ToolDataFolder.LogName),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The settings folder of another tool of the same family, for reading its files.</summary>
    public static string FolderOf(ToolDataPlan plan) => new ToolDataFolder(DefaultRoot, plan.Product, plan.Role).Folder;

    /// <summary>
    /// Picks this tool's folder, copies its old files over once, and writes out the modules the exe
    /// carries in the background (ToolPayload). Never throws.
    /// </summary>
    public static void Start(ToolDataPlan plan)
    {
        try
        {
            var folder = new ToolDataFolder(DefaultRoot, plan.Product, plan.Role);
            folder.CopyOldFiles(AppContext.BaseDirectory, plan.Key, plan.OldFiles);
            Current = folder;
            CurrentPlan = plan;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Current = null;
            return;
        }

        Task.Run(() =>
        {
            try
            {
                ToolPayload.WriteOutAll();
            }
            catch (Exception)
            {
                // Best effort; the first real use tries again.
            }
        });
    }
}
