using System.Text.Json;

namespace TaomCoop.Core;

/// <summary>
/// The TAOM client launcher sets a Coop that isn't TAOM's 0.1.6 aside (CoopStaging.ExtraCoop, often a Coop 0.1.7 the
/// player unzipped for the Coop or AD1259 tools) only after a yes, and puts it back when the launcher closes. A marker
/// file lists each folder set aside and where it went, so a launcher that crashed or was killed puts it back on its
/// next start. A put back never overwrites a Modules folder that is there again: that copy stays parked for Restore
/// parked Coop. The Burning Empires client launcher sets its extra Coop copies aside the same way (Tool BE, its own
/// marker), without asking. Each parked folder also gets a tag file beside it in the game folder (TagSuffix), so the
/// folder still goes back when the marker in the tool's data folder is lost or another data folder was used.
/// </summary>
public static class CoopParking
{
    public const string MarkerFileName = "coop-set-aside.json";
    public const string TagSuffix = ".coodsz-put-back";
    public const string TaomTool = "TAOM";
    public const string BeTool = "BE";

    public const string AskTitle = "Set your Coop aside?";
    public const string SetAsideButton = "Set aside and play";
    public const string SetAsideTip = "Moves that Coop out of Modules now and puts it back when you close this launcher.";
    public const string CancelButton = "Cancel";
    public const string CancelTip = "Leaves Modules as it is. Connect stays off while the game loads that Coop.";
    public const string DontAskBox = "Don't ask again";
    // No word on Use Workshop Coop 0.1.6 here: with the Workshop on Coop 0.1.7 it stops before it sets anything aside.
    public const string DontAskTip = "Remembers this answer in this launcher.";

    // The saved answers (TaomClientSettings.SetAsideCoop). Anything else, an empty value too, means ask.
    public const string AlwaysAnswer = "always";
    public const string NeverAnswer = "never";

    /// <summary>The question for the folders CoopStaging.ExtraCoop found, each named by its id and version.</summary>
    public static string Question(IEnumerable<CoopStaging.CoopFolder> folders) =>
        "TAOM needs Coop " + TaomStack.CoopVersion.TrimStart('v') + ". Your " + string.Join(", ", folders.Select(folder => (folder.Id + " " + folder.Version).Trim()))
            + " in Modules will be set aside while this launcher runs, and put back when you close it. Continue?";

    /// <summary>
    /// The answer for this run: a saved answer wins, then the one given earlier this run, else the ask. Save is the
    /// value to write to the settings when Don't ask again was ticked, else null.
    /// </summary>
    public static SetAsideDecision Decide(string? saved, bool? thisRun, Func<(bool SetAside, bool DontAskAgain)> ask)
    {
        var answer = (saved ?? "").Trim();
        if (answer.Equals(AlwaysAnswer, StringComparison.OrdinalIgnoreCase))
            return new SetAsideDecision(true, null, false);
        if (answer.Equals(NeverAnswer, StringComparison.OrdinalIgnoreCase))
            return new SetAsideDecision(false, null, false);
        if (thisRun is { } given)
            return new SetAsideDecision(given, null, false);
        var (setAside, dontAskAgain) = ask();
        return new SetAsideDecision(setAside, dontAskAgain ? (setAside ? AlwaysAnswer : NeverAnswer) : null, true);
    }

