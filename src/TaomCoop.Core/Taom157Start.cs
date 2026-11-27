using System.Text.Json;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;

namespace TaomCoop.Core;

/// <summary>Which way Start server goes for a server folder.</summary>
public enum Taom157Path
{
    Legacy148,
    Line157,
    Stop
}

/// <summary><see cref="Taom157Start.Pick"/>'s answer. Problem is the plain line when Path is Stop.</summary>
public sealed record Taom157Pick(Taom157Path Path, string? Problem = null);

/// <summary>
/// Start server on the official Coop dedicated server for Bannerlord 1.5.7 (Coop v0.1.8, tools guide section 7): which
/// server folder goes that way, the "modules" list TAOM gives it, its folders and the lines it shows. The server keeps
/// its data in the host's own data folder (--data-dir), so the Saves page and server-config.json stay where they are,
/// and reads mod-config.json from COOP_DATA_DIR, the Documents CoopData folder the settings pages edit (Coodsz, PR #22).
/// </summary>
public static class Taom157Start
{
    /// <summary>The file in the root of the official Coop server package. The 1.4.8 kit server has none: its root holds the
    /// kit wrapper files (ServerProgramRepair.Files), and release-info.txt may come with the Workshop server it is made from.</summary>
    public const string PackageMarker = "coop-pin.json";

    public const string NativeUnreadable =
        "The server's engine\\Modules\\Native\\SubModule.xml can't be read now, so the tool can't tell which Bannerlord the server is for. Close programs that may have it open, like an antivirus scan, then press Start server again.";

    public const string NativeMissing =
        "This server folder has the coop-pin.json of the Coop server for Bannerlord 1.5.7, but no engine\\Modules\\Native folder. Unpack the whole Coop server download into it again, then press Start server.";

    public const string NativeTooOld =
        "This server folder has the coop-pin.json of the Coop server for Bannerlord 1.5.7, but its engine\\Modules\\Native is for an older Bannerlord. Unpack the whole Coop server download into an empty folder again, then press Start server.";

    public const string TestModsRefused = "Test mods doesn't run on a Bannerlord 1.5.7 server yet.";

    public const string ExtraModsRefused =
        "Extra mods don't run on the Bannerlord 1.5.7 server yet. Pick Plain TAOM on the Mods page, then press Start server again.";

    public const string RescueRefused =
        "Run Rescue save doesn't work on a Bannerlord 1.5.7 server yet. That server runs Coop Save Rescue by itself on every start, so press Start server instead.";

    public const string TermsBody =
        "The TAOM server runs on the Bannerlord Coop dedicated server, which has a license agreement (EULA). The server does not host until the person hosting accepts it.\n\n"
        + "Read the terms below. Click I accept only if you agree. This tool then tells the server you accepted, and the server saves that in its eula.txt. You are asked again only when the terms change.\n\n"
        + "The terms allow private, non-commercial community hosting. Paid hosting needs written permission from the Coop team.";

    public const string HelpersNotListed =
        "The server stopped while it loaded TAOM (exit code 5). That happens when TAOM's helper DLLs are not in the server engine's DLL list. Press Start server again. It puts them in that list before the server starts.";

    /// <summary>Exit code -1 is not one of the server's own codes. All the tool knows is that it did not ask for the stop.</summary>
    public const string EndedFromOutside =
        "The server ended with exit code -1. This tool did not ask it to stop. Another program or Windows may have closed it, and it may not have saved on the way out.";

    public const string StoppedByTool = "This tool stopped the server.";

    /// <summary>The note under Start server. It starts with "Server is starting", so ServerReadyNote swaps it once the server serves.</summary>
    public const string Starting =
        "Server is starting. It is ready when the log says SERVING. That usually takes 1 to 10 minutes, and can take about 20 on a slow PC.";

    /// <summary>
    /// TAOM.Dependencies' alias stubs for the BUTR modules. A TAOM client has them ticked (DefaultModule), and Coop's join check
    /// refuses a client module the server does not list, so the server lists each one it has. Their one SubModule is tagged
    /// IsNoRenderModeElement, which the engine never loads, so they bring no code.
    /// </summary>
    public static readonly IReadOnlyList<string> HelperFolders = ["Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen"];

    public const string DualWieldModuleId = "DualWield";

