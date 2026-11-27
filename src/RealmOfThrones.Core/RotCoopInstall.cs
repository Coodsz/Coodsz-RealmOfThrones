using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>What this launcher has in the game's Modules folder: the RoT build of Coop it copied to Modules\Coop (Ours,
/// null when it used one that was there), every other Coop it parked in the game folder, and the id written into our
/// copy (Id, in OwnFileName), so a player's own copy of the same build is never taken for ours. A marker written before
/// the id has none and goes by the build.</summary>
public sealed record RotCoopMarker(string? Ours, List<SetAsideEntry>? Parked, string? Id = null);

/// <summary>What PutIn did: the line for under Connect when it stopped (Problem), and the lines for the log.</summary>
public sealed record RotPutIn(string? Problem, IReadOnlyList<string> Lines);

/// <summary>What TakeOut did: the line for under Connect the player has to act on (a Coop that stays parked, a marker
/// that could not be read), and the lines for the log.</summary>
public sealed record RotTakeOut(string? Notice, IReadOnlyList<string> Lines);

/// <summary>What closing the launcher does with our Coop in Modules (RotCoopInstall.CloseStep).</summary>
public enum RotCloseStep { TakeOut, Wait, LeaveForNextStart }

/// <summary>
/// The RoT build of Coop the Client Launcher carries (RotStack.CoopBuild.ClientZip, src\RotCoopBuild.targets) goes into
/// Modules\Coop at Connect and comes back out when the launcher closes. Burning Empires moves every CoopNightly out of
/// Modules (BeCoopCopy), TAOM asks to set aside any Coop that is not its own (CoopStaging) and Coop and Friends and AD1259
/// read a CoopNightly as their Coop, so our build is in Modules only while this launcher runs and none of them needs a
/// change. Any other Coop in Modules is parked in the game folder as Name-parked-by-rot-version (RotCoopRestore.ParkPath
/// with ParkTag, not -kept-, so Restore parked Coop in the TAOM and Burning Empires launchers never takes it) and put
/// back. The marker (rot-coop-in.json, the CoopParking way) is written whole (a .tmp file, then one move) before anything
/// moves, so a launcher that crashed or was killed is undone at its next start. Nothing here deletes a Coop of the
/// player's, reads or writes the Workshop folder or moves anything while Bannerlord runs. Our copy goes in and out through
/// Modules\.coodsz-rot-coop with its SubModule.xml in last and out first, so the game and the other tools never read a
/// half copy as a module, and Modules\Coop itself only ever changes by one rename.
/// </summary>
public static class RotCoopInstall
{
    public const string FolderName = "Coop";
    public const string MarkerFileName = "rot-coop-in.json";
    public const string DoneFileName = "unpacked.json";
    public const string WorkFolderName = ".coodsz-rot-coop";
    /// <summary>The file in our copy of Coop with the marker's Id.</summary>
    public const string OwnFileName = "coodsz-rot-coop.id";
    /// <summary>The tag of a folder this launcher parks (Coop-parked-by-rot-0.1.7). Markers of before list Name-kept-version.</summary>
    public const string ParkTag = "-parked-by-rot-";

    /// <summary>The embedded client zip (src\RotCoopBuild.targets names it Coodsz.RotCoop.&lt;zip name&gt;).</summary>
    public static string ResourceName => "Coodsz.RotCoop." + RotStack.CoopBuild.ClientZip;

    /// <summary>Where the carried zip is unpacked: CoopBuild\&lt;build&gt; in the tool's data folder (Coop and its LICENSE and NOTICE.md).</summary>
    public static string BuildFolder(string productFolder) => Path.Combine(productFolder, "CoopBuild", CoopBuildId.Short(RotStack.CoopBuild.Build));

    // ----- Unpacking the carried zip once -----

    private sealed record UnpackRecord(string Sha256, string Files);

