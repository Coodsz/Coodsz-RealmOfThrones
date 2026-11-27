using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>A Coop module folder: in the game's Modules folder, or kept in the game folder as Name-kept-version.</summary>
public sealed record RotCoopFolder(string Directory, string ModuleId, string? Build);

public enum RotRestoreAction
{
    /// <summary>The pinned build is in Modules, or no kept folder has it.</summary>
    Nothing,
    /// <summary>Move the kept folder back into Modules.</summary>
    Restore,
    /// <summary>Park the other Coop in Modules first, then move the kept folder back.</summary>
    ParkThenRestore,
    /// <summary>A kept folder has the pinned build, but the game runs, so nothing moves.</summary>
    GameRunning
}

/// <param name="Kept">The kept folder with the pinned build, when there is one.</param>
/// <param name="Park">The Coop folders in Modules to park first (ParkThenRestore).</param>
public sealed record RotRestorePlan(RotRestoreAction Action, RotCoopFolder? Kept, IReadOnlyList<RotCoopFolder> Park);

/// <summary>
/// The Burning Empires and TAOM launchers move a Coop nightly out of the game's Modules folder into the game folder
/// as Coop-kept-0.1.8 (BeCoopCopy, CoopStaging). At every Connect, before its checks, the Realm of Thrones launcher
/// puts the pinned Coop build (RotStack.CoopBuild.Build) back when Modules has no Coop of that build and a kept folder has
/// it. It only moves folders, never deletes one, and never while the game runs. Another Coop in Modules is parked
/// first the way the Burning Empires launcher parks it: Name-kept-version in the game folder, or Name-kept-date when
/// that name is taken. Like the Burning Empires launcher it writes no marker (TAOM's coop-set-aside.json lists only
/// what the TAOM launcher set aside itself), so a Coop parked here stays in the game folder until Restore parked Coop
/// in the TAOM or Burning Empires launcher, or this restore for the pinned build, moves it back. A launcher that carries
/// the RoT build of Coop skips this restore and puts its own copy in and out instead (RotCoopInstall).
/// </summary>
public static class RotCoopRestore
{
    public const string KeptTag = "-kept-";

