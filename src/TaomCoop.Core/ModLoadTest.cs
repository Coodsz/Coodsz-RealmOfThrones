using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;

namespace TaomCoop.Core;

/// <summary>One mod the server load test tries: TAOM core plus this mod (and the mods it needs).</summary>
public sealed record ModTestCandidate(string ModuleId, string Name, string WorkshopId, string Version, IReadOnlyList<TaomExtraMod> Needs)
{
    public string Label => Name.Length > 0 ? Name : ModuleId;
}

/// <summary>One finished test: pass when the server reached serving.</summary>
public sealed class ModTestResult
{
    public const string Pass = "pass";
    public const string Fail = "fail";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Result { get; set; } = "";
    public string Reason { get; set; } = "";
    // YYYY-MM-DD
    public string Date { get; set; } = "";
    public string Taom { get; set; } = "";
    /// <summary>What the mod was tested on in a tool other than the TAOM server tool (for Coodsz
    /// Host the Coop version under the working list core).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Base { get; set; }
    public int Seconds { get; set; }

    [JsonIgnore]
    public bool Passed => Result.Equals(Pass, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A running test server. The log is everything the server wrote so far.</summary>
public interface IModTestRun : IDisposable
{
    string Log { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    /// <summary>Asks the server to stop, then ends it when it does not.</summary>
    Task StopAsync();
}

/// <summary>Starts the test server for one setup (TAOM core plus the mod under test) on the throwaway
/// test save. The TAOM server tool does it with the Coodsz TAOM server path; tests use a fake.</summary>
public interface IModTestServer
{
    Task<IModTestRun> StartAsync(TaomSetup setup, CancellationToken cancellationToken);
}

/// <summary>The clock and the wait the test uses, so tests can run it without waiting.</summary>
public interface IModTestClock
{
    DateTime UtcNow { get; }
    Task Delay(TimeSpan time, CancellationToken cancellationToken);
}

public sealed class SystemModTestClock : IModTestClock
{
    public static readonly SystemModTestClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;

    public Task Delay(TimeSpan time, CancellationToken cancellationToken) => Task.Delay(time, cancellationToken);
}

/// <summary>How long a test may take. Total is not counted while the server builds the map cache
/// (a new campaign's first start), but a server that writes nothing for Quiet fails.</summary>
public sealed record ModTestOptions(TimeSpan Total, TimeSpan Quiet, TimeSpan Poll)
{
    public static readonly ModTestOptions Default = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(2));
}

/// <summary>
/// What differs between the tools that run the load test: the first status line of a test, the
/// reason for a server that closed before serving (null when the log does not say), and what the
/// result is stamped with. <see cref="Taom"/> is the TAOM server tool's; Coodsz Host has its own.
/// </summary>
public sealed record ModTestFlavor(
    Func<ModTestCandidate, string> StartLine,
    Func<string, int?, string?> ExplainExit,
    Action<ModTestResult> Stamp)
{
    public static readonly ModTestFlavor Taom = new(
        mod => "Starting the server with the TAOM core and " + mod.Label + ".",
        ModLoadTest.TaomExitReason,
        result => result.Taom = TaomStack.TaomVersionText);
}

/// <summary>Where a running test is, for the window.</summary>
public sealed record ModTestProgress(int Index, int Count, ModTestCandidate Mod, string Status);

/// <summary>
/// The automatic server load test of the TAOM server tool. For each picked mod, one at a time, it
/// starts the Coodsz TAOM server with the TAOM core plus that one mod (and the extra mods it needs)
/// on a throwaway copy of the host's save (save name + "-modtest", so real saves are never written),
/// waits for the server's serving state (pass), or a fatal state, an exit or a timeout (fail, with
/// the reason from the log), stops it and goes on. Results go to Data\modtest-results.json; passed
/// mods can be written into a copy of the TAOM mod list with source server-loads for Coodsz to publish.
/// </summary>
public static class ModLoadTest
{
    public const string TestSaveSuffix = "-modtest";
    public const string ResultsFileName = "modtest-results.json";
    public const string ExportFileName = "modtest-taom-mod-compat.json";
    public const string SetupName = "Server load test";

    /// <summary>The throwaway save name for a save: name-modtest.</summary>
    public static string TestSaveName(string saveName)
    {
        var name = (saveName ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Pick a save first. The test runs on a copy of it.");
        return name.EndsWith(TestSaveSuffix, StringComparison.OrdinalIgnoreCase) ? name : name + TestSaveSuffix;
    }

    /// <summary>
    /// Copies the save (and its .json) to the test name in the same folder, replacing an older test
    /// copy. The real save is only read. Returns the test save name.
    /// </summary>
    public static string CopyTestSave(string savesDirectory, string saveName)
    {
        var testName = TestSaveName(saveName);
        if (testName.Equals(saveName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pick the real save, not the test copy.");
        var source = Path.Combine(savesDirectory, saveName.Trim() + ".sav");
        if (!File.Exists(source))
            throw new InvalidOperationException("The save " + saveName + " is not in the server Game Saves folder.");
        File.Copy(source, Path.Combine(savesDirectory, testName + ".sav"), overwrite: true);
        var json = Path.Combine(savesDirectory, saveName.Trim() + ".json");
        var testJson = Path.Combine(savesDirectory, testName + ".json");
        if (File.Exists(json))
            File.Copy(json, testJson, overwrite: true);
        else if (File.Exists(testJson))
            File.Delete(testJson);
        return testName;
    }

    /// <summary>Deletes the test copy and what the server made of it (its .json, backups and the
    /// unfinished save marker). Only files named after the test save are touched. Returns how many.</summary>
    public static int RemoveTestSave(string savesDirectory, string testName)
    {
        if (!testName.EndsWith(TestSaveSuffix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(savesDirectory))
            return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(savesDirectory, testName + ".*"))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith(testName + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Delete(file);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// The mods to test by default: mods on this PC that the TAOM mod list knows, that are not tested
    /// in Coop by Coodsz yet and can be a TAOM extra. A Workshop mod is matched by module id, Workshop
    /// id or name; a mod in the game's Modules folder (Nexus or ModDB) only by its module id. Each one
    /// carries the extra mods on this PC it needs (from its SubModule.xml), so the test is about that
    /// mod, and a patch such as TAOM_Standalone_Compat_RBM brings the mod it patches.
    /// </summary>
    public static IReadOnlyList<ModTestCandidate> DefaultCandidates(TaomCompatList list, IReadOnlyList<FoundMod> onThisPc, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        var picked = new List<ModTestCandidate>();
        foreach (var mod in onThisPc)
        {
            if (TaomLoadOrder.RefusalReason(mod.ModuleId) != null)
                continue;
            CompatEntry? entry;
            if (SharedModList.IsWorkshopId(mod.WorkshopId))
                entry = list.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
            else if (!mod.Source.Equals("Workshop", StringComparison.OrdinalIgnoreCase))
                entry = list.Find(mod.ModuleId);
            else
                continue;
            if (entry == null || entry.Source.Equals(CompatSource.CoodszCoop, StringComparison.OrdinalIgnoreCase))
                continue;
            if (picked.Any(item => item.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                continue;
            picked.Add(Candidate(mod, onThisPc, manifests));
        }

        return picked.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True for a mod the list says is broken; the window leaves those unticked.</summary>
    public static bool KnownBroken(TaomCompatList list, ModTestCandidate mod) =>
        list.Find(mod.ModuleId, mod.WorkshopId, mod.Name) is { Kind: CompatTag.Broken };

    public static ModTestCandidate Candidate(FoundMod mod, IReadOnlyList<FoundMod> onThisPc, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        manifests ??= new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase);
        var manifest = manifests.TryGetValue(mod.ModuleId, out var known) ? known : ModuleManifestReader.Read(mod.Directory);
        var needs = new List<TaomExtraMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod.ModuleId };
        void Walk(ModuleManifest? of)
        {
            if (of == null)
                return;
            foreach (var id in of.RequiredIds)
            {
                if (!seen.Add(id) || TaomLoadOrder.RefusalReason(id) != null)
                    continue;
                var found = onThisPc.FirstOrDefault(item => item.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                    continue;
                Walk(manifests.TryGetValue(found.ModuleId, out var next) ? next : ModuleManifestReader.Read(found.Directory));
                needs.Add(new TaomExtraMod { Name = found.Name, ModuleId = found.ModuleId, WorkshopId = found.WorkshopId, Enabled = true, Slot = ExtraSlot.AfterTaom });
            }
        }

        Walk(manifest);
        return new ModTestCandidate(mod.ModuleId, mod.Name, mod.WorkshopId, manifest?.Version ?? "", needs);
    }

    /// <summary>The setup a test starts: the TAOM core, the mods this one needs, then this mod, in a
    /// good order (TaomAutoSort).</summary>
    public static TaomSetup SetupFor(ModTestCandidate mod, IReadOnlyDictionary<string, ModuleManifest>? manifests = null)
    {
        var setup = new TaomSetup { Name = SetupName };
        foreach (var need in mod.Needs)
            setup.Extras.Add(need.Copy());
        setup.Extras.Add(new TaomExtraMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId, Enabled = true, Slot = ExtraSlot.AfterTaom });
        TaomLoadOrder.Normalize(setup);
        if (manifests != null)
            TaomAutoSort.Sort(setup, manifests);
        return setup;
    }

    /// <summary>What the log says so far: null while the server is still starting, else pass or fail
    /// with the reason. Serving and fatal come from the server's @DS@ state events (or its SERVING
    /// line); the reason for an exit before serving comes from explainExit (the TAOM report when
    /// none is given).</summary>
    public static (bool Passed, string Reason)? Judge(string log, bool exited, int? exitCode, Func<string, int?, string?>? explainExit = null)
    {
        var state = ServerStateEvent.Last(log);
        if (state is { Serving: true } || ServerStateEvent.HasServingLine(log))
            return (true, "The server reached serving.");
        if (state is { Fatal: true })
            return (false, ServerStateEvent.FatalStatus(state));
        if (!exited)
            return null;
        var reason = (explainExit ?? TaomExitReason)(log, exitCode);
        if (!string.IsNullOrWhiteSpace(reason))
            return (false, reason.Trim());

        return (false, exitCode is { } code
            ? "The server closed with exit code " + code + " before it was serving."
            : "The server closed before it was serving.");
    }

    /// <summary>The TAOM server tool's reason for a server that closed before serving, or null.</summary>
    public static string? TaomExitReason(string log, int? exitCode)
    {
        var report = TaomHostReport.Explain(log, wasServing: false, exited: true);
        if (report is not { } known)
            return null;
        var happened = known.Lines.FirstOrDefault(line => line.StartsWith("What happened", StringComparison.Ordinal));
        var detail = happened == null ? "" : " " + happened["What happened".Length..].TrimStart(' ', ':').Trim();
        return (known.Status + detail).Trim();
    }

    /// <summary>Runs one test: start, wait for serving or a failure, stop. Never throws for a server
    /// problem; that is a failed result.</summary>
    public static async Task<ModTestResult> RunOneAsync(
        ModTestCandidate mod,
        TaomSetup setup,
        IModTestServer server,
        ModTestOptions options,
        IModTestClock clock,
        Action<string>? status,
        CancellationToken cancellationToken,
        ModTestFlavor? flavor = null)
    {
        flavor ??= ModTestFlavor.Taom;
        var started = clock.UtcNow;
        var result = new ModTestResult
        {
            Id = mod.ModuleId,
            Name = mod.Name,
            WorkshopId = mod.WorkshopId,
            Version = mod.Version,
            Date = started.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        flavor.Stamp(result);

        IModTestRun? run = null;
        try
        {
            status?.Invoke(flavor.StartLine(mod));
            run = await server.StartAsync(setup, cancellationToken);
            var lastLength = -1;
            var lastOutput = clock.UtcNow;
            var buildingFor = TimeSpan.Zero;
            var lastTick = clock.UtcNow;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var log = run.Log ?? "";
                var now = clock.UtcNow;
                if (log.Length != lastLength)
                {
                    lastLength = log.Length;
                    lastOutput = now;
                }

                var verdict = Judge(log, run.HasExited, run.ExitCode, flavor.ExplainExit);
                if (verdict is { } done)
                {
                    result.Result = done.Passed ? ModTestResult.Pass : ModTestResult.Fail;
                    result.Reason = done.Reason;
                    break;
                }

                // The map cache build of a new campaign can take up to an hour; it does not count.
                var step = StartupProgress.Measure(log);
                if (step is { Stage: "settlements" or "neighbors" })
                    buildingFor += now - lastTick;
                lastTick = now;
                if (now - lastOutput >= options.Quiet)
                {
                    result.Result = ModTestResult.Fail;
                    result.Reason = "The server wrote nothing for " + Minutes(options.Quiet) + " minutes before it was serving.";
                    break;
                }

                if (now - started - buildingFor >= options.Total)
                {
                    result.Result = ModTestResult.Fail;
                    result.Reason = "The server was not serving after " + Minutes(options.Total) + " minutes.";
                    break;
                }

                var progress = StartupProgress.Describe(log);
                status?.Invoke(progress ?? "Waiting for the server to say serving.");
                await clock.Delay(options.Poll, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Result = ModTestResult.Fail;
            result.Reason = "The test server did not start. " + ex.Message;
        }
        finally
        {
            if (run != null)
            {
                status?.Invoke("Stopping the test server.");
                try
                {
                    await run.StopAsync();
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                }

                run.Dispose();
            }
        }

        result.Seconds = (int)Math.Max(0, (clock.UtcNow - started).TotalSeconds);
        return result;
    }

    /// <summary>Tests the mods one at a time. Each result is handed to recorded as soon as it is done
    /// (the window saves it to the results file). Stop (the token) ends the running test and stops.</summary>
    public static async Task<IReadOnlyList<ModTestResult>> RunAllAsync(
        IReadOnlyList<ModTestCandidate> mods,
        Func<ModTestCandidate, TaomSetup> setupFor,
        IModTestServer server,
        ModTestOptions options,
        IModTestClock clock,
        IProgress<ModTestProgress>? progress,
        Action<ModTestResult>? recorded,
        CancellationToken cancellationToken,
        ModTestFlavor? flavor = null)
    {
        var results = new List<ModTestResult>();
        for (var i = 0; i < mods.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            var mod = mods[i];
            var index = i;
            ModTestResult result;
            try
            {
                result = await RunOneAsync(mod, setupFor(mod), server, options, clock,
                    line => progress?.Report(new ModTestProgress(index + 1, mods.Count, mod, line)), cancellationToken, flavor);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            results.Add(result);
            recorded?.Invoke(result);
        }

        return results;
    }

    private static string Minutes(TimeSpan time) =>
        ((int)Math.Round(time.TotalMinutes)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ----- The results file and the export -----

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions EntryOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class ResultsFile
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = TaomResultsKind;
        public int SchemaVersion { get; set; } = 1;
        public List<ModTestResult> Results { get; set; } = new();
    }

    /// <summary>The saved results, newest last. An unreadable file reads as none.</summary>
    public static List<ModTestResult> LoadResults(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new List<ModTestResult>();
            var file = JsonSerializer.Deserialize<ResultsFile>(File.ReadAllText(path), ReadOptions);
            return file?.Results?.Where(result => result != null && result.Id.Length > 0).ToList() ?? new List<ModTestResult>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<ModTestResult>();
        }
    }

    public const string TaomResultsKind = "CoodszTaomModTest";

    /// <summary>Adds a result to the file. A mod keeps one result per version, the newest. Kind
    /// names the tool that keeps the file.</summary>
    public static List<ModTestResult> Record(string path, ModTestResult result, string kind = TaomResultsKind)
    {
        var results = LoadResults(path);
        results.RemoveAll(old => old.Id.Equals(result.Id, StringComparison.OrdinalIgnoreCase) && old.Version == result.Version);
        results.Add(result);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(new ResultsFile { Kind = kind, Results = results }, WriteOptions));
        return results;
    }

    /// <summary>
    /// The TAOM mod list with the passed mods written in as source server-loads: a mod the list
    /// already has gets the module id, Workshop id and test date, and a load test note; a reported
    /// or unknown tag becomes loads, a risky, broken or everyone tag stays (loading on the server does
    /// not make a campaign or battle mod safe). Mods Coodsz tested in Coop are left as they are. A mod
    /// the list does not have is added. Failed results change nothing. The newest pass of a mod counts.
    /// </summary>
    public static TaomCompatList MergePassed(TaomCompatList current, IEnumerable<ModTestResult> results, string today)
    {
        var merged = TaomCompatList.Parse(Serialize(current));
        merged.SchemaVersion = Math.Max(merged.SchemaVersion, TaomCompatList.CurrentSchema);
        merged.Updated = today;
        var newest = results.Where(result => result.Passed)
            .GroupBy(result => result.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last());
        foreach (var result in newest)
        {
            var note = "Passed the automatic server load test on " + result.Date + (result.Version.Length > 0 ? " with version " + result.Version : "") + ". Joining was not tested.";
            var entry = merged.Find(result.Id, result.WorkshopId, result.Name);
            if (entry == null)
            {
                merged.Mods.Add(new CompatEntry
                {
                    Id = result.Id,
                    Name = result.Name,
                    WorkshopId = SharedModList.IsWorkshopId(result.WorkshopId) ? result.WorkshopId : "",
                    Tag = "loads",
                    Source = CompatSource.ServerLoads,
                    Checked = result.Date,
                    Note = note
                });
                continue;
            }

            if (entry.Source.Equals(CompatSource.CoodszCoop, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Id.Length == 0)
                entry.Id = result.Id;
            if (entry.WorkshopId.Length == 0 && SharedModList.IsWorkshopId(result.WorkshopId))
                entry.WorkshopId = result.WorkshopId;
            if (entry.Kind is CompatTag.Reported or CompatTag.Unknown or CompatTag.Loads)
                entry.Tag = "loads";
            entry.Source = CompatSource.ServerLoads;
            entry.Checked = result.Date;
            var oldNote = System.Text.RegularExpressions.Regex.Replace(entry.Note, @"\s*Passed the automatic server load test on .*?Joining was not tested\.", "").Trim();
            entry.Note = oldNote.Length > 0 ? oldNote + " " + note : note;
        }

        return merged;
    }

    /// <summary>The list as the repository keeps it: one line per mod, CRLF line ends.</summary>
    public static string Serialize(TaomCompatList list)
    {
        var text = new StringBuilder();
        text.Append("{\r\n");
        text.Append("  \"kind\": ").Append(JsonSerializer.Serialize(list.FileKind)).Append(",\r\n");
        text.Append("  \"schemaVersion\": ").Append(list.SchemaVersion).Append(",\r\n");
        text.Append("  \"taom\": ").Append(JsonSerializer.Serialize(list.Taom, EntryOptions)).Append(",\r\n");
        text.Append("  \"game\": ").Append(JsonSerializer.Serialize(list.Game, EntryOptions)).Append(",\r\n");
        text.Append("  \"coop\": ").Append(JsonSerializer.Serialize(list.Coop, EntryOptions)).Append(",\r\n");
        text.Append("  \"updated\": ").Append(JsonSerializer.Serialize(list.Updated, EntryOptions)).Append(",\r\n");
        text.Append("  \"mods\": [\r\n");
        for (var i = 0; i < list.Mods.Count; i++)
        {
            var entry = list.Mods[i];
            text.Append("    { ")
                .Append("\"id\": ").Append(JsonSerializer.Serialize(entry.Id, EntryOptions))
                .Append(", \"name\": ").Append(JsonSerializer.Serialize(entry.Name, EntryOptions))
                .Append(", \"workshopId\": ").Append(JsonSerializer.Serialize(entry.WorkshopId, EntryOptions));
            if (entry.NexusId.Length > 0)
                text.Append(", \"nexusId\": ").Append(JsonSerializer.Serialize(entry.NexusId, EntryOptions));
            text.Append(", \"tag\": ").Append(JsonSerializer.Serialize(entry.Tag, EntryOptions));
            if (entry.Class.Length > 0)
                text.Append(", \"class\": ").Append(JsonSerializer.Serialize(entry.Class, EntryOptions));
            if (entry.ClientOnlyOnServer)
                text.Append(", \"clientOnlyOnServer\": true");
            if (entry.HasServerBuild)
                text.Append(", \"hasServerBuild\": true");
            if (entry.Source.Length > 0)
                text.Append(", \"source\": ").Append(JsonSerializer.Serialize(entry.Source, EntryOptions));
            if (entry.Checked.Length > 0)
                text.Append(", \"checked\": ").Append(JsonSerializer.Serialize(entry.Checked, EntryOptions));
            text.Append(", \"note\": ").Append(JsonSerializer.Serialize(entry.Note, EntryOptions))
                .Append(" }").Append(i < list.Mods.Count - 1 ? "," : "").Append("\r\n");
        }

        text.Append("  ]\r\n}\r\n");
        return text.ToString();
    }
}
