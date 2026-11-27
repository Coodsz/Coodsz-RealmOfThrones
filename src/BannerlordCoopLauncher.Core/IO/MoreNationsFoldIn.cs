using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Core.IO;

/// <summary>
/// Coodsz and Friends (the More Nations build) became the More Nations switch of the Coop client launcher.
/// Its settings, and those of a second client folder, join the client's own launcher.json once per source
/// folder. When the client has no settings yet the source is copied whole. Otherwise every setup of the
/// source that the client lacks is added, a setup of the same name that differs comes in as
/// "Name (More Nations)" or "Name (Coop)", and the whole source file is kept beside it as
/// launcher.more-nations.json or launcher.coop.json. Settings from a Coodsz and Friends folder turn the
/// switch on. A setup with no map saved gets the map it had in its own tool first, so no setup changes map.
/// Nothing is deleted, and an unreadable file is kept aside, never overwritten.
/// </summary>
public static class MoreNationsFoldIn
{
    public const string FriendsExe = "Coodsz and Friends.exe";
    public const string SettingsName = "launcher.json";
    public const string CacheName = "profile-cache.json";
    // One line per source folder already folded in.
    public const string DoneName = "settings-fold-in.txt";
    public const string MoreNationsSuffix = " (More Nations)";
    public const string CoopSuffix = " (Coop)";
    // Left in a Coodsz and Friends folder the first time it is seen, so a fold in that fails once still
    // knows the folder after ToolRename's cleanup removed Coodsz and Friends.exe and the rename marker.
    public const string NoteName = "coodsz-and-friends.txt";