    /// <summary>
    /// The carried Coop unpacked (buildFolder\Coop). The zip is unpacked only when the done record (buildFolder\unpacked.json,
    /// written after a complete unpack) is missing or no longer fits: another SHA256, or a Coop folder that lost or gained
    /// files since. The zip's SHA256 is checked against sha256 first, and a zip that differs throws InvalidDataException
    /// before anything is written.
    /// </summary>
    public static string Unpack(Stream zip, string sha256, string buildFolder)
    {
        var coop = Path.Combine(buildFolder, FolderName);
        if (Unpacked(buildFolder, sha256))
            return coop;
        using var copy = new MemoryStream();
        zip.CopyTo(copy);
        copy.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(copy));
        if (!hash.Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Coop build inside this launcher is damaged. Download the Client Launcher again.");

        var done = Path.Combine(buildFolder, DoneFileName);
        if (File.Exists(done))
            File.Delete(done);
        if (Directory.Exists(coop))
            Directory.Delete(coop, recursive: true);
        Directory.CreateDirectory(buildFolder);
        copy.Position = 0;
        using (var archive = new ZipArchive(copy, ZipArchiveMode.Read, leaveOpen: true))
            archive.ExtractToDirectory(buildFolder, overwriteFiles: true);
        if (!File.Exists(Path.Combine(coop, "SubModule.xml")))
            throw new InvalidDataException("The Coop build inside this launcher has no Coop folder. Download the Client Launcher again.");
        File.WriteAllText(done, JsonSerializer.Serialize(new UnpackRecord(hash, Stamp(coop))));
        return coop;
    }