    /// <summary>
    /// Line157 for the official Coop server for Bannerlord 1.5.x: engine\Modules\Native says 1.5 or later and the package's
    /// coop-pin.json is in the root. Without coop-pin.json the folder stays on the 1.4.8 path, as before: a kit server (also
    /// one staged from a newer game, Native 1.5), a folder without Native or no folder at all. A Native that can't be read,
    /// or coop-pin.json with no Native or one older than 1.5, stops: such a folder never gets the 1.4.8 repair.
    /// </summary>
    public static Taom157Pick Pick(string? serverRoot)
    {
        if (string.IsNullOrWhiteSpace(serverRoot))
            return new Taom157Pick(Taom157Path.Legacy148);
        var engine = Path.Combine(serverRoot, "engine");
        var package = File.Exists(Path.Combine(serverRoot, PackageMarker));
        if (!File.Exists(Path.Combine(engine, "Modules", "Native", "SubModule.xml")))
            return package ? new Taom157Pick(Taom157Path.Stop, NativeMissing) : new Taom157Pick(Taom157Path.Legacy148);
        if (!TaomGameLine.TryDetect(engine, out var line))
            return new Taom157Pick(Taom157Path.Stop, NativeUnreadable);
        if (!package)
            return new Taom157Pick(Taom157Path.Legacy148);
        return line == TaomLine.Line157 ? new Taom157Pick(Taom157Path.Line157) : new Taom157Pick(Taom157Path.Stop, NativeTooOld);
    }

    /// <summary>True when the window shows the Bannerlord 1.5.7 host: the server folder is the Coop server for 1.5.7, or this PC's
    /// game is 1.5.x (then a server folder not set up yet, or still the 1.4.8 kit default, never gets the 1.4.8 notices). A 1.4.8
    /// game with any other folder is false, as before.</summary>
    public static bool HostOnLine157(string? serverRoot, string? gameRoot) =>
        Pick(serverRoot).Path == Taom157Path.Line157 || TaomGameLine.Detect(gameRoot) == TaomLine.Line157;

    /// <summary>
    /// The "modules" list of server-config.json, from Haterade's server test on Coop v0.1.8: TAOM.Dependencies, its helper
    /// folders, LOTRLOME_Armory, TAOM_Map, TAOM, then Dual Wield and TAOM.CoopCompat (its README: after TAOM on a dedicated
    /// server). The helper folders, Dual Wield and TAOM.CoopCompat are listed only when they are in the server, since a
    /// listed module without a folder stops it (exit code 8). Coop's join check compares every module both ways, so a
    /// client with one ticked that the server does not list is refused. Native, SandBoxCore, Sandbox, Coop and
    /// DedicatedServer.Windows are not in it: the server loads them itself (NightlyServer.LoadsFirst and LoadsLast).
    /// </summary>
    public static IReadOnlyList<string> Modules(string serverRoot, bool includeDualWield = false)
    {
        List<string> modules = ["TAOM.Dependencies", .. HelperFolders.Where(id => InServer(serverRoot, id)), "LOTRLOME_Armory", "TAOM_Map", "TAOM"];
        if (includeDualWield && HasDualWield(serverRoot))
            modules.Add(DualWieldModuleId);
        if (InServer(serverRoot, TaomStack.CompatModuleId))
            modules.Add(TaomStack.CompatModuleId);
        return modules;
    }

    public static bool RequiresDualWield(TaomSetup setup, IReadOnlyList<string>? savedModules) =>
        setup.EnabledExtras.Any(mod => mod.ModuleId.Equals(DualWieldModuleId, StringComparison.OrdinalIgnoreCase))
        || savedModules?.Contains(DualWieldModuleId, StringComparer.OrdinalIgnoreCase) == true;

    public const string DualWieldNeedsPrepare = "This campaign needs Dual Wield. Install it in Bannerlord, then click Prepare server.";

    public static bool HasDualWield(string serverRoot) => InServer(serverRoot, DualWieldModuleId);

    private static string SubModulePath(string serverRoot, string id) => Path.Combine(serverRoot, "engine", "Modules", id, "SubModule.xml");

    private static bool InServer(string serverRoot, string id) => File.Exists(SubModulePath(serverRoot, id));

    /// <summary>The start's warning when TAOM is in the server but some of its helper folders are not, or null. The start goes
    /// on: only a player who joins with them ticked is refused.</summary>
    public static string? HelperFoldersLine(string serverRoot)
    {
        var missing = HelperFolders.Where(id => !InServer(serverRoot, id)).ToList();
        if (missing.Count == 0 || !InServer(serverRoot, TaomModules157.TaomModuleId))
            return null;
        return "TAOM's helper folders are not all in the server's engine\\Modules folder: " + string.Join(", ", missing) + ". Players who have them ticked, which is the normal TAOM setup, will be refused. Copy them from the game's Modules folder into the server, then press Start server again.";
    }