    /// <summary>
    /// True when the client runs from a Coodsz and Friends folder: Coodsz and Friends.exe is beside it, the
    /// update that renamed it left its name in rename-cleanup.pending, or an earlier start left the note.
    /// Call it before ToolRename.OnStartup, which starts the cleanup that removes that exe. It writes the
    /// note the first time, best effort.
    /// </summary>
    public static bool CameFromFriends(string exeFolder)
    {
        bool found;
        try
        {
            if (File.Exists(Path.Combine(exeFolder, NoteName)))
                return true;
            var marker = Path.Combine(exeFolder, SuccessorInstall.RenameMarker);
            found = File.Exists(Path.Combine(exeFolder, FriendsExe))
                || (File.Exists(marker)
                    && File.ReadLines(marker).Any(line => Path.GetFileName(line.Trim().Trim('"')).Equals(FriendsExe, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        if (found)
        {
            try
            {
                File.WriteAllText(Path.Combine(exeFolder, NoteName),
                    "This folder had Coodsz and Friends.exe. Coodsz Coop Client Launcher brings its settings in with More Nations on." + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Best effort. The exe or the marker still tells this start.
            }
        }

        return found;
    }

    /// <summary>
    /// Folds the settings beside the exe, and those in the old More Nations data folder, into the client's
    /// folder. Each source folder once. Never throws: a failure leaves the files as they were, and the
    /// next start tries again.
    /// </summary>
    public static void Run(string clientFolder, string exeFolder, bool fromFriends, string? moreNationsFolder)
    {
        try
        {
            Directory.CreateDirectory(clientFolder);
            var done = DoneFolders(clientFolder);
            // That folder also holds payload files, so only its launcher.json makes it a source.
            if (moreNationsFolder != null && File.Exists(Path.Combine(moreNationsFolder, SettingsName)))
                FoldOne(clientFolder, moreNationsFolder, moreNations: true, done);
            FoldOne(clientFolder, exeFolder, fromFriends, done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log(clientFolder, "Could not fold in the settings from " + exeFolder + ". " + ex.Message);
        }
    }

    private static void FoldOne(string clientFolder, string sourceFolder, bool moreNations, HashSet<string> done)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder) || SameFolder(sourceFolder, clientFolder))
            return;
        var key = Normal(sourceFolder);
        if (done.Contains(key))
            return;
        var source = Path.Combine(sourceFolder, SettingsName);
        var target = Path.Combine(clientFolder, SettingsName);
        // A Coop folder with nothing to bring is skipped without a line, so a later launcher.json still comes in.
        if (!File.Exists(source) && !moreNations)
            return;

        var label = moreNations ? "More Nations" : "Coop";
        var lines = new List<string>();
        var current = Read(target, out var targetBroken);
        if (targetBroken)
        {
            var aside = Keep(target, clientFolder, "launcher.unreadable");
            lines.Add("Could not read " + SettingsName + ", so it was kept as " + Path.GetFileName(aside) + ".");
            current = null;
        }

        var incoming = File.Exists(source) ? Read(source, out _) : null;
        if (File.Exists(source) && incoming == null)
        {
            var aside = Keep(source, clientFolder, KeptBase(moreNations));
            lines.Add("Could not read the " + label + " " + SettingsName + " in " + sourceFolder + ". It was kept as " + Path.GetFileName(aside) + ".");
        }

        if (incoming != null)
            FillBlankMaps(incoming, moreNations ? StableClientSetup.MoreNationsMap : StableClientSetup.VanillaMap);

        var hadSettings = current != null;
        if (current == null)
        {
            current = incoming ?? new LauncherSettings();
            current.EnsureSetups();
            if (incoming != null)
                lines.Add("Took the " + label + " settings from " + sourceFolder + ".");
        }
        else if (incoming != null && !File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target)))
        {
            // Blank maps meant this client's own default before the switch can change.
            FillBlankMaps(current, current.DefaultMap());
            var added = Merge(current, incoming, moreNations ? MoreNationsSuffix : CoopSuffix);
            var kept = Keep(source, clientFolder, KeptBase(moreNations));
            lines.Add("Added " + added.Count + " setup(s) from the " + label + " settings in " + sourceFolder
                + (added.Count > 0 ? " (" + string.Join(", ", added) + ")" : "") + ". The whole file is kept as " + Path.GetFileName(kept) + ".");
        }
        else if (incoming != null)
            FillBlankMaps(current, current.DefaultMap());

        if (moreNations && !current.MoreNations)
        {
            // Settings that were here before meant the vanilla map by a blank one. Brand new ones keep the
            // blank, so their Default starts on More Nations like Coodsz and Friends did.
            if (hadSettings)
                FillBlankMaps(current, current.DefaultMap());
            current.MoreNations = true;
            lines.Add("Turned the More Nations switch on, because these settings come from Coodsz and Friends.");
        }

        Save(current, target);
        // Done as soon as the settings are saved, so nothing after this can make the next start merge again.
        File.AppendAllLines(Path.Combine(clientFolder, DoneName), [key]);
        done.Add(key);
        var cache = Path.Combine(sourceFolder, CacheName);
        var cacheTarget = Path.Combine(clientFolder, CacheName);
        try
        {
            if (File.Exists(cache) && !File.Exists(cacheTarget))
                File.Copy(cache, cacheTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a cache of the downloaded profile. The next download writes a new one.
            lines.Add("Could not copy " + CacheName + " from " + sourceFolder + ". " + ex.Message);
        }

        lines.ForEach(line => Log(clientFolder, line));
    }

    /// <summary>
    /// Adds the setups of incoming that target lacks. Locked setups stay target's, and only bring the
    /// server to join when target's has none. Blank fields of target take incoming's. Returns the names added.
    /// </summary>
    public static List<string> Merge(LauncherSettings target, LauncherSettings incoming, string suffix)
    {
        target.EnsureSetups();
        incoming.EnsureSetups();
        var added = new List<string>();
        foreach (var setup in incoming.Setups)
        {
            var same = target.Setups.FirstOrDefault(item => item.Name.Equals(setup.Name, StringComparison.OrdinalIgnoreCase));
            if (setup.Locked)
            {
                if (same != null && !HasJoin(same) && HasJoin(setup))
                    CopyJoin(setup, same);
                continue;
            }

            // Already here under any name (an earlier merge added it as "Name (More Nations)"), so it is not
            // added twice. Locked setups do not count: an empty unlocked setup can look like one but loads differently.
            if (target.Setups.Any(item => !item.Locked && SameSetup(item, setup)))
                continue;
            var name = same == null ? setup.Name : FreeName(target, setup.Name + suffix);
            target.Setups.Add(setup.Copy(name));
            added.Add(name);
        }

        if (string.IsNullOrWhiteSpace(target.GamePath))
            target.GamePath = incoming.GamePath;
        if (string.IsNullOrWhiteSpace(target.NexusApiKey))
            target.NexusApiKey = incoming.NexusApiKey;
        if ((string.IsNullOrWhiteSpace(target.ProfileUrl) || target.ProfileUrl == ProfileJson.DefaultProfileUrl)
            && !string.IsNullOrWhiteSpace(incoming.ProfileUrl))
            target.ProfileUrl = incoming.ProfileUrl;
        return added;
    }

    /// <summary>Gives each setup with no map saved the map it showed in its own tool.</summary>
    public static void FillBlankMaps(LauncherSettings settings, string map)
    {
        settings.EnsureSetups();
        foreach (var setup in settings.Setups.Where(item => string.IsNullOrWhiteSpace(item.CampaignMap)))
            setup.CampaignMap = map;
        if (string.IsNullOrWhiteSpace(settings.CampaignMap))
            settings.CampaignMap = map;
    }

    private static bool HasJoin(ClientSetup setup) =>
        !string.IsNullOrWhiteSpace(setup.SteamHostName) || !string.IsNullOrWhiteSpace(setup.DirectHost)
        || !string.IsNullOrWhiteSpace(setup.DirectPort) || !string.IsNullOrWhiteSpace(setup.ServerPassword);

    private static void CopyJoin(ClientSetup from, ClientSetup to)
    {
        to.UseDirectConnection = from.UseDirectConnection;
        to.SteamHostName = from.SteamHostName;
        to.DirectHost = from.DirectHost;
        to.DirectPort = from.DirectPort;
        to.ServerPassword = from.ServerPassword;
    }

    private static bool SameSetup(ClientSetup left, ClientSetup right) =>
        System.Text.Json.JsonSerializer.Serialize(left.Copy("")) == System.Text.Json.JsonSerializer.Serialize(right.Copy(""));

    private static string FreeName(LauncherSettings settings, string name)
    {
        var candidate = name;
        for (var number = 2; settings.Setups.Any(item => item.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = name + " " + number;
        return candidate;
    }

    private static LauncherSettings? Read(string path, out bool broken)
    {
        broken = false;
        if (!File.Exists(path))
            return null;
        try
        {
            return LauncherSettings.Load(path);
        }
        catch (Exception ex) when (ex is ProfileException or System.Text.Json.JsonException or NotSupportedException)
        {
            broken = true;
            return null;
        }
    }

    private static string KeptBase(bool moreNations) => moreNations ? "launcher.more-nations" : "launcher.coop";

    // A copy under the first free name, launcher.more-nations.json, launcher.more-nations-2.json and so on.
    // A kept copy with the same bytes is used again, so a retry never keeps the same file twice.
    private static string Keep(string file, string folder, string baseName)
    {
        var bytes = File.ReadAllBytes(file);
        var path = Path.Combine(folder, baseName + ".json");
        for (var number = 2; File.Exists(path); number++)
        {
            if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                return path;
            path = Path.Combine(folder, baseName + "-" + number + ".json");
        }

        File.Copy(file, path);
        return path;
    }

    // Written beside and then moved over, so a cut short write never leaves half a launcher.json.
    private static void Save(LauncherSettings settings, string path)
    {
        var partial = path + ".partial";
        settings.Save(partial);
        File.Move(partial, path, overwrite: true);
    }

    private static HashSet<string> DoneFolders(string clientFolder)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(clientFolder, DoneName);
        if (File.Exists(path))
            done.UnionWith(File.ReadLines(path).Where(line => line.Trim().Length > 0).Select(line => line.Trim()));
        // Folders an earlier build of this client already copied from count as done too.
        var folder = new ToolDataFolder(Path.GetDirectoryName(clientFolder.TrimEnd('\\', '/')) ?? clientFolder, Path.GetFileName(clientFolder.TrimEnd('\\', '/')));
        done.UnionWith(folder.CopiedFrom(ToolData.CoopClient.Key).Select(Normal));
        return done;
    }

    private static void Log(string clientFolder, string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(clientFolder, ToolDataFolder.LogName),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The log is best effort.
        }
    }

    private static string Normal(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/');

    private static bool SameFolder(string left, string right) =>
        Normal(left).Equals(Normal(right), StringComparison.OrdinalIgnoreCase);
}