    /// <summary>True when Decide would show the question (no saved answer and none given this run).</summary>
    public static bool NeedsAsk(string? saved, bool? thisRun)
    {
        var answer = (saved ?? "").Trim();
        return thisRun == null
            && !answer.Equals(AlwaysAnswer, StringComparison.OrdinalIgnoreCase)
            && !answer.Equals(NeverAnswer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CoopStaging.WrongWorkshopCoop when the Workshop Coop (its module folder) is newer than TAOM's Coop and the game's
    /// Modules folder has no Coop 0.1.6 of the player's own, else null. Setting a Coop aside can't make TAOM run then,
    /// so the launcher neither asks nor acts on a saved always, and shows this line instead. With a local Coop 0.1.6 the
    /// other copies still go aside, so that one loads.
    /// </summary>
    public static string? NothingToSetAsideFor(string? workshopModule, string? gameRoot) =>
        CoopStaging.HasLocalKitCoop(gameRoot) ? null : CoopStaging.NewerCoopProblem(workshopModule);

    /// <summary>
    /// Moves every folder CoopStaging.ExtraCoop finds into the game folder (Name-kept-version) and lists it in the
    /// marker and its tag first, so a crash during the move still leaves a marker and a tag the next start reads.
    /// </summary>
    public static IReadOnlyList<string> SetAside(string gameRoot, string markerPath, string tool = TaomTool) =>
        SetAside(gameRoot, markerPath, CoopStaging.ExtraCoop(gameRoot), tool);

    /// <summary>SetAside for folders the caller picked (BeCoopCopy picks its own).</summary>
    public static IReadOnlyList<string> SetAside(string gameRoot, string markerPath, IReadOnlyList<CoopStaging.CoopFolder> folders, string tool)
    {
        var entries = Read(markerPath).ToList();
        var moved = new List<string>();
        foreach (var folder in folders)
        {
            var entry = new SetAsideEntry(folder.Directory, CoopStaging.KeptPath(folder, gameRoot));
            // KeptPath's time stamped name can already exist within the same second; never touch that folder or its tag.
            if (Directory.Exists(entry.To) || File.Exists(entry.To))
                throw new IOException(Path.GetFileName(entry.To) + " is already in the game folder, so " + Path.GetFileName(entry.From) + " stays in Modules.");
            entries.Add(entry);
            Write(markerPath, entries);
            var createdTag = false;
            try
            {
                // A tag left over the same name by an older run names no folder (checked above), so it is stale.
                DeleteTag(entry.To);
                using (var tag = new FileStream(entry.To + TagSuffix, FileMode.CreateNew, FileAccess.Write))
                {
                    createdTag = true;
                    JsonSerializer.Serialize(tag, new SetAsideTag(entry.From, entry.To, tool, DateTime.UtcNow), WriteOptions);
                }

                Directory.Move(entry.From, entry.To);
            }
            catch
            {
                entries.Remove(entry);
                Write(markerPath, entries);
                if (createdTag)
                    DeleteTag(entry.To);
                throw;
            }

            moved.Add(entry.To);
        }

        return moved;
    }

    /// <summary>True when the marker or a tag of this tool in the game folder lists a folder that is still set aside.</summary>
    public static bool HasSetAside(string markerPath, string? gameRoot = null, string tool = TaomTool) =>
        Entries(markerPath, gameRoot, tool).Any(entry => Directory.Exists(entry.To));

    /// <summary>
    /// Puts every folder in the marker and in this tool's tags in the game folder back where it was and returns the
    /// lines to log. A folder that is gone (Restore parked Coop took it) is dropped. One whose Modules place is taken
    /// stays parked and is dropped too, with a line. One that could not move stays in the marker and keeps its tag, so
    /// the next start tries again. A tag goes once its folder is back or dropped.
    /// </summary>
    public static IReadOnlyList<string> PutBack(string markerPath, string? gameRoot = null, string tool = TaomTool)
    {
        var lines = new List<string>();
        var left = new List<SetAsideEntry>();
        foreach (var entry in Entries(markerPath, gameRoot, tool))
        {
            if (!Directory.Exists(entry.To))
            {
                DeleteTag(entry.To);
                continue;
            }

            var kept = Path.GetFileName(entry.To);
            var name = Path.GetFileName(entry.From);
            if (Directory.Exists(entry.From) || File.Exists(entry.From))
            {
                lines.Add("Modules\\" + name + " is there again, so " + kept + " stays in the game folder. Click Restore parked Coop in Settings if you want it back.");
                DeleteTag(entry.To);
                continue;
            }

            if (!Directory.Exists(Path.GetDirectoryName(entry.From)))
            {
                left.Add(entry);
                lines.Add("Could not put " + kept + " back, because the game's Modules folder is missing. The launcher tries again on its next start.");
                continue;
            }

            try
            {
                Directory.Move(entry.To, entry.From);
                lines.Add("Put " + kept + " back into Modules as " + name + ".");
                DeleteTag(entry.To);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Add(entry);
                lines.Add("Could not put " + kept + " back into Modules. " + ex.Message + " The launcher tries again on its next start.");
            }
        }

        if (left.Count > 0)
            Write(markerPath, left);
        else if (File.Exists(markerPath))
            File.Delete(markerPath);
        return lines;
    }

    /// <summary>The log line for one folder SetAside moved.</summary>
    public static string SetAsideLine(string parked) =>
        "Set " + Path.GetFileName(parked) + " aside in the game folder. It goes back into Modules when you close this launcher.";

    /// <summary>The log line when an extra Coop would be set aside while Bannerlord runs.</summary>
    public const string CloseGameFirst = "Close Bannerlord first, then TAOM can set the other Coop aside.";

    public const string WaitsForGame =
        "Bannerlord is running, so the Coop this launcher set aside stays out of Modules for now. It goes back when you close this launcher.";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // A missing or garbled marker lists nothing.
    private static IReadOnlyList<SetAsideEntry> Read(string markerPath)
    {
        try
        {
            if (!File.Exists(markerPath))
                return [];
            var entries = JsonSerializer.Deserialize<List<SetAsideEntry>>(File.ReadAllText(markerPath));
            return entries?.Where(entry => !string.IsNullOrWhiteSpace(entry?.From) && !string.IsNullOrWhiteSpace(entry?.To)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static void Write(string markerPath, IReadOnlyList<SetAsideEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markerPath))!);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(entries, WriteOptions));
    }

    // The marker's entries, then this tool's tags in the game folder the marker doesn't already list. An old marker
    // from before the tags works alone, and a tag alone brings its folder back when the marker is gone.
    private static List<SetAsideEntry> Entries(string markerPath, string? gameRoot, string tool)
    {
        var entries = Read(markerPath).Where(IsParkedCoop).ToList();
        foreach (var tag in ReadTags(gameRoot, tool))
        {
            if (!entries.Any(entry => SamePath(entry.To, tag.To)))
                entries.Add(tag);
        }

        return entries;
    }

    // Only the shape SetAside makes is moved back: a "-kept-" folder straight in a game folder, going back straight into
    // that game folder's Modules. Tags are files anyone can write in the game folder, so any other path in one (or in a
    // garbled marker) moves nothing (pr-reviewer, Codex).
    private static bool IsParkedCoop(SetAsideEntry entry)
    {
        try
        {
            var to = Path.GetFullPath(entry.To).TrimEnd('\\', '/');
            var from = Path.GetFullPath(entry.From).TrimEnd('\\', '/');
            var game = Path.GetDirectoryName(to);
            var modules = Path.GetDirectoryName(from);
            return game != null && modules != null
                && Path.GetFileName(to).Contains("-kept-", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(from).Length > 0
                && SamePath(modules, Path.Combine(game, "Modules"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // A tag counts only beside the folder it names, so a copied or renamed tag moves nothing. A garbled one is skipped.
    private static List<SetAsideEntry> ReadTags(string? gameRoot, string tool)
    {
        var found = new List<SetAsideEntry>();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return found;
        string[] files;
        try
        {
            files = Directory.GetFiles(gameRoot, "*" + TagSuffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        foreach (var file in files)
        {
            try
            {
                var tag = JsonSerializer.Deserialize<SetAsideTag>(File.ReadAllText(file));
                if (tag == null || string.IsNullOrWhiteSpace(tag.From) || string.IsNullOrWhiteSpace(tag.To)
                    || !string.Equals(tag.Tool, tool, StringComparison.OrdinalIgnoreCase) || !SamePath(tag.To + TagSuffix, file))
                    continue;
                var entry = new SetAsideEntry(tag.From, tag.To);
                if (IsParkedCoop(entry))
                    found.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            {
            }
        }

        return found;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // A tag that can't be deleted now is dropped by a later put back, once its folder is gone from where it names.
    private static void DeleteTag(string parked)
    {
        try
        {
            File.Delete(parked + TagSuffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>One folder set aside: where it was in Modules and where it is now.</summary>
public sealed record SetAsideEntry(string From, string To);

/// <summary>The tag file beside a parked folder (its path plus CoopParking.TagSuffix): the same From and To, the tool
/// that set it aside (TAOM or BE) and when, in UTC.</summary>
public sealed record SetAsideTag(string From, string To, string Tool, DateTime Utc);

/// <summary>What CoopParking.Decide picked. Save is the answer to remember, Asked is true when the window showed.</summary>
public readonly record struct SetAsideDecision(bool SetAside, string? Save, bool Asked);