    private static bool Unpacked(string buildFolder, string sha256)
    {
        try
        {
            var done = Path.Combine(buildFolder, DoneFileName);
            var coop = Path.Combine(buildFolder, FolderName);
            if (!File.Exists(done) || !File.Exists(Path.Combine(coop, "SubModule.xml")))
                return false;
            var record = JsonSerializer.Deserialize<UnpackRecord>(File.ReadAllText(done));
            return record != null && record.Sha256.Equals(sha256.Trim(), StringComparison.OrdinalIgnoreCase) && record.Files == Stamp(coop);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    // The number of files and their total size, enough to see a folder an unpack did not finish or someone changed.
    private static string Stamp(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => new FileInfo(file)).ToList();
        return files.Count + " files, " + files.Sum(file => file.Length) + " bytes";
    }

    // ----- Lines -----

    private static string Label => RotStack.CoopBuild.Label;

    /// <summary>The log line at Connect when this launcher was built without the zip (a Debug build).</summary>
    public const string NotCarried = "This launcher was built without the RoT build of Coop, so Connect uses the Coop in the game's Modules folder.";

    public static string CouldNotUnpack(string reason) =>
        "Could not get Coop " + Label + " ready, so the game was not started. " + reason;

    public static string GameRunning =>
        "Bannerlord is running, so the launcher can not put Coop " + Label + " in the game's Modules folder. Close the game, then click Connect again.";

    public static string WaitsForGame =>
        "Bannerlord is running, so Coop " + Label + " stays in the game's Modules folder for now. It comes back out when you close this launcher.";

    public const string Taken =
        "Modules\\Coop holds a folder that is not Coop, so the launcher left it as it is. Move that folder out of the game's Modules folder, then click Connect again.";

    public static string Declined =>
        "Your other Coop stays in Modules, so the game was not started. Realm of Thrones only runs on Coop " + Label + " that this launcher carries.";

    /// <summary>The window title while a closed launcher waits for Bannerlord in the taskbar.</summary>
    public const string WaitingTitle = "Waiting for Bannerlord to close, then Coop is put back";

    public static string WaitingToTakeOut =>
        "Waiting for Bannerlord to close. Then the launcher takes Coop " + Label + " out of the game's Modules folder, puts back any Coop it set aside and closes. Close this window again to leave it all for the next start.";

    public static string StillIn(string ours) =>
        "The launcher still has Coop " + Label + " in " + ours + ". Close the game, then open this launcher again to take it out.";

    public static string Question(IEnumerable<RotCoopFolder> folders) =>
        "Realm of Thrones runs on Coop " + Label + " that this launcher carries. Your "
            + string.Join(", ", folders.Select(folder => (folder.ModuleId + " " + RotCoopRestore.VersionOf(folder.Directory)).Trim()))
            + " in Modules will be set aside while this launcher runs, and put back when you close it. Continue?";

    public static string PutInLine => "Put Coop " + Label + " in the game's Modules folder. It comes back out when you close this launcher.";

    public static string TakenOut => "Took Coop " + Label + " back out of the game's Modules folder.";

    /// <summary>The Active Coop line on Play while this launcher carries the RoT build and it is not in Modules yet.
    /// The Coop in Modules before Connect is set aside then, so naming it would point at the wrong one.</summary>
    public static string ActiveCarried => "Active Coop: " + Label + ", which Connect puts in the game's Modules folder.";

    /// <summary>The Active Coop line on Play while the RoT build is in Modules\Coop (its OwnFileName is there).</summary>
    public static string ActiveIn => "Active Coop: " + Label + ", in the game's Modules folder until you close this launcher.";

    public static string CouldNotPutIn(string reason) =>
        "Could not put Coop " + Label + " in the game's Modules folder, so the game was not started. " + reason + " Close anything that uses the Modules folder, then click Connect again.";

    public static string CouldNotTakeOut(string reason) =>
        "Could not take Coop " + Label + " out of the game's Modules folder. " + reason + " The launcher tries again on its next start.";

    /// <summary>A parked Coop whose place in Modules is taken: it stays parked and in the marker, so the next start tries again.</summary>
    public static string StaysParked(string name, string parked) =>
        "Your " + name + " is in " + parked + ", because something else is in Modules\\" + name + " now. Move that out of Modules and the launcher puts yours back when it next starts.";

    public static string MarkerGarbled(string keptAs) =>
        "The launcher could not read its record of the Coop folders it moved, so it left the game's Modules folder as it is. The record is kept as " + keptAs + ". If your own Coop is gone from Modules, look for it in the game folder.";

    public static string MarkerUnreadable(string reason) =>
        "The launcher could not read its record of the Coop folders it moved, so it left the game's Modules folder as it is. " + reason + " The launcher tries again on its next start.";

    // ----- Putting it in at Connect -----

    /// <summary>
    /// At Connect, before the checks. A Modules\Coop that already is the RoT build (CoopModuleId and the build of
    /// RotStack.CoopBuild) is used as it is and stays the player's. Every other Coop in Modules is parked after setAside
    /// says yes, then the carried copy goes in. Stops with a Problem line while Bannerlord runs, when Modules\Coop holds a
    /// folder that is not a Coop module, or when setAside says no. A step that fails undoes the ones before it. buildOf
    /// reads a Coop folder's build (CoopBuildId.FromModule; tests pass their own).
    /// </summary>
    public static RotPutIn PutIn(string gameRoot, string carried, string markerPath, bool gameRunning,
        Func<IReadOnlyList<RotCoopFolder>, bool> setAside, Func<string, string?>? buildOf = null)
    {
        buildOf ??= CoopBuildId.FromModule;
        var target = Path.Combine(gameRoot, "Modules", FolderName);
        var found = RotCoopRestore.InModules(gameRoot);
        var ours = found.FirstOrDefault(folder => SamePath(folder.Directory, target) && IsOurs(folder.Directory, folder.ModuleId, buildOf));
        var others = found.Where(folder => folder != ours).ToList();
        if (ours != null && others.Count == 0)
            return new RotPutIn(null, []);
        if (gameRunning)
            return new RotPutIn(GameRunning, []);
        if (ours == null && !others.Any(folder => SamePath(folder.Directory, target)) && (Directory.Exists(target) || File.Exists(target)))
            return new RotPutIn(Taken, []);
        var marker = Read(markerPath, out var unreadable);
        if (unreadable != null)
            return new RotPutIn(unreadable, []);
        if (marker.Ours != null && !SamePath(marker.Ours, target))
            return new RotPutIn(StillIn(marker.Ours), []);
        if (others.Count > 0 && !setAside(others))
            return new RotPutIn(Declined, []);

        var lines = new List<string>();
        var parked = marker.Parked!;
        try
        {
            foreach (var other in others)
            {
                var entry = new SetAsideEntry(other.Directory, RotCoopRestore.ParkPath(gameRoot, other.Directory, RotCoopRestore.VersionOf(other.Directory), ParkTag));
                parked.Add(entry);
                Write(markerPath, marker);
                try
                {
                    Directory.Move(entry.From, entry.To);
                }
                catch
                {
                    parked.Remove(entry);
                    Write(markerPath, marker);
                    throw;
                }

                lines.Add(CoopParking.SetAsideLine(entry.To));
            }

            if (ours == null)
            {
                var id = Guid.NewGuid().ToString("N");
                Write(markerPath, marker = marker with { Ours = target, Id = id });
                var work = WorkFolder(target);
                RemoveWork(work);
                CopyModule(carried, work, id);
                Directory.Move(work, target);
                lines.Add(PutInLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Undone right away: our copy comes back out and what was parked goes back in.
            var undo = TakeOut(markerPath, gameRunning: false, buildOf);
            return new RotPutIn(CouldNotPutIn(ex.Message), lines.Concat(undo.Lines).Concat(undo.Notice is { } notice ? [notice] : []).ToList());
        }

        return new RotPutIn(null, lines);
    }

    // ----- Taking it out at close and after a crash -----

    /// <summary>True when the marker lists our copy or a parked folder that is still in the game folder, or cannot be read.</summary>
    public static bool HasWork(string markerPath)
    {
        var marker = Read(markerPath, out var unreadable, keepBad: false);
        return unreadable != null || marker.Ours != null || marker.Parked!.Any(entry => Directory.Exists(entry.To));
    }

    /// <summary>
    /// Takes our copy out of Modules\Coop (only when it holds the marker's Id, or for a marker of before without one
    /// when it still is the RoT build, so a Coop someone put there since stays) and puts every parked folder back where
    /// it was, then clears the marker. A parked folder whose place is taken stays parked and in the marker, with the
    /// Notice that says where it is, so the next start tries again. A step that fails (a locked file) stays in the marker
    /// with a line, so the next start tries again. While Bannerlord runs nothing moves, and nothing moves on a marker
    /// that cannot be read either.
    /// </summary>
    public static RotTakeOut TakeOut(string markerPath, bool gameRunning, Func<string, string?>? buildOf = null)
    {
        buildOf ??= CoopBuildId.FromModule;
        var marker = Read(markerPath, out var unreadable);
        if (unreadable != null)
            return new RotTakeOut(unreadable, []);
        var lines = new List<string>();
        if (marker.Ours != null || marker.Parked!.Count > 0)
        {
            if (gameRunning)
                return new RotTakeOut(null, [WaitsForGame]);
        }

        if (marker.Ours is { } ours)
        {
            try
            {
                var work = WorkFolder(ours);
                var moved = false;
                if (Directory.Exists(ours) && (marker.Id != null ? HasId(ours, marker.Id) : IsOurs(ours, null, buildOf)))
                {
                    RemoveWork(work);
                    Directory.Move(ours, work);
                    moved = true;
                }

                RemoveWork(work);
                marker = marker with { Ours = null, Id = null };
                if (moved)
                    lines.Add(TakenOut);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines.Add(CouldNotTakeOut(ex.Message));
            }
        }

        var left = new List<SetAsideEntry>();
        var notices = new List<string>();
        foreach (var entry in marker.Parked!)
        {
            if (!Directory.Exists(entry.To))
                continue;
            var kept = Path.GetFileName(entry.To);
            var name = Path.GetFileName(entry.From);
            if (Directory.Exists(entry.From) || File.Exists(entry.From))
            {
                // Our copy could not come out yet, or something else took the place: the parked one waits for the
                // next try, and the player is told where it is when it is not just our copy in the way.
                left.Add(entry);
                if (marker.Ours == null || !SamePath(entry.From, marker.Ours))
                    notices.Add(StaysParked(name, entry.To));
                continue;
            }

            try
            {
                Directory.Move(entry.To, entry.From);
                lines.Add("Put " + kept + " back into Modules as " + name + ".");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Add(entry);
                lines.Add("Could not put " + kept + " back into Modules. " + ex.Message + " The launcher tries again on its next start.");
            }
        }

        if (marker.Ours != null || left.Count > 0)
            Write(markerPath, marker with { Parked = left });
        else if (File.Exists(markerPath))
            File.Delete(markerPath);
        return new RotTakeOut(notices.Count == 0 ? null : string.Join(" ", notices), lines);
    }

    // ----- Deciding at close -----

    /// <summary>A game this launcher started a moment ago may not show its process yet, so the close counts it as running.</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Why the close waits for the game, or null when Coop can come out now: a Bannerlord process, a program from
    /// Modules\Coop\bin by its name (coopProgram, like Coop.CrashReporter, which the game starts when it crashes or is
    /// killed and which holds files there for about half a minute, so the move out fails), a game this launcher started
    /// less than StartGrace ago (startedExited null when it can not be told, true once that process has exited, so a game
    /// that is already gone ends the grace), or the TaleWorlds launcher.
    /// </summary>
    public static string? WaitReason(bool gameProcess, string? coopProgram, bool? startedExited, TimeSpan sinceStart, bool taleWorldsLauncher)
    {
        if (gameProcess)
            return "game process";
        if (coopProgram != null)
            return "Coop program " + coopProgram;
        if (startedExited != true && sinceStart < StartGrace)
            return "starting grace";
        return taleWorldsLauncher ? "TaleWorlds launcher" : null;
    }

    /// <summary>What a close does: with nothing to wait for Coop comes out and the window closes. Otherwise the first close
    /// waits for the game, and a second close while it waits leaves Coop for the next start.</summary>
    public static RotCloseStep CloseStep(bool waiting, string? waitReason) =>
        waitReason == null ? RotCloseStep.TakeOut : waiting ? RotCloseStep.LeaveForNextStart : RotCloseStep.Wait;

    // ----- Helpers -----

    private static bool IsOurs(string directory, string? moduleId, Func<string, string?> buildOf)
    {
        try
        {
            moduleId ??= ModuleIdentityReader.Read(Path.Combine(directory, "SubModule.xml")).Id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }

        return moduleId.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && CoopBuildId.Same(buildOf(directory), RotStack.CoopBuild.Build);
    }

    private static bool HasId(string directory, string id)
    {
        try
        {
            var file = Path.Combine(directory, OwnFileName);
            return File.Exists(file) && File.ReadAllText(file).Trim() == id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string WorkFolder(string target) => Path.Combine(Path.GetDirectoryName(target)!, WorkFolderName);

    // Removes the work folder only: its name and its Modules parent are checked first, SubModule.xml goes first, and a
    // link is removed without following it.
    private static void RemoveWork(string work)
    {
        if (!Path.GetFileName(work).Equals(WorkFolderName, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(Path.GetDirectoryName(work) ?? "").Equals("Modules", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The launcher did not remove " + work + ", because it is not its own work folder.");
        if (!Directory.Exists(work))
            return;
        if (new DirectoryInfo(work).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(work);
            return;
        }

        var xml = Path.Combine(work, "SubModule.xml");
        if (File.Exists(xml))
            File.Delete(xml);
        Directory.Delete(work, recursive: true);
    }

    // Copies a module folder with the marker's id in OwnFileName, its SubModule.xml last.
    private static void CopyModule(string source, string destination, string id)
    {
        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, folder)));
        var xml = Path.Combine(source, "SubModule.xml");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(file => !SamePath(file, xml)))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        File.WriteAllText(Path.Combine(destination, OwnFileName), id);
        File.Copy(xml, Path.Combine(destination, "SubModule.xml"), overwrite: true);
    }

    private static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // A missing marker lists nothing. One that cannot be read lists nothing too, with the line why in unreadable, and
    // the callers move nothing then. A garbled one is kept aside as rot-coop-in.json.bad first (keepBad), so the line
    // shows once and the player's Modules folder is left as it is.
    private static RotCoopMarker Read(string markerPath, out string? unreadable, bool keepBad = true)
    {
        unreadable = null;
        try
        {
            if (!File.Exists(markerPath))
                return new RotCoopMarker(null, []);
            if (JsonSerializer.Deserialize<RotCoopMarker>(File.ReadAllText(markerPath)) is not { } marker)
                throw new JsonException();
            return marker with
            {
                Ours = string.IsNullOrWhiteSpace(marker.Ours) ? null : marker.Ours,
                Parked = (marker.Parked ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry?.From) && !string.IsNullOrWhiteSpace(entry?.To)).ToList(),
                Id = string.IsNullOrWhiteSpace(marker.Id) ? null : marker.Id.Trim()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable = MarkerUnreadable(ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            var bad = markerPath + ".bad";
            unreadable = MarkerGarbled(bad);
            if (keepBad)
            {
                try
                {
                    File.Move(markerPath, bad, overwrite: true);
                }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    unreadable = MarkerUnreadable(moveEx.Message);
                }
            }
        }

        return new RotCoopMarker(null, []);
    }

    // Written whole: a .tmp file first, then one move over the marker, so a crash never leaves half a marker.
    private static void Write(string markerPath, RotCoopMarker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markerPath))!);
        var tmp = markerPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(marker, WriteOptions));
        File.Move(tmp, markerPath, overwrite: true);
    }
}