    /// <summary>
    /// The "does not run its code" note for the listed modules whose every DLL SubModule is for the game client only, read per
    /// SubModule (Taom157ServerSetup.ServerLoadedDlls). TAOM and TAOM.Dependencies have a server DLL next to their client ones,
    /// so they get none, and so do modules without a DLL SubModule tagged DedicatedServerType none. The helper folders are
    /// among those: their one SubModule names TAOM.Dependencies.dll with no DedicatedServerType tag, so this rule counts it as
    /// loaded on the server (the engine skips it for its IsNoRenderModeElement tag, but this rule does not read that tag).
    /// A file that can't be read gets no note.
    /// </summary>
    public static IReadOnlyList<string> ModuleNotes(string serverRoot, bool includeDualWield = false)
    {
        var notes = new List<string>();
        foreach (var id in Modules(serverRoot, includeDualWield))
        {
            var path = SubModulePath(serverRoot, id);
            try
            {
                if (Taom157ServerSetup.ServerLoadedDlls(path) is { Count: 0 } && ServerModuleNotes.SkippedCode(id, File.ReadAllText(path)) is { } note)
                    notes.Add(note);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return notes;
    }

    /// <summary>COOP_DATA_DIR: the folder of the mod-config.json the settings pages edit.</summary>
    public static string CoopDataRoot() => Path.GetDirectoryName(CoopModConfig.UserPath())!;

    /// <summary>
    /// The start: the server's data in the host's data folder, mod-config.json from <see cref="CoopDataRoot"/>. The server
    /// passes --region on to the engine unchecked, after its port, so the two engine switches ride along in it with the
    /// configured region, with EU as the default. Without them every missing TAOM animation makes the engine write a report folder
    /// (124 folders and 8.7 GB in 200 seconds in a live test); with them it writes none.
    /// </summary>
    public static NightlyServerStart Plan(string serverRoot, string dataRoot, bool acceptTerms, IReadOnlyDictionary<string, string>? extra = null)
    {
        var plan = NightlyServer.StartPlan(serverRoot, CoopDataRoot(), acceptTerms, extra, dataDir: dataRoot);
        var path = Path.Combine(dataRoot, NightlyServer.ConfigFileName);
        var region = File.Exists(path) ? ReadRegion(File.ReadAllText(path)) : "EU";
        return plan with { Arguments = [.. plan.Arguments, "--region", region + " 0 /DisableErrorReporting /suppress_message_boxes"] };
    }

    internal static string ReadRegion(string config)
    {
        using var document = JsonDocument.Parse(config, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("region", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                return property.Value.GetString()!.Trim();
        }
        return "EU";
    }

    /// <summary>The line for an exit code. Exit code 5 is TAOM.dll failing to load its helper DLLs, -1 an end this tool did not
    /// ask for; every other code is the server's own (NightlyServer.ExitHint).</summary>
    public static string? ExitHint(int exitCode) => exitCode switch
    {
        5 => HelpersNotListed,
        -1 => EndedFromOutside,
        _ => NightlyServer.ExitHint(exitCode)
    };

    /// <summary>The line the run's log file gets after the exit code: who ended it, as far as the tool knows. Null for an exit
    /// this tool did not ask for with a code other than -1 (the server's own code says enough).</summary>
    public static string? ExitLine(bool stopAsked, int? exitCode) =>
        stopAsked ? StoppedByTool : exitCode == -1 ? EndedFromOutside : null;

    /// <summary>The log line for what <see cref="Taom157ServerSetup.Apply"/> changed, or null when it changed nothing.</summary>
    public static string? SetupLine(Taom157SetupResult result) => result.Changed
        ? "Set TAOM up on the server: moved " + result.Moved.Count + " helper DLLs into its engine folder, added or updated " + result.DepsWritten.Count + " in its DLL list and took out " + result.DepsRemoved.Count + "."
        : null;

    /// <summary>What the Server Tool's Made for card checks on a Coop server for Bannerlord 1.5.7 instead of the 1.4.8 kit's pins:
    /// any Bannerlord 1.5, any TAOM (TAOM updates often, tools guide decision 4) and the Coop the package's coop-pin.json names,
    /// any Coop when that file can't be read.</summary>
    public static Taom157MadeFor MadeFor(string serverRoot) => new("v1.5", null, PinnedCoop(serverRoot));

    // The card reads it on the window thread at every refresh, so a file far larger than the package's one line is not read.
    private const long PinFileLimit = 64 * 1024;

    private static string? PinnedCoop(string serverRoot)
    {
        try
        {
            var path = Path.Combine(serverRoot, PackageMarker);
            if (new FileInfo(path).Length > PinFileLimit)
                return null;
            using var pin = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return pin.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && pin.RootElement.TryGetProperty("coopVersion", out var version)
                && version.ValueKind == System.Text.Json.JsonValueKind.String && version.GetString() is { Length: > 0 } text ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The start up log line on a Coop server for Bannerlord 1.5.7 with the versions found in its folder, or null when
    /// one of them was not found, so the line never shows an empty version.</summary>
    public static string? ServerFolderLine(string? bannerlord, string? taom, string? coop) =>
        string.IsNullOrWhiteSpace(bannerlord) || string.IsNullOrWhiteSpace(taom) || string.IsNullOrWhiteSpace(coop)
            ? null
            : "Server folder: Bannerlord " + bannerlord + ", TAOM " + taom + ", Coop " + coop + ".";
}

/// <summary>The pins of <see cref="Taom157Start.MadeFor"/>. Null means any version.</summary>
public sealed record Taom157MadeFor(string Game, string? Taom, string? Coop);
