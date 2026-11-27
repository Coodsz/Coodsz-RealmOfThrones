using System.IO.Compression;

namespace BannerlordCoopLauncher.Core.Updates;

public readonly record struct UpdateStep(string Message, double? Fraction);

/// <summary>
/// What an update installs. PayloadExe is the program the zip must hold. SuccessorOnly means a two exe
/// zip, where only that one exe is copied and the old exe this tool started from is removed after.
/// </summary>
public sealed record InstallPlan(string ZipName, string PayloadExe, bool SuccessorOnly);

public static class ToolUpdate
{
    public static string? PickZip(string exeFileName, IEnumerable<string> assetNames) =>
        PlanInstall(exeFileName, assetNames)?.ZipName;

    public static InstallPlan? PlanInstall(string exeFileName, IEnumerable<string> assetNames)
    {
        var exeName = Path.GetFileName(exeFileName);
        var zips = assetNames.Where(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        var tool = ToolNames.Find(exeName);
        if (tool == null)
            return LegacyPickZip(exeName, zips) is { } legacy ? new InstallPlan(legacy, exeName, false) : null;

        // A two exe release carries both programs of a product in one zip. Prefer it for both roles.
        var newZip = zips.FirstOrDefault(tool.IsNewZip);
        if (newZip != null)
            return new InstallPlan(newZip, tool.NewExeName, true);
        foreach (var pattern in tool.OldZipPatterns)
        {
            var oldZip = zips.FirstOrDefault(name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (oldZip != null)
                return new InstallPlan(oldZip, exeName, false);
        }

        return null;
    }

    // Today's rules, for an exe whose name is not in ToolNames (a player renamed it).
    private static string? LegacyPickZip(string exeFileName, List<string> zips)
    {
        // The Burning Empires tools have no rule here: their first release is the two exe zip, which only
        // the exact names in ToolNames pick.
        var exe = Path.GetFileNameWithoutExtension(exeFileName);
        if (IsAD1259Server(exe))
            return zips.FirstOrDefault(name => name.Contains("AD1259-Server", StringComparison.OrdinalIgnoreCase));
        if (IsAD1259Client(exe))
            return zips.FirstOrDefault(name => name.Contains("AD1259-Client-Launcher", StringComparison.OrdinalIgnoreCase));
        if (IsTaomServer(exe))
            return zips.FirstOrDefault(name => name.Contains("Server-Tool", StringComparison.OrdinalIgnoreCase));
        if (IsTaomClient(exe))
            return zips.FirstOrDefault(name => name.Contains("Client-Launcher", StringComparison.OrdinalIgnoreCase));
        if (IsHost(exe))
        {
            return zips.FirstOrDefault(name => name.Contains("Coodsz-Host", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("CoodszHost", StringComparison.OrdinalIgnoreCase))
                ?? zips.FirstOrDefault(name => name.Contains("Coop-and-Friends", StringComparison.OrdinalIgnoreCase));
        }

        if (IsFriends(exe))
            return zips.FirstOrDefault(name => name.Contains("Coop-and-Friends", StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>
    /// Json files the build writes next to a loose file program. They describe the program itself,
    /// so an update must always replace them. Every other json in the folder is a saved setting or a
    /// default the player may have changed, and the update leaves it alone.
    /// </summary>
    public static readonly IReadOnlyList<string> BuildJsonSuffixes = [".deps.json", ".runtimeconfig.json"];

    /// <summary>Files the player owns in the program folder. An update never replaces or removes them.</summary>
    public static readonly IReadOnlyList<string> KeptUserFiles =
    [
        "launcher.json",
        "profile-cache.json",
        "host.json",
        "cheats.json",
        "taom-launcher.json",
        "taom-server.json",
        "taom-cheats.json",
        "ad1259-launcher.json",
        "be-launcher.json",
        "be-server.json",
        "be-cheats.json",
        "be-bans.json",
        "rot-launcher.json",
        "rot-server.json",
        "update-install.log",
        UpdateApply.SkippedLogName,
        "launcher-error.txt"
    ];

    public static bool ShouldReplace(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (name.Length == 0 || IsKeptUserFile(name))
            return false;
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return true;
        return BuildJsonSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    public static void ExtractZip(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        ExtractEntries(zip, destination);
    }

    private static void ExtractEntries(ZipArchive zip, string destination)
    {
        var root = Path.GetFullPath(destination);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update zip contains a file outside its folder.");
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public static string FindPayloadRoot(string extractedDirectory, string exeFileName)
    {
        var matches = Directory.EnumerateFiles(extractedDirectory, exeFileName, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException("The update does not contain " + exeFileName + ".");
        return Path.GetDirectoryName(matches[0]) ?? extractedDirectory;
    }

    public const string UntrustedDownloadMessage = "The update link does not point to the Coodsz GitHub page, so nothing was installed.";

    public const string DamagedDownloadMessage = "The update download did not match the release, so nothing was installed. Try again later.";

    /// <summary>
    /// True only for the exact link GitHub gives a Coodsz release asset. No pattern matching, so dot
    /// segments, escapes and other repos all fail.
    /// </summary>
    public static bool IsTrustedDownloadUrl(string url, string repo, string tag, string assetName)
    {
        var parts = repo.Split('/');
        if (parts.Length != 2 || parts[0] != "Coodsz" || !IsPlainName(parts[1]) || !IsPlainName(tag) || !IsSafeAssetName(assetName))
            return false;
        return string.Equals(url, "https://github.com/" + repo + "/releases/download/" + tag + "/" + assetName, StringComparison.Ordinal);
    }

    public static bool IsSafeAssetName(string name) =>
        name.Length > 0
        && name != "."
        && name != ".."
        && Path.GetFileName(name) == name
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0;

    private static bool IsPlainName(string value) =>
        value.Length > 0
        && !value.Contains("..", StringComparison.Ordinal)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>
    /// Checks a downloaded update before any file is replaced and, when extractTo is given, extracts it
    /// from the same open file so nothing can swap the zip between the check and the extract. Throws when
    /// the release digest is missing or does not match, or the zip does not hold the program.
    /// </summary>
    public static void VerifyDownload(string zipPath, string? digest, string exeFileName, string? extractTo = null)
    {
        // GitHub always writes the prefix in lower case; keep this strict rather than accept look alikes.
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException(DamagedDownloadMessage);
        var expected = digest[prefix.Length..];
        if (expected.Length != 64 || !expected.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException(DamagedDownloadMessage);

        using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(DamagedDownloadMessage);

        stream.Position = 0;
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException(DamagedDownloadMessage);
        }

        using (zip)
        {
            if (!zip.Entries.Any(entry => entry.Name.Equals(exeFileName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The update does not contain " + exeFileName + ".");
            if (extractTo != null)
                ExtractEntries(zip, extractTo);
        }
    }

    public static async Task InstallAsync(
        ReleaseOffer offer,
        string installDirectory,
        string exePath,
        int processId,
        IProgress<UpdateStep>? progress,
        CancellationToken cancellationToken)
    {
        var assets = offer.Assets;
        var exeName = Path.GetFileName(exePath);
        var plan = PlanInstall(exeName, assets.Select(asset => asset.Name));
        if (plan == null)
            throw new InvalidOperationException("This release does not include an update for " + exeName + ".");
        var zipName = plan.ZipName;
        var asset = assets.FirstOrDefault(item => item.Name == zipName);
        if (asset == null)
            throw new InvalidOperationException("This release does not include an update for " + exeName + ".");
        if (!IsSafeAssetName(zipName) || !IsTrustedDownloadUrl(asset.DownloadUrl, offer.Repo, offer.Tag, zipName))
            throw new InvalidOperationException(UntrustedDownloadMessage);

        var stage = Path.Combine(Path.GetTempPath(), "coodsz-update-" + processId);
        try
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(stage);
            var zipPath = Path.Combine(stage, zipName);
            progress?.Report(new UpdateStep("Downloading the update. Saved settings stay in this folder.", null));
            await DownloadAsync(asset.DownloadUrl, zipPath, progress, cancellationToken).ConfigureAwait(false);
            var extracted = Path.Combine(stage, "files");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(extracted);
                VerifyDownload(zipPath, asset.Digest, plan.PayloadExe, extracted);
            }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateStep("The download finished. This window closes, then opens again.", 1));
            var payload = FindPayloadRoot(extracted, plan.PayloadExe);
            var planPath = Path.Combine(stage, UpdateApply.PlanFileName);
            var installPlan = plan.SuccessorOnly
                ? SuccessorPlan(SuccessorInstall.Prepare(payload, plan.PayloadExe, installDirectory, exePath,
                    keepOldExe: ToolRename.ShortcutsMayPointAt(new WindowsShortcutStore(), exePath)), processId, stage)
                : LegacyPlan(payload, installDirectory, exePath, processId, stage);
            await UpdateApply.WritePlanAsync(planPath, installPlan, cancellationToken).ConfigureAwait(false);
            // A copy of this exe installs the update once this one has closed (UpdateApply), no script needed.
            UpdateApply.StartHelper(Environment.ProcessPath ?? exePath, stage, planPath, IsSingleFileApp());
        }
        catch
        {
            try { Directory.Delete(stage, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static bool IsSingleFileApp() =>
        string.IsNullOrEmpty(System.Reflection.Assembly.GetEntryAssembly()?.Location);

    public static void RemovePreviousVersion(string installDirectory, string currentExePath, bool singleFile)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            return;

        // An update helper holds the folder while it swaps files, and its tagged .new looks like a leftover.
        // It still holds it for a moment after starting this tool again, so wait a little, else the next
        // start cleans up.
        UpdateApply.FolderLock? gate;
        try
        {
            gate = UpdateApply.FolderLock.Take(installDirectory, RemoveWait);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (gate == null)
            return;
        using (gate)
        {
            foreach (var path in FirstStartFiles(installDirectory, Path.GetFileName(currentExePath), singleFile))
                TryDelete(path);
        }
    }

    // How long a start waits for an update helper still holding the folder (tests shorten it).
    internal static TimeSpan RemoveWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// What RemovePreviousVersion deletes on this start. While an old exe of this tool is still in the
    /// folder (kept because a shortcut to it could not be moved yet) it must still start, so only files
    /// named after the current exe go; the old exe's dll, json and loose libraries wait for a start
    /// after ToolRename.Cleanup has removed it. While Cleanup cannot remove it, they stay on purpose.
    /// </summary>
    public static IReadOnlyList<string> FirstStartFiles(string installDirectory, string currentExeName, bool singleFile)
    {
        var files = PreviousVersionFiles(installDirectory, currentExeName, singleFile);
        if (!ToolRename.OldExePaths(installDirectory, currentExeName).Any(File.Exists))
            return files;
        var current = Path.GetFileNameWithoutExtension(currentExeName);
        return files.Where(path => BaseOfProgramFile(Path.GetFileName(path)).Equals(current, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The leftover program files RemovePreviousVersion deletes. See StaleFiles for what is never listed.
    /// </summary>
    public static IReadOnlyList<string> PreviousVersionFiles(string installDirectory, string currentExeName, bool singleFile, IEnumerable<string>? alsoOwnExeNames = null) =>
        StaleFiles(installDirectory, currentExeName, name => IsPreviousProgramFile(name, singleFile), alsoOwnExeNames);

    /// <summary>
    /// The files the legacy install removes after a copy: this tool's own program files in the
    /// folder that the payload root does not carry.
    /// </summary>
    public static IReadOnlyList<string> LegacyStaleFiles(string sourceDirectory, string installDirectory, string currentExeName)
    {
        var payload = Directory.Exists(sourceDirectory)
            ? new HashSet<string>(Directory.EnumerateFiles(sourceDirectory).Select(path => Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] stale = [".dll", ".pdb", ".old", ".new", ".deps.json", ".runtimeconfig.json", ".exe"];
        // Another name of this tool (Coodsz and Friends.exe beside Coodsz Coop and Friends.exe) is left for
        // the new exe's first start, which moves its shortcuts and folds in its settings before removing it.
        return StaleFiles(installDirectory, currentExeName, name =>
            !payload.Contains(name) && stale.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            && !ToolNames.IsKnownExe(name));
    }

    /// <summary>
    /// Top level payload files that belong to a sibling tool whose exe is not in the install folder.
    /// The release zips carry every sibling so the shipped updater never removes one, but a folder
    /// that holds only this tool does not need them.
    /// </summary>
    public static IReadOnlyList<string> AbsentSiblingFiles(string sourceDirectory, string installDirectory, string currentExeName)
    {
        if (!Directory.Exists(sourceDirectory))
            return [];
        var own = OwnBaseNames(currentExeName);
        var others = SiblingBaseNames(currentExeName);
        var result = new List<string>();
        foreach (var name in Directory.EnumerateFiles(sourceDirectory).Select(path => Path.GetFileName(path)))
        {
            var owner = BaseOfProgramFile(name);
            if (own.Contains(owner) || !others.Contains(owner) || !ProgramSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!File.Exists(Path.Combine(installDirectory, owner + ".exe")))
                result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// Top level files of the folder that pass the candidate rule and that this tool owns (see
    /// IsOwnFile), minus what an update must never remove: this exe, the player's files and every
    /// other Coodsz exe and the files named after it (its .old, .new and .pdb too). When another Coodsz
    /// tool in the folder still runs as loose files (its exe sits next to a dll of the same name), the
    /// shared dlls are its too, so only this tool's own named files go.
    /// </summary>
    private static IReadOnlyList<string> StaleFiles(string installDirectory, string currentExeName, Func<string, bool> candidate, IEnumerable<string>? alsoOwnExeNames = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            return [];
        var files = Directory.EnumerateFiles(installDirectory).ToList();
        var names = new HashSet<string>(files.Select(path => Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase);
        var own = OwnBaseNames(currentExeName);
        foreach (var name in alsoOwnExeNames ?? [])
            own.Add(Path.GetFileNameWithoutExtension(name));
        var others = SiblingBaseNames(currentExeName);
        others.ExceptWith(own);
        var looseSibling = others.Any(name => names.Contains(name + ".exe") && names.Contains(name + ".dll"));
        var result = new List<string>();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name.Equals(currentExeName, StringComparison.OrdinalIgnoreCase) || IsKeptUserFile(name) || !candidate(name))
                continue;
            // An update that could not put a file back leaves only its .old and .new, so those stay until the
            // file itself is there again.
            var live = WithoutBackupSuffix(name);
            if (live != name && !names.Contains(live))
                continue;
            if (!IsOwnFile(name, currentExeName, own))
                continue;
            var owner = BaseOfProgramFile(name);
            if (others.Contains(owner) || (looseSibling && !own.Contains(owner)))
                continue;
            result.Add(path);
        }

        return result;
    }

    /// <summary>
    /// The ownership rule every update and first start cleanup follows. A file belongs to this tool
    /// when it is a program file (.exe, .dll, .deps.json, .runtimeconfig.json or .pdb, also with .old
    /// or .new after it) named after one of this tool's exe names, old or new, or one of the loose
    /// libraries this tool's own earlier payloads carried. An unknown exe or dll, and every sibling's
    /// file, is never this tool's to remove.
    /// </summary>
    public static bool IsOwnFile(string fileName, string currentExeName) =>
        IsOwnFile(Path.GetFileName(fileName), currentExeName, OwnBaseNames(currentExeName));

    private static bool IsOwnFile(string fileName, string currentExeName, HashSet<string> ownBaseNames)
    {
        var name = WithoutUpdateTag(fileName);
        foreach (var suffix in new[] { ".old", ".new" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }

        if (!ProgramSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return false;
        var owner = BaseOfProgramFile(name);
        if (ownBaseNames.Contains(owner))
            return true;
        // A loose library and its .pdb or .deps.json, but never an exe of that name.
        return !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && ToolNames.PreviousLooseFiles(currentExeName).Any(file => Path.GetFileNameWithoutExtension(file).Equals(owner, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ProgramSuffixes = [".deps.json", ".runtimeconfig.json", ".dll", ".pdb", ".exe"];

    /// <summary>The base names of every Coodsz exe that is not this tool's own.</summary>
    public static HashSet<string> SiblingBaseNames(string currentExeName)
    {
        var own = OwnBaseNames(currentExeName);
        var others = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ToolNames.KnownExeNames.Select(name => Path.GetFileNameWithoutExtension(name)))
        {
            if (!own.Contains(name))
                others.Add(name);
        }

        return others;
    }

    private static HashSet<string> OwnBaseNames(string currentExeName)
    {
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(currentExeName) };
        if (ToolNames.Find(currentExeName) is { } tool)
            own.UnionWith(tool.AllExeNames.Select(name => Path.GetFileNameWithoutExtension(name)));
        return own;
    }

    // "Tool.deps.json", "Tool.exe.old" and "Tool.dll" all belong to "Tool".
    private static string BaseOfProgramFile(string fileName)
    {
        var name = WithoutUpdateTag(fileName);
        foreach (var suffix in new[] { ".old", ".new" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }

        foreach (var suffix in new[] { ".deps.json", ".runtimeconfig.json", ".dll", ".pdb", ".exe" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        return name;
    }

    // A killed update helper leaves its copy as "Tool.exe.coodsz-update-12.new" (see UpdateApply.ReplaceExe),
    // which is read as "Tool.exe.new".
    private static string WithoutUpdateTag(string fileName)
    {
        if (!fileName.EndsWith(".new", StringComparison.OrdinalIgnoreCase))
            return fileName;
        var name = fileName[..^".new".Length];
        var dot = name.LastIndexOf('.');
        return dot > 0 && name[(dot + 1)..].StartsWith("coodsz-update-", StringComparison.OrdinalIgnoreCase)
            ? name[..dot] + ".new"
            : fileName;
    }

    // "Tool.exe.old" and "Tool.exe.coodsz-update-12.new" are copies of "Tool.exe".
    private static string WithoutBackupSuffix(string fileName)
    {
        var name = WithoutUpdateTag(fileName);
        foreach (var suffix in new[] { ".old", ".new" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        return fileName;
    }

    public static bool IsKeptUserFile(string fileName) =>
        KeptUserFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    public static bool IsPreviousProgramFile(string fileName, bool singleFile)
    {
        if (fileName.EndsWith(".old", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".new", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!singleFile)
            return false;
        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(400);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(400);
            }
        }
    }

    /// <summary>
    /// The plan for a legacy zip: every payload file except the player's, minus a sibling's files when that
    /// sibling is not in this folder, then this tool's own stale program files. UpdateApply runs it.
    /// </summary>
    public static UpdatePlan LegacyPlan(string sourceDirectory, string installDirectory, string exePath, int processId, string stageDirectory)
    {
        var exeName = Path.GetFileName(exePath);
        return new UpdatePlan(
            processId,
            stageDirectory,
            Path.Combine(installDirectory, "update-install.log"),
            exePath,
            SourceDirectory: sourceDirectory,
            InstallDirectory: installDirectory,
            SkipTopFiles: AbsentSiblingFiles(sourceDirectory, installDirectory, exeName),
            // Worked out here, while this process still knows the folder, so another Coodsz tool in
            // the same folder (and its loose files) is never removed.
            StaleFiles: LegacyStaleFiles(sourceDirectory, installDirectory, exeName),
            SiblingPrefixes: SiblingBaseNames(exeName).Select(name => name + ".").ToList(),
            // The helper checks its copy of the exe against this before the swap.
            ExeSha256: File.Exists(Path.Combine(sourceDirectory, exeName)) ? SuccessorInstall.Hash(Path.Combine(sourceDirectory, exeName)) : null);
    }

    /// <summary>
    /// The plan for a two exe zip. The helper checks the successor exe against the hash taken in
    /// process, copies the payload files beside it, then the exe last: to .new first, checked against the
    /// hash again, then swapped in with the exe in place kept as .old until the swap is done (see
    /// UpdateApply.ReplaceExe), so a failed copy never leaves a broken exe. Only when that copy is whole does
    /// it remove the old exe and the old loose files and start the new exe. Both stay when a shortcut
    /// still points at the old exe, until the new exe has moved it. When anything fails the old
    /// exe stays and starts again. Every step goes to update-install.log.
    /// </summary>
    public static UpdatePlan SuccessorPlan(SuccessorInstall install, int processId, string stageDirectory) =>
        new(processId, stageDirectory, Path.Combine(install.InstallDirectory, "update-install.log"), install.NewExePath, Successor: install);

    private static async Task DownloadAsync(string url, string destination, IProgress<UpdateStep>? progress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Coodsz-ReleaseCheck");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The update download failed.");

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long done = 0;
        var lastReport = DateTime.UtcNow;
        var lastPercent = -1;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            var percent = total is > 0 ? (int)(done * 100 / total.Value) : -1;
            var now = DateTime.UtcNow;
            if (percent == lastPercent && now - lastReport < TimeSpan.FromMilliseconds(200))
                continue;
            lastReport = now;
            lastPercent = percent;
            double? fraction = total is > 0 ? done / (double)total.Value : null;
            var message = percent >= 0
                ? "Downloading the update. " + percent + "% finished. Saved settings stay in this folder."
                : "Downloading the update. Saved settings stay in this folder.";
            progress?.Report(new UpdateStep(message, fraction));
        }
    }

    private static bool IsAD1259Server(string exe) =>
        exe.Contains("AD1259", StringComparison.OrdinalIgnoreCase) && exe.Contains("Server", StringComparison.OrdinalIgnoreCase);

    private static bool IsAD1259Client(string exe) =>
        exe.Contains("AD1259", StringComparison.OrdinalIgnoreCase) && exe.Contains("Client", StringComparison.OrdinalIgnoreCase);

    private static bool IsTaomServer(string exe) =>
        exe.Contains("TAOM", StringComparison.OrdinalIgnoreCase) && exe.Contains("Server", StringComparison.OrdinalIgnoreCase);

    private static bool IsTaomClient(string exe) =>
        exe.Contains("TAOM", StringComparison.OrdinalIgnoreCase) && exe.Contains("Client", StringComparison.OrdinalIgnoreCase);

    private static bool IsHost(string exe) =>
        exe.Equals("CoodszHost", StringComparison.OrdinalIgnoreCase)
        || exe.Contains("Coodsz Host", StringComparison.OrdinalIgnoreCase);

    private static bool IsFriends(string exe) =>
        exe.Contains("Friends", StringComparison.OrdinalIgnoreCase);
}