    /// <summary>What to do, from the Coop folders in Modules, the kept ones in the game folder and whether the game runs.</summary>
    public static RotRestorePlan Decide(IReadOnlyList<RotCoopFolder> inModules, IReadOnlyList<RotCoopFolder> kept, bool gameRunning)
    {
        if (inModules.Any(IsPinned))
            return new RotRestorePlan(RotRestoreAction.Nothing, null, []);
        var match = kept.Where(IsPinned).OrderBy(folder => Path.GetFileName(folder.Directory), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (match == null)
            return new RotRestorePlan(RotRestoreAction.Nothing, null, []);
        if (gameRunning)
            return new RotRestorePlan(RotRestoreAction.GameRunning, match, []);
        return inModules.Count == 0
            ? new RotRestorePlan(RotRestoreAction.Restore, match, [])
            : new RotRestorePlan(RotRestoreAction.ParkThenRestore, match, inModules);
    }

    private static bool IsPinned(RotCoopFolder folder) =>
        folder.ModuleId.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && CoopBuildId.Same(folder.Build, RotStack.CoopBuild.Build);

    /// <summary>The name a kept folder had in Modules: the part before -kept- (Coop-kept-0.1.8 is Coop).</summary>
    public static string OriginalName(string keptDirectory)
    {
        var name = Path.GetFileName(keptDirectory.TrimEnd('\\', '/'));
        var at = name.IndexOf(KeptTag, StringComparison.OrdinalIgnoreCase);
        return at > 0 ? name[..at] : name;
    }

    /// <summary>Where a Modules folder is parked: Name-kept-version in the game folder, or Name-kept-date when taken.
    /// RotCoopInstall passes its own tag, so the other launchers' Restore parked Coop leaves its folders alone.</summary>
    public static string ParkPath(string gameRoot, string moduleDirectory, string version, string tag = KeptTag)
    {
        var name = Path.GetFileName(moduleDirectory.TrimEnd('\\', '/'));
        var plain = version.Trim().TrimStart('v', 'V');
        var kept = Path.Combine(gameRoot, name + tag + (plain.Length == 0 ? "old" : plain));
        return Directory.Exists(kept) || File.Exists(kept) ? Path.Combine(gameRoot, name + tag + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : kept;
    }

    /// <summary>The Coop folders (a Coop module id in SubModule.xml) in the game's Modules folder.</summary>
    public static IReadOnlyList<RotCoopFolder> InModules(string gameRoot) => Read(Path.Combine(gameRoot, "Modules"), _ => true);

    /// <summary>The kept Coop folders (Name-kept-something) in the game folder.</summary>
    public static IReadOnlyList<RotCoopFolder> Kept(string gameRoot) =>
        Read(gameRoot, name => name.Contains(KeptTag, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<RotCoopFolder> Read(string folder, Func<string, bool> nameFits)
    {
        var found = new List<RotCoopFolder>();
        IEnumerable<string> children;
        try
        {
            children = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        foreach (var child in children.Where(child => nameFits(Path.GetFileName(child))))
        {
            var xml = Path.Combine(child, "SubModule.xml");
            try
            {
                if (!File.Exists(xml))
                    continue;
                var id = ModuleIdentityReader.Read(xml).Id;
                if (id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) || RotStack.OtherCoopModuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    found.Add(new RotCoopFolder(child, id, CoopBuildId.FromModule(child)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
            }
        }

        return found;
    }

    /// <summary>
    /// Does the plan: parks each Coop in Modules (ParkThenRestore), then moves the kept folder back into Modules under
    /// the name it had. Returns the one line for the player, or null when there was nothing to do. A Modules place that
    /// is taken stops it with a line, so nothing is overwritten.
    /// </summary>
    public static string? Apply(string gameRoot, RotRestorePlan plan)
    {
        if (plan.Action == RotRestoreAction.Nothing || plan.Kept is not { } kept)
            return null;
        var build = CoopBuildId.Short(RotStack.CoopBuild.Build);
        var keptName = Path.GetFileName(kept.Directory);
        if (plan.Action == RotRestoreAction.GameRunning)
            return "Bannerlord is running, so the launcher did not put the Coop build " + build + " back from " + keptName + ". Close the game, then click Connect again.";

        // Checked before anything moves: a place taken by something that is not parked here stays as it is.
        var target = Path.Combine(gameRoot, "Modules", OriginalName(kept.Directory));
        var parksTarget = plan.Park.Any(other => Path.GetFullPath(other.Directory).TrimEnd('\\', '/').Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase));
        if ((Directory.Exists(target) && !parksTarget) || File.Exists(target))
            return "The Coop build " + build + " is in " + keptName + ", but Modules\\" + Path.GetFileName(target) + " is taken, so the launcher left both as they are. Move one of them yourself, then click Connect again.";

        var parked = new List<string>();
        foreach (var other in plan.Park)
        {
            var to = ParkPath(gameRoot, other.Directory, VersionOf(other.Directory));
            Directory.Move(other.Directory, to);
            parked.Add(Path.GetFileName(to));
        }

        Directory.Move(kept.Directory, target);
        return parked.Count == 0
            ? "Put the Coop build " + build + " back into the game's Modules folder from " + keptName + ", where another launcher had moved it."
            : "Moved the other Coop out of the game's Modules folder to " + string.Join(", ", parked) + " and put the Coop build " + build + " back from " + keptName + ".";
    }

    internal static string VersionOf(string module)
    {
        try
        {
            var xml = Path.Combine(module, "SubModule.xml");
            return File.Exists(xml) ? ModuleIdentityReader.Read(xml).Version : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "";
        }
    }
}
